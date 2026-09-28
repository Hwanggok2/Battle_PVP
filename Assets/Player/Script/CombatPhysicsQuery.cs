using System;
using UnityEngine;
using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    internal static class CombatHitTargets
    {
        public static void Resolve(Collider collider, out IDamageReceiver receiver,
            out StatManager stats, out HitBodyPart bodyPart)
        {
            ServerPoseHistory cache = collider.GetComponentInParent<ServerPoseHistory>();
            if (cache != null && cache.TryResolveHitCollider(collider, out bodyPart, out HealthSystem health, out stats))
            {
                receiver = health;
                return;
            }
            receiver = collider.GetComponentInParent<IDamageReceiver>();
            stats = collider.GetComponentInParent<StatManager>();
            bodyPart = collider.GetComponentInParent<HitBodyPart>();
            // A training stand is scenery, not an unmarked torso. Legacy single-box targets remain supported.
            if (receiver is DummyHealth dummy && dummy.RequiresBodyPartHitboxes && bodyPart == null)
            { receiver = null; stats = null; }
        }
    }

    /// <summary>Reusable queries retry a full buffer so crowd density cannot silently drop targets.</summary>
    public sealed class CombatPhysicsQuery
    {
        private const int MaximumReusableCapacity = 4096;
        private Collider[] _colliders = new Collider[32];
        private RaycastHit[] _hits = new RaycastHit[32];
        public Collider[] Colliders => _colliders;
        public RaycastHit[] Hits => _hits;

        public int OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, int layers = ~0)
        {
            while (true)
            {
                int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _colliders, rotation,
                    layers, QueryTriggerInteraction.Collide);
                if (count < _colliders.Length) return count;
                if (_colliders.Length >= MaximumReusableCapacity)
                {
                    _colliders = Physics.OverlapBox(center, halfExtents, rotation, layers, QueryTriggerInteraction.Collide);
                    return _colliders.Length;
                }
                Array.Resize(ref _colliders, _colliders.Length * 2);
            }
        }

        public int Raycast(Vector3 origin, Vector3 direction, float distance, int layers = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            while (true)
            {
                int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance, layers, triggers);
                if (count < _hits.Length) return count;
                if (_hits.Length >= MaximumReusableCapacity)
                {
                    _hits = Physics.RaycastAll(origin, direction, distance, layers, triggers);
                    return _hits.Length;
                }
                Array.Resize(ref _hits, _hits.Length * 2);
            }
        }

        public int CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance,
            int layers = Physics.DefaultRaycastLayers)
        {
            while (true)
            {
                int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _hits, distance,
                    layers, QueryTriggerInteraction.Ignore);
                if (count < _hits.Length) return count;
                if (_hits.Length >= MaximumReusableCapacity)
                {
                    _hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance, layers, QueryTriggerInteraction.Ignore);
                    return _hits.Length;
                }
                Array.Resize(ref _hits, _hits.Length * 2);
            }
        }

        public int BoxCast(Vector3 center, Vector3 halfExtents, Quaternion rotation, Vector3 direction,
            float distance, int layers = ~0)
        {
            while (true)
            {
                int count = Physics.BoxCastNonAlloc(center, halfExtents, direction, _hits, rotation,
                    distance, layers, QueryTriggerInteraction.Collide);
                if (count < _hits.Length) return count;
                if (_hits.Length >= MaximumReusableCapacity)
                {
                    _hits = Physics.BoxCastAll(center, halfExtents, direction, rotation,
                        distance, layers, QueryTriggerInteraction.Collide);
                    return _hits.Length;
                }
                Array.Resize(ref _hits, _hits.Length * 2);
            }
        }
    }
}
