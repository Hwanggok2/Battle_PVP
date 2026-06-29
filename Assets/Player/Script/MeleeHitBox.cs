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
        private BoxCollider _boxCollider;
        private readonly HashSet<IDamageReceiver> _hitTargets = new HashSet<IDamageReceiver>();
        private readonly Collider[] _sweepResults = new Collider[32];

        [Header("Swept Hit Detection")]
        [SerializeField] private bool _useSweptHitDetection = true;
        [SerializeField] private float _sweepSampleSpacing = 0.15f;
        [SerializeField] private int _maxSweepSamples = 8;
        [SerializeField] private float _sweepPadding = 0.02f;

        private bool _hitBoxActive;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _boxCollider = _collider as BoxCollider;
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

        private void LateUpdate()
        {
            if (!_hitBoxActive || !_useSweptHitDetection || _boxCollider == null)
                return;

            ProcessSweptBox();
            CaptureCurrentPose();
        }

        public void SetAttackData(AttackData data)
        {
            _currentAttackData = data;
        }

        public void EnableHitBox()
        {
            if (_collider != null)
                _collider.enabled = true;

            _hitBoxActive = true;
            _hitTargets.Clear();
            CaptureCurrentPose();

            ProcessCurrentOverlaps();
        }

        public void DisableHitBox()
        {
            _hitBoxActive = false;
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

            if (_boxCollider != null)
            {
                ProcessBoxOverlap(transform.position, transform.rotation);
                return;
            }

            Bounds bounds = _collider.bounds;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                _sweepResults,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
                TryProcessHit(_sweepResults[i]);
        }

        private void ProcessSweptBox()
        {
            float distance = Vector3.Distance(_previousPosition, transform.position);
            float angle = Quaternion.Angle(_previousRotation, transform.rotation);
            int distanceSamples = Mathf.CeilToInt(distance / Mathf.Max(0.01f, _sweepSampleSpacing));
            int angleSamples = Mathf.CeilToInt(angle / 25f);
            int samples = Mathf.Clamp(Mathf.Max(distanceSamples, angleSamples), 1, _maxSweepSamples);

            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 samplePosition = Vector3.Lerp(_previousPosition, transform.position, t);
                Quaternion sampleRotation = Quaternion.Slerp(_previousRotation, transform.rotation, t);
                ProcessBoxOverlap(samplePosition, sampleRotation);
            }
        }

        private void ProcessBoxOverlap(Vector3 samplePosition, Quaternion sampleRotation)
        {
            if (_boxCollider == null)
                return;

            Vector3 scale = Abs(transform.lossyScale);
            Vector3 center = samplePosition + sampleRotation * Vector3.Scale(_boxCollider.center, scale);
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, scale) + Vector3.one * _sweepPadding;

            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _sweepResults,
                sampleRotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
                TryProcessHit(_sweepResults[i]);
        }

        private void CaptureCurrentPose()
        {
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
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
