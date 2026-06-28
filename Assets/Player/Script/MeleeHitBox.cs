using System.Collections.Generic;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    public class MeleeHitBox : MonoBehaviour
    {
        [SerializeField] private AttackProcessor _attackProcessor;
        [SerializeField] private AttackData _currentAttackData;

        private Collider _collider;
        private readonly HashSet<IDamageReceiver> _hitTargets = new HashSet<IDamageReceiver>();

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null)
                _collider.isTrigger = true;

            if (_attackProcessor == null)
                _attackProcessor = GetComponentInParent<AttackProcessor>();

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            DisableHitBox();
        }

        public void SetAttackData(AttackData data)
        {
            _currentAttackData = data;
        }

        public void EnableHitBox()
        {
            if (_collider != null)
                _collider.enabled = true;
            _hitTargets.Clear();

            ProcessCurrentOverlaps();
        }

        public void DisableHitBox()
        {
            if (_collider != null)
                _collider.enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryProcessHit(other);
        }

        private void ProcessCurrentOverlaps()
        {
            if (_collider == null)
                return;

            Bounds bounds = _collider.bounds;
            Collider[] overlaps = Physics.OverlapBox(
                bounds.center,
                bounds.extents,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < overlaps.Length; i++)
                TryProcessHit(overlaps[i]);
        }

        private void TryProcessHit(Collider other)
        {
            if (other == null || other.transform.root == transform.root)
                return;

            IDamageReceiver defender = other.GetComponent<IDamageReceiver>();
            if (defender == null)
                defender = other.GetComponentInParent<IDamageReceiver>();

            if (defender == null || _hitTargets.Contains(defender))
                return;

            if (NetworkClient.active && !NetworkServer.active && defender is HealthSystem)
                return;

            StatManager defenderStats = other.GetComponent<StatManager>();
            if (defenderStats == null)
                defenderStats = other.GetComponentInParent<StatManager>();

            if (defenderStats == null || _attackProcessor == null)
                return;

            Vector3 hitPosition = other.ClosestPoint(transform.position);
            _attackProcessor.ProcessHit(_currentAttackData, defenderStats, defender, hitPosition);
            _hitTargets.Add(defender);
        }
    }
}
