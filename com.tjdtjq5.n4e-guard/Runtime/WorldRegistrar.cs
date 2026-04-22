using Unity.Entities;

namespace N4EGuard
{
    /// <summary>
    /// Singleton registration helpers for Netcode multi-World environments.
    /// Replaces inline World.All iteration + idempotent singleton creation boilerplate.
    /// </summary>
    public static class WorldRegistrar
    {
        /// <summary>
        /// Creates or replaces a singleton component in the given World (idempotent).
        /// Destroys any existing entity with the same component type before creating.
        /// Safe to call repeatedly (Play mode restart, Domain Reload).
        /// </summary>
        public static Entity SetSingleton<T>(World world, T data)
            where T : unmanaged, IComponentData
        {
            var em = world.EntityManager;
            using var existing = em.CreateEntityQuery(typeof(T));
            if (existing.CalculateEntityCount() > 0)
                em.DestroyEntity(existing);

            var entity = em.CreateEntity(typeof(T));
            em.SetComponentData(entity, data);
            return entity;
        }

        /// <summary>
        /// Sets a singleton in all active Worlds. Returns the number of Worlds registered.
        /// </summary>
        public static int SetSingletonInAllWorlds<T>(T data)
            where T : unmanaged, IComponentData
        {
            int count = 0;
            foreach (var world in World.All)
            {
                if (!world.IsCreated) continue;
                SetSingleton(world, data);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Sets a singleton only if it doesn't already exist in the World.
        /// Returns true if registered, false if already present.
        /// Use this from LateWorldRegisterSystem to avoid overwriting existing data.
        /// </summary>
        public static bool TrySetSingleton<T>(World world, T data)
            where T : unmanaged, IComponentData
        {
            var em = world.EntityManager;
            using var existing = em.CreateEntityQuery(typeof(T));
            if (existing.CalculateEntityCount() > 0)
                return false;

            var entity = em.CreateEntity(typeof(T));
            em.SetComponentData(entity, data);
            return true;
        }
    }
}
