using Unity.Entities;
using Unity.NetCode;

namespace N4EGuard
{
    /// <summary>
    /// Netcode World lookup and host detection.
    /// Replaces inline ClientServerBootstrap iteration and DefaultGameObjectInjectionWorld direct usage.
    /// </summary>
    public static class NetcodeWorldHelper
    {
        /// <summary>Returns the first active ClientWorld, or null if none exists.</summary>
        public static World GetClientWorld()
        {
            foreach (var w in ClientServerBootstrap.ClientWorlds)
                if (w.IsCreated) return w;
            return null;
        }

        /// <summary>Returns the first active ServerWorld, or null if none exists.</summary>
        public static World GetServerWorld()
        {
            foreach (var w in ClientServerBootstrap.ServerWorlds)
                if (w.IsCreated) return w;
            return null;
        }

        /// <summary>
        /// Returns the world to use for visuals.
        /// Prefers ClientWorld, falls back to DefaultGameObjectInjectionWorld.
        /// Works in editor standalone and server-less environments.
        /// </summary>
        public static World GetVisualWorld()
        {
            var client = GetClientWorld();
            if (client != null) return client;
            return World.DefaultGameObjectInjectionWorld;
        }

        /// <summary>
        /// Determines if this process is actually hosting (MPPM-safe).
        /// Checks for NetworkStreamInGame connections in ServerWorld.
        /// Guest processes' ServerWorld has no listening connections, so this returns false.
        /// </summary>
        public static bool IsHost()
        {
            foreach (var w in ClientServerBootstrap.ServerWorlds)
            {
                if (!w.IsCreated) continue;
                using var query = w.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<NetworkStreamInGame>());
                if (query.CalculateEntityCount() > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Quick check if any ServerWorld exists.
        /// NOT MPPM-safe: guest processes may also have a ServerWorld.
        /// Use <see cref="IsHost"/> for accurate host detection.
        /// Useful before InGame when IsHost() would return false due to no connections yet.
        /// </summary>
        public static bool HasServerWorld()
        {
            return ClientServerBootstrap.ServerWorlds.Count > 0;
        }
    }
}
