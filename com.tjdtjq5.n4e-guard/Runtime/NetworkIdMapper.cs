using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace N4EGuard
{
    /// <summary>
    /// Resolves entities across Worlds using NetworkId.
    /// Caches results to avoid per-frame linear search.
    /// Must be Disposed when no longer needed or when the bound World is destroyed.
    /// </summary>
    public class NetworkIdMapper : IDisposable
    {
        readonly Dictionary<int, Entity> _cache = new();
        World _world;
        EntityQuery _query;
        bool _disposed;

        /// <summary>
        /// Binds to a World for entity resolution.
        /// Replaces any previous binding (disposes old query).
        /// </summary>
        public void Bind(World world)
        {
            if (_query != default && _world != null && _world.IsCreated)
                _query.Dispose();

            _world = world;
            _cache.Clear();

            if (world != null && world.IsCreated)
            {
                _query = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<GhostOwner>());
            }
        }

        /// <summary>
        /// Resolves an Entity by NetworkId in the bound World.
        /// Returns cached result if valid, otherwise performs linear search.
        /// Returns Entity.Null if not found or World is invalid.
        /// </summary>
        public Entity Resolve(int networkId)
        {
            if (_world == null || !_world.IsCreated)
                return Entity.Null;

            var em = _world.EntityManager;

            // Cache hit + validation
            if (_cache.TryGetValue(networkId, out var cached))
            {
                if (em.Exists(cached))
                    return cached;
                _cache.Remove(networkId);
            }

            // Linear search fallback
            using var entities = _query.ToEntityArray(Allocator.Temp);
            using var owners = _query.ToComponentDataArray<GhostOwner>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (owners[i].NetworkId == networkId)
                {
                    _cache[networkId] = entities[i];
                    return entities[i];
                }
            }

            return Entity.Null;
        }

        /// <summary>Clears the cache. Call when ghost entities are destroyed/recreated.</summary>
        public void Invalidate()
        {
            _cache.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cache.Clear();
            if (_query != default && _world != null && _world.IsCreated)
                _query.Dispose();
            _query = default;
            _world = null;
        }
    }
}
