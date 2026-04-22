using Epic.OnlineServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using PlayEveryWare.EpicOnlineServices;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS P2P를 Unity Transport의 INetworkInterface로 래핑하는 구현체.
    /// managed API를 사용하므로 WrapToUnmanaged()으로 감싸서 NetworkDriver에 주입한다.
    ///
    /// 사용법:
    ///   var eos = new EOSP2PNetworkInterface();
    ///   var driver = NetworkDriver.Create(eos.WrapToUnmanaged(), settings);
    ///
    /// 전제 조건:
    ///   1. EOSManager.Instance.Init() 완료
    ///   2. EOSManager.Instance.StartConnectLoginWithDeviceToken() 완료
    /// </summary>
    public struct EOSP2PNetworkInterface : INetworkInterface
    {
        // struct 기본값 0 = 미초기화. EOSContextStore에서 인덱스 0은 예약됨.
        const int k_InvalidContext = 0;

        int m_ContextHandle;
        NativeArray<NetworkEndpoint> m_LocalEndpoint;

        public NetworkEndpoint LocalEndpoint =>
            m_LocalEndpoint.IsCreated ? m_LocalEndpoint[0] : default;

        // Deferred init: -1 = uninitialized, -2 = pending EOS, >0 = ready
        const int k_PendingEOS = -2;

        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            if (m_ContextHandle != k_InvalidContext)
            {
                Debug.LogError("[EOS Transport] Already initialized");
                return -1;
            }

            m_LocalEndpoint = new NativeArray<NetworkEndpoint>(1, Allocator.Persistent);

            // Poller를 먼저 생성 (EOS 없이도 endpoint 매핑 가능)
            var go = new GameObject("[EOS Transport Poller]");
            Object.DontDestroyOnLoad(go);
            var poller = go.AddComponent<EOSTransportPoller>();
            poller.Prepare(); // NativeQueue + Active 등록 — EOS 불필요

            var ctx = new EOSContext
            {
                SocketName = "EOSTransport",
                Poller = poller,
            };
            m_ContextHandle = EOSContextStore.Alloc(ctx);

            // EOS가 이미 준비됐으면 즉시 바인딩, 아니면 deferred
            if (!TryBindEOS())
            {
                Debug.Log("[EOS Transport] Initialized (deferred — waiting for EOS login)");
            }

            return 0;
        }

        /// <summary>
        /// Attempts to bind EOS P2P to the already-created Poller.
        /// Called from Initialize (immediate) and ScheduleSend/Receive (deferred).
        /// </summary>
        bool TryBindEOS()
        {
            if (m_ContextHandle <= k_InvalidContext) return false;

            var ctx = EOSContextStore.Get(m_ContextHandle);
            if (ctx == null || ctx.Poller == null) return false;
            if (ctx.P2P != null) return true; // already bound

            if (EOSManager.Instance == null ||
                EOSManager.Instance.GetEOSPlatformInterface() == null)
                return false;

            var p2p = EOSManager.Instance.GetEOSP2PInterface();
            if (p2p == null) return false;

            var localUserId = EOSManager.Instance.GetProductUserId();
            if (localUserId == null || !localUserId.IsValid()) return false;

            ctx.P2P = p2p;
            ctx.LocalUserId = localUserId;
            ctx.Poller.Bind(p2p, localUserId, "EOSTransport");

            Debug.Log($"[EOS Transport] Bound to EOS. ProductUserId: {localUserId}");
            return true;
        }

        public int Bind(NetworkEndpoint endpoint)
        {
            if (m_ContextHandle == k_InvalidContext)
            {
                Debug.LogError("[EOS Transport] Not initialized");
                return -1;
            }

            // EOS P2P는 IP:Port 바인딩이 없음 — 가상 endpoint 저장
            m_LocalEndpoint[0] = endpoint;

#if EOS_DEBUG
            Debug.Log($"[EOS Transport] Bound to {endpoint}");
#endif
            return 0;
        }

        public int Listen()
        {
            Debug.Log($"[EOS Transport][DIAG] Listen() invoked. ctxHandle={m_ContextHandle}");
            TryBindEOS(); // ensure EOS is bound before listening

            if (m_ContextHandle <= k_InvalidContext)
            {
                Debug.LogError("[EOS Transport][DIAG] Listen: Not initialized or EOS not ready");
                return -1;
            }

            var ctx = EOSContextStore.Get(m_ContextHandle);
            if (ctx?.Poller == null)
            {
                Debug.LogError("[EOS Transport][DIAG] Listen: Poller not available");
                return -1;
            }

            ctx.Poller.StartListening();

            Debug.Log("[EOS Transport][DIAG] Listen() completed — StartListening called");
            return 0;
        }

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            TryBindEOS(); // deferred EOS binding (no-op if already bound)

            if (m_ContextHandle <= k_InvalidContext)
                return dep;

            var ctx = EOSContextStore.Get(m_ContextHandle);
            if (ctx?.Poller == null)
                return dep;

            // Poller의 수신 NativeQueue에서 패킷을 꺼내 ReceiveQueue로 이동
            var job = new DrainReceiveJob
            {
                Source = ctx.Poller.ReceiveQueue,
                ReceiveQueue = arguments.ReceiveQueue,
                ReceiveResult = arguments.ReceiveResult,
            };

            return job.Schedule(dep);
        }

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            TryBindEOS(); // deferred EOS binding (no-op if already bound)

            if (m_ContextHandle <= k_InvalidContext)
                return dep;

            var ctx = EOSContextStore.Get(m_ContextHandle);
            if (ctx?.Poller == null)
                return dep;

            // SendQueue에서 패킷을 꺼내 Poller의 송신 NativeQueue로 이동
            var job = new FillSendJob
            {
                Destination = ctx.Poller.SendQueue,
                SendQueue = arguments.SendQueue,
            };

            var handle = job.Schedule(dep);
            ctx.Poller.LastSendJobHandle = handle;
            return handle;
        }

        public void Dispose()
        {
            if (m_ContextHandle > k_InvalidContext)
            {
                var ctx = EOSContextStore.Get(m_ContextHandle);
                if (ctx?.Poller != null)
                    Object.Destroy(ctx.Poller.gameObject);

                EOSContextStore.Free(m_ContextHandle);
                m_ContextHandle = k_InvalidContext;
            }

            if (m_LocalEndpoint.IsCreated)
                m_LocalEndpoint.Dispose();

#if EOS_DEBUG
            Debug.Log("[EOS Transport] Disposed");
#endif
        }

        // ── Jobs ──

        /// <summary>
        /// Poller의 수신 NativeQueue → Unity Transport ReceiveQueue로 패킷 이동.
        /// </summary>
        struct DrainReceiveJob : IJob
        {
            public NativeQueue<TransportPacket> Source;
            public PacketsQueue ReceiveQueue;
            public OperationResult ReceiveResult;

            public unsafe void Execute()
            {
                while (Source.TryDequeue(out var packet))
                {
                    if (!ReceiveQueue.EnqueuePacket(out var processor))
                    {
                        ReceiveResult.ErrorCode = -10; // NetworkReceiveQueueFull
                        return;
                    }

                    processor.EndpointRef = packet.Endpoint;
                    processor.AppendToPayload(packet.Data, packet.DataLength);
                }
            }
        }

        /// <summary>
        /// Unity Transport SendQueue → Poller의 송신 NativeQueue로 패킷 이동.
        /// </summary>
        struct FillSendJob : IJob
        {
            public NativeQueue<TransportPacket> Destination;
            [ReadOnly] public PacketsQueue SendQueue;

            public unsafe void Execute()
            {
                for (int i = 0; i < SendQueue.Count; i++)
                {
                    var processor = SendQueue[i];
                    if (!processor.IsCreated || processor.Length <= 0)
                        continue;

                    var packet = new TransportPacket
                    {
                        Endpoint = processor.EndpointRef,
                        DataLength = processor.Length,
                    };

                    var src = processor.GetUnsafePayloadPtr();
                    var len = packet.DataLength;
                    if (len > TransportPacket.MaxPayloadSize)
                        len = TransportPacket.MaxPayloadSize;

                    for (int b = 0; b < len; b++)
                        packet.Data[b] = ((byte*)src)[b];

                    packet.DataLength = len;
                    Destination.Enqueue(packet);
                }
            }
        }
    }
}
