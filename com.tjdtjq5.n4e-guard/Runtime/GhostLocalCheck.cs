using Unity.Entities;
using Unity.NetCode;

namespace N4EGuard
{
    /// <summary>
    /// GhostOwnerIsLocal safe check utility.
    /// IEnableableComponent trap: HasComponent alone always returns true for owner-predicted ghosts.
    /// ECS query WithAll checks enabled state automatically - this helper is only needed
    /// when using EntityManager directly from MB code.
    /// </summary>
    public static class GhostLocalCheck
    {
        /// <summary>
        /// Checks if the entity is owned by the local player.
        /// Combines HasComponent + IsComponentEnabled for correct enableable component check.
        /// </summary>
        public static bool IsLocalOwner(EntityManager em, Entity entity)
        {
            return em.HasComponent<GhostOwnerIsLocal>(entity)
                && em.IsComponentEnabled<GhostOwnerIsLocal>(entity);
        }
    }
}
