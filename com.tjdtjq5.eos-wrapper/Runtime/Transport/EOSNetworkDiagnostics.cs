using System.Text;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS 네트워킹 상태 덤프 유틸. 연결 실패 원인을 진단할 때 호출한다.
    ///
    /// 출력 항목:
    ///   - <see cref="ClientServerBootstrap.ServerWorld"/> / <see cref="ClientServerBootstrap.ClientWorld"/>
    ///   - 현재 <see cref="NetworkStreamReceiveSystem.DriverConstructor"/> 타입
    ///   - 모든 World의 IsServer/IsClient
    ///   - Client/Server World의 NetworkStreamConnection / NetworkId / PendingConnect / PendingListen 개수
    ///   - <see cref="EOSTransportPoller"/> 바인딩 상태
    ///
    /// 사용법:
    ///   var state = EOSNetworkDiagnostics.DumpState();  // string으로 받음
    ///   EOSNetworkDiagnostics.Log();                    // 콘솔 출력
    ///
    /// 주의: EntityQuery를 쓰므로 메인 스레드에서만 호출.
    /// </summary>
    public static class EOSNetworkDiagnostics
    {
        public static string DumpState()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== EOS Network Diagnostics ===");

            var serverWorld = ClientServerBootstrap.ServerWorld;
            var clientWorld = ClientServerBootstrap.ClientWorld;
            sb.AppendLine($"ServerWorld: {(serverWorld != null ? serverWorld.Name : "null")}");
            sb.AppendLine($"ClientWorld: {(clientWorld != null ? clientWorld.Name : "null")}");

            var dc = NetworkStreamReceiveSystem.DriverConstructor;
            sb.AppendLine($"DriverConstructor: {(dc != null ? dc.GetType().FullName : "null")}");

            sb.AppendLine("--- All Worlds ---");
            foreach (var w in World.All)
            {
                if (!w.IsCreated) continue;
                sb.AppendLine($"  {w.Name} IsServer={w.IsServer()} IsClient={w.IsClient()}");
            }

            DumpWorldConnections(sb, clientWorld, "ClientWorld");
            DumpWorldConnections(sb, serverWorld, "ServerWorld");

            sb.AppendLine("--- EOS Transport Poller ---");
            sb.AppendLine($"  Active: {(EOSTransportPoller.Active != null ? "bound" : "null")}");

            return sb.ToString();
        }

        public static void Log() => Debug.Log(DumpState());

        static void DumpWorldConnections(StringBuilder sb, World world, string label)
        {
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            sb.AppendLine($"--- {label} ---");
            sb.AppendLine($"  NetworkStreamConnection: {em.CreateEntityQuery(typeof(NetworkStreamConnection)).CalculateEntityCount()}");
            sb.AppendLine($"  NetworkId:               {em.CreateEntityQuery(typeof(NetworkId)).CalculateEntityCount()}");
            sb.AppendLine($"  PendingConnect:          {em.CreateEntityQuery(typeof(NetworkStreamRequestConnect)).CalculateEntityCount()}");
            sb.AppendLine($"  PendingListen:           {em.CreateEntityQuery(typeof(NetworkStreamRequestListen)).CalculateEntityCount()}");
        }
    }
}
