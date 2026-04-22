using Epic.OnlineServices;
using Unity.Networking.Transport;
using UnityEngine;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS Transport의 public static API.
    /// 게임 코드에서 매칭/로비 후 원격 피어를 등록할 때 사용한다.
    ///
    /// 사용법 (클라이언트):
    ///   var serverEndpoint = EOSTransportUtility.RegisterRemotePeer(serverProductUserId);
    ///   driver.Connect(serverEndpoint);
    /// </summary>
    public static class EOSTransportUtility
    {
        /// <summary>
        /// 원격 피어의 ProductUserId를 등록하고 대응하는 가상 NetworkEndpoint를 반환한다.
        /// 클라이언트가 서버에 연결하기 전에 호출해야 한다.
        /// </summary>
        public static NetworkEndpoint RegisterRemotePeer(ProductUserId remoteUserId)
        {
            if (remoteUserId == null || !remoteUserId.IsValid())
            {
                Debug.LogError("[EOS Transport] Invalid remote ProductUserId");
                return default;
            }

            var poller = EOSTransportPoller.Active;
            if (poller == null)
            {
                Debug.LogError(
                    "[EOS Transport] Transport not initialized. EOSP2PNetworkInterface가 등록된 NetworkDriver가 없습니다.\n" +
                    "해결:\n" +
                    "  NetworkStreamReceiveSystem.DriverConstructor = new EOSAndIpcDriverConstructor();\n" +
                    "또는 직접 등록:\n" +
                    "  driver.RegisterDriver(TransportType.Socket,\n" +
                    "      DefaultDriverBuilder.CreateClientNetworkDriver(new EOSP2PNetworkInterface(), settings));\n" +
                    "진단: EOSNetworkDiagnostics.Log() 로 현재 World/Driver/Connection 상태를 확인하세요.");
                return default;
            }

            var ep = poller.GetOrCreateEndpoint(remoteUserId);
            Debug.Log($"[EOS Utility][DIAG] RegisterRemotePeer(PUID={remoteUserId}) → endpoint={ep}");
            return ep;
        }
    }
}
