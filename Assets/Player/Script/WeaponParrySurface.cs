using UnityEngine;

namespace BattlePvp.Combat
{
    // A defensive blade surface, independent of the attack damage collider.
    public sealed class WeaponParrySurface : MonoBehaviour
    {
        internal PlayerCombat Owner { get; private set; }
        internal HealthSystem Health { get; private set; }
        private BoxCollider _box;

        internal void Initialize(PlayerCombat owner)
        {
            Owner = owner;
            Health = owner.GetComponent<HealthSystem>();
            _box = gameObject.AddComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        internal void Sync(Transform blade, WeaponCatalog.Entry entry, bool active)
        {
            _box.enabled = active;
            if (!active) return;
            transform.SetPositionAndRotation(blade.position, blade.rotation);
            Vector3 scale = blade.lossyScale, parentScale = transform.parent.lossyScale;
            transform.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
            // Cover the blade itself, excluding the hands, hilt and pommel.
            _box.center = (entry.BladeBase + entry.BladeTip) * .5f;
            _box.size = new Vector3(entry.HitSize.x, entry.HitSize.y, Mathf.Abs(entry.BladeTip.z - entry.BladeBase.z));
        }

        internal bool Intercept(PlayerCombat attacker) => Owner != null && Owner.TryParryBlade(attacker);
    }
}
