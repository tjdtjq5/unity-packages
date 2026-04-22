using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS P2P API는 메인 스레드에서만 호출 가능하므로,
    /// Update()에서 수신 polling, LateUpdate()에서 송신을 처리한다.
    /// Job(ScheduleSend/ScheduleReceive)과는 NativeQueue로 통신한다.
    /// </summary>
    internal class EOSTransportPoller : MonoBehaviour
    {
        const int k_MaxReceivePerFrame = 256;

        // 진단 로그 throttle (Send/Recv가 스팸이라 첫 N회만 출력)
        const int k_DiagLogLimit = 10;
        int _sendLogCount;
        int _recvLogCount;
        int _dropLogCount;

        // 싱글톤 접근 (Bridge 레지스트리 패턴 — EOSTransportUtility에서 사용)
        internal static EOSTransportPoller Active { get; private set; }

        // NativeQueue — Job과 공유
        NativeQueue<TransportPacket> _receiveQueue;
        NativeQueue<TransportPacket> _sendQueue;

        // Endpoint ↔ ProductUserId 매핑 — 프로세스 전역 공유
        // Server Driver / Client Driver가 각자 Poller 인스턴스를 만들기 때문에
        // instance-local map이면 RegisterRemotePeer가 한 Poller에 기록해도
        // 다른 Poller의 FlushSend에서 매핑 miss로 DROP되는 구조적 버그 발생.
        // static 공유로 해결.
        static readonly Dictionary<ProductUserId, NetworkEndpoint> s_UserToEndpoint = new();
        static readonly Dictionary<NetworkEndpoint, ProductUserId> s_EndpointToUser = new();
        static ushort s_NextPort = 2; // 1 = 로컬, 2~ = 리모트

        // EOS 참조
        P2PInterface _p2p;
        ProductUserId _localUserId;
        SocketId _socketId;

        // EOS notification ID (해제용)
        ulong _connectionRequestNotifyId;
        ulong _connectionClosedNotifyId;
        bool _listening;

        // 재사용 버퍼
        byte[] _receiveBuffer;
        byte[] _sendBuffer;

        bool _bound;

        // FillSendJob 완료 보장용 — LateUpdate에서 Complete 후 FlushSend
        public JobHandle LastSendJobHandle;

        public NativeQueue<TransportPacket> ReceiveQueue => _receiveQueue;
        public NativeQueue<TransportPacket> SendQueue => _sendQueue;

        void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
        }

        /// <summary>
        /// Phase 1: NativeQueue + Active 등록. EOS 없이 호출 가능.
        /// Initialize 시점에 호출. GetOrCreateEndpoint() 사용 가능해짐.
        /// </summary>
        public void Prepare()
        {
            _receiveQueue = new NativeQueue<TransportPacket>(Allocator.Persistent);
            _sendQueue = new NativeQueue<TransportPacket>(Allocator.Persistent);
            Active = this;
        }

        /// <summary>
        /// Phase 2: EOS P2P 바인딩. EOS 로그인 완료 후 호출.
        /// </summary>
        public void Bind(P2PInterface p2p, ProductUserId localUserId, string socketName)
        {
            _p2p = p2p;
            _localUserId = localUserId;
            _socketId = new SocketId { SocketName = socketName };
            _receiveBuffer = new byte[TransportPacket.MaxPayloadSize];
            _sendBuffer = new byte[TransportPacket.MaxPayloadSize];
            _bound = true;

            Debug.Log($"[EOS Poller][DIAG] Bound to EOS. LocalPUID={localUserId} SocketName={socketName}");
        }

        /// <summary>
        /// 서버 모드: EOS P2P 연결 요청/종료 알림을 등록한다.
        /// EOSP2PNetworkInterface.Listen()에서 호출됨.
        /// </summary>
        public void StartListening()
        {
            if (_listening) return;

            var requestOptions = new AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = _localUserId,
                SocketId = _socketId,
            };
            _connectionRequestNotifyId = _p2p.AddNotifyPeerConnectionRequest(
                ref requestOptions, null, OnConnectionRequest);

            var closedOptions = new AddNotifyPeerConnectionClosedOptions
            {
                LocalUserId = _localUserId,
                SocketId = _socketId,
            };
            _connectionClosedNotifyId = _p2p.AddNotifyPeerConnectionClosed(
                ref closedOptions, null, OnConnectionClosed);

            _listening = true;

            Debug.Log($"[EOS Poller][DIAG] StartListening done. LocalPUID={_localUserId} SocketName={_socketId.SocketName} reqNotifyId={_connectionRequestNotifyId} closedNotifyId={_connectionClosedNotifyId}");
        }

        void OnConnectionRequest(ref OnIncomingConnectionRequestInfo info)
        {
            var incomingSocket = info.SocketId.HasValue ? info.SocketId.Value.SocketName : "<null>";
            Debug.Log($"[EOS Poller][DIAG] ★ OnConnectionRequest fired! from={info.RemoteUserId} socket={incomingSocket}");

            var acceptOptions = new AcceptConnectionOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = info.RemoteUserId,
                SocketId = _socketId,
            };

            var result = _p2p.AcceptConnection(ref acceptOptions);

            if (result != Result.Success)
            {
                Debug.LogError($"[EOS Poller][DIAG] AcceptConnection failed: {result}");
                return;
            }

            GetOrCreateEndpoint(info.RemoteUserId);

            Debug.Log($"[EOS Poller][DIAG] Accepted connection from {info.RemoteUserId}");
        }

        void OnConnectionClosed(ref OnRemoteConnectionClosedInfo info)
        {
            Debug.Log($"[EOS Poller][DIAG] ★ OnConnectionClosed from={info.RemoteUserId} reason={info.Reason}");
            if (s_UserToEndpoint.TryGetValue(info.RemoteUserId, out var endpoint))
            {
                s_EndpointToUser.Remove(endpoint);
                s_UserToEndpoint.Remove(info.RemoteUserId);
            }
        }

        /// <summary>
        /// ProductUserId에 대응하는 가상 NetworkEndpoint를 조회하거나 새로 할당한다.
        /// </summary>
        public NetworkEndpoint GetOrCreateEndpoint(ProductUserId userId)
        {
            if (s_UserToEndpoint.TryGetValue(userId, out var existing))
                return existing;

            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(s_NextPort++);
            s_UserToEndpoint[userId] = endpoint;
            s_EndpointToUser[endpoint] = userId;

            Debug.Log($"[EOS Poller][DIAG] Mapped PUID={userId} → endpoint={endpoint} (port={endpoint.Port} nextPort={s_NextPort}) [shared map]");
            return endpoint;
        }

        void Update()
        {
            if (!_bound) return;
            PollReceive();
        }

        void LateUpdate()
        {
            if (!_bound) return;
            LastSendJobHandle.Complete();
            FlushSend();
        }

        /// <summary>
        /// EOS P2P에서 수신된 패킷을 polling하여 _receiveQueue에 적재.
        /// </summary>
        unsafe void PollReceive()
        {
            var receiveOptions = new ReceivePacketOptions
            {
                LocalUserId = _localUserId,
                MaxDataSizeBytes = (uint)TransportPacket.MaxPayloadSize,
            };

            for (int i = 0; i < k_MaxReceivePerFrame; i++)
            {
                ProductUserId peerId = null;
                var outSocketId = default(SocketId);
                byte outChannel;
                uint bytesWritten;

                var segment = new System.ArraySegment<byte>(_receiveBuffer);
                var result = _p2p.ReceivePacket(
                    ref receiveOptions,
                    ref peerId,
                    ref outSocketId,
                    out outChannel,
                    segment,
                    out bytesWritten);

                if (result != Result.Success)
                    break;

                var endpoint = GetOrCreateEndpoint(peerId);

                var packet = new TransportPacket
                {
                    Endpoint = endpoint,
                    DataLength = (int)bytesWritten,
                };

                fixed (byte* src = _receiveBuffer)
                {
                    for (int b = 0; b < bytesWritten; b++)
                        packet.Data[b] = src[b];
                }

                _receiveQueue.Enqueue(packet);

                if (_recvLogCount < k_DiagLogLimit)
                {
                    _recvLogCount++;
                    Debug.Log($"[EOS Poller][DIAG] ★ Recv #{_recvLogCount} from PUID={peerId} len={bytesWritten}B socket={outSocketId.SocketName}");
                }
            }
        }

        /// <summary>
        /// _sendQueue에서 패킷을 꺼내 EOS P2P로 송신.
        /// </summary>
        unsafe void FlushSend()
        {
            while (_sendQueue.TryDequeue(out var packet))
            {
                if (!s_EndpointToUser.TryGetValue(packet.Endpoint, out var remoteUserId))
                {
                    if (_dropLogCount < k_DiagLogLimit)
                    {
                        _dropLogCount++;
                        Debug.LogWarning($"[EOS Poller][DIAG] ★ DROP #{_dropLogCount} unmapped endpoint={packet.Endpoint} len={packet.DataLength}B (sharedMap.Count={s_EndpointToUser.Count})");
                    }
                    continue;
                }

                var len = packet.DataLength;
                if (len > _sendBuffer.Length)
                    len = _sendBuffer.Length;

                byte* src = packet.Data;
                for (int b = 0; b < len; b++)
                    _sendBuffer[b] = src[b];

                var sendOptions = new SendPacketOptions
                {
                    LocalUserId = _localUserId,
                    RemoteUserId = remoteUserId,
                    SocketId = _socketId,
                    Channel = 0,
                    AllowDelayedDelivery = true,
                    Reliability = PacketReliability.UnreliableUnordered,
                    DisableAutoAcceptConnection = false,
                    Data = new System.ArraySegment<byte>(_sendBuffer, 0, len),
                };

                var result = _p2p.SendPacket(ref sendOptions);

                if (result != Result.Success)
                {
                    Debug.LogError($"[EOS Poller][DIAG] ★ SendPacket FAIL to={remoteUserId} len={len}B result={result}");
                }
                else if (_sendLogCount < k_DiagLogLimit)
                {
                    _sendLogCount++;
                    Debug.Log($"[EOS Poller][DIAG] Send #{_sendLogCount} to PUID={remoteUserId} len={len}B → Success");
                }
            }
        }

        void OnApplicationQuit()
        {
            TryRemoveNotifications();
        }

#if UNITY_EDITOR
        void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                TryRemoveNotifications();
                // static map 리셋 — 다음 Play 시 stale endpoint 재사용 방지
                s_UserToEndpoint.Clear();
                s_EndpointToUser.Clear();
                s_NextPort = 2;
            }
        }
#endif

        /// <summary>
        /// EOS SDK가 살아있음이 확인될 때만 P2P notification을 제거한다.
        /// OnDestroy 시점에는 EOSManager가 이미 SDK를 shutdown한 상태일 수 있어
        /// _p2p 호출이 네이티브 크래시(EOS_P2P_RemoveNotifyPeerConnectionRequest)를 일으킨다.
        /// 따라서 ExitingPlayMode / OnApplicationQuit에서 먼저 제거하고,
        /// OnDestroy는 fallback으로 남겨둔다 (이미 제거됐으면 _listening=false라 no-op).
        /// </summary>
        void TryRemoveNotifications()
        {
            if (!_listening) return;
            if (_p2p == null) return;
            if (EOSManager.Instance == null || EOSManager.Instance.GetEOSPlatformInterface() == null) return;

            _p2p.RemoveNotifyPeerConnectionRequest(_connectionRequestNotifyId);
            _p2p.RemoveNotifyPeerConnectionClosed(_connectionClosedNotifyId);
            _listening = false;

#if EOS_DEBUG
            Debug.Log("[EOS Poller] Notifications removed (pre-destroy)");
#endif
        }

        void OnDestroy()
        {
            // Job이 아직 NativeQueue를 참조 중인데 Dispose하면 네이티브 크래시.
            LastSendJobHandle.Complete();

            // fallback — ExitingPlayMode/OnApplicationQuit에서 이미 처리됐으면 no-op.
            TryRemoveNotifications();

            if (_receiveQueue.IsCreated) _receiveQueue.Dispose();
            if (_sendQueue.IsCreated) _sendQueue.Dispose();

            if (Active == this) Active = null;
            _bound = false;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif

#if EOS_DEBUG
            Debug.Log("[EOS Poller] Destroyed");
#endif
        }
    }
}
