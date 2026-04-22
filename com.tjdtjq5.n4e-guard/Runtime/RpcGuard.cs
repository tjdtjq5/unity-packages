using Unity.Entities;
using Unity.NetCode;

namespace N4EGuard
{
    /// <summary>
    /// RPC send helpers.
    /// Reduces CreateEntity + SetComponentData + SendRpcCommandRequest boilerplate to one call.
    /// Supports both EntityManager (MB code) and EntityCommandBuffer (ISystem code).
    /// </summary>
    public static class RpcGuard
    {
        // -- EntityManager overloads (MB / non-Burst code) --

        /// <summary>Broadcasts an RPC to all connections.</summary>
        public static Entity Send<T>(EntityManager em, T rpc)
            where T : unmanaged, IRpcCommand
        {
            var entity = em.CreateEntity(typeof(T), typeof(SendRpcCommandRequest));
            em.SetComponentData(entity, rpc);
            return entity;
        }

        /// <summary>Sends an RPC to a specific connection (unicast).</summary>
        public static Entity Send<T>(EntityManager em, T rpc, Entity targetConnection)
            where T : unmanaged, IRpcCommand
        {
            var entity = em.CreateEntity(typeof(T), typeof(SendRpcCommandRequest));
            em.SetComponentData(entity, rpc);
            em.SetComponentData(entity, new SendRpcCommandRequest
            {
                TargetConnection = targetConnection
            });
            return entity;
        }

        // -- EntityCommandBuffer overloads (ISystem / Burst-adjacent code) --

        /// <summary>Broadcasts an RPC via ECB.</summary>
        public static Entity Send<T>(EntityCommandBuffer ecb, T rpc)
            where T : unmanaged, IRpcCommand
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, rpc);
            ecb.AddComponent(entity, new SendRpcCommandRequest());
            return entity;
        }

        /// <summary>Sends an RPC to a specific connection via ECB.</summary>
        public static Entity Send<T>(EntityCommandBuffer ecb, T rpc, Entity targetConnection)
            where T : unmanaged, IRpcCommand
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, rpc);
            ecb.AddComponent(entity, new SendRpcCommandRequest
            {
                TargetConnection = targetConnection
            });
            return entity;
        }
    }
}
