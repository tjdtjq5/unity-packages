using N4EGuard.Multiplayer;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS P2P + IPC 드라이버 구성자. Unity Netcode for Entities 제약에 맞춰 다음을 등록한다:
    ///   - Server: IPC + EOS Socket **병행 등록** (Netcode는 Server 드라이버 다중 등록 허용)
    ///   - Client: IPC 또는 EOS Socket **택일 등록** (Netcode는 Client 드라이버 1개만 허용 — 2개 이상 등록 시 InvalidOperationException)
    ///
    /// Client 택일 기준은 <see cref="MppmRoleDetector.ShouldClientUseSocket"/>에 위임한다:
    ///   - MPPM Virtual Player → 무조건 Socket (EditorPrefs 변경 없이 VP만 Socket 강제)
    ///   - 그 외 → Netcode 공식 <see cref="DefaultDriverBuilder.ClientUseSocketDriver"/> 위임
    ///     (NetworkSimulator / PlayType.Client / ServerWorld 유무 순으로 판정)
    ///
    /// 사용법:
    /// <code>
    /// public class MyBootstrap : ClientServerBootstrap
    /// {
    ///     public override bool Initialize(string defaultWorldName)
    ///     {
    ///         AutoConnectPort = 0;
    ///         NetworkStreamReceiveSystem.DriverConstructor = new EOSAndIpcDriverConstructor();
    ///         CreateDefaultClientServerWorlds();
    ///         return true;
    ///     }
    /// }
    /// </code>
    ///
    /// MPPM Virtual Player 처리:
    ///   <see cref="MppmRoleDetector"/>가 Main Editor/VP를 구분해 VP는 무조건 Socket으로 분기시킨다.
    ///   사용자가 MPPM PlayMode Tools Window에서 설정한 PlayType/NetworkSimulator 값을 변경하지 않는다.
    /// </summary>
    public class EOSAndIpcDriverConstructor : INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            var settings = DefaultDriverBuilder.GetNetworkClientSettings();

            if (MppmRoleDetector.ShouldClientUseSocket(netDebug))
            {
                var eosDriver = DefaultDriverBuilder.CreateClientNetworkDriver(
                    new EOSP2PNetworkInterface(), settings);
                driver.RegisterDriver(TransportType.Socket, eosDriver);
                Debug.Log($"[EOSAndIpcDriverConstructor] Client → EOS Socket (MPPM VP={MppmRoleDetector.IsVirtualPlayer})");
            }
            else
            {
                DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driver, netDebug, settings);
                Debug.Log("[EOSAndIpcDriverConstructor] Client → IPC (ServerWorld in-process)");
            }
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            var settings = DefaultDriverBuilder.GetNetworkServerSettings();

            DefaultDriverBuilder.RegisterServerIpcDriver(world, ref driver, netDebug, settings);

            var eosDriver = DefaultDriverBuilder.CreateServerNetworkDriver(
                new EOSP2PNetworkInterface(), settings);
            driver.RegisterDriver(TransportType.Socket, eosDriver);

            Debug.Log("[EOSAndIpcDriverConstructor] Server → IPC + EOS Socket 병행 등록");
        }
    }
}
