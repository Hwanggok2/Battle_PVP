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

        [Header("Aim/Crouch Hit Position")]
        [SerializeField] private float _aimForwardOffset = 0.8f;
        [SerializeField] private float _crouchVerticalOffset = -0.45f;

        private bool _hitBoxActive;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _aimDirection = Vector3.forward;
        private bool _isCrouching;
        private PlayerCombat _playerCombat;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _boxCollider = _collider as BoxCollider;
            if (_collider != null)
                _collider.isTrigger = true;

            if (_attackProcessor == null)
                _attackProcessor = GetComponentInParent<AttackProcessor>();

            _playerCombat = GetComponentInParent<PlayerCombat>();

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

        public void SetAttackContext(Vector3 aimDirection, bool isCrouching)
        {
            _aimDirection = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : transform.forward;
            _isCrouching = isCrouching;
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
            if (_useSweptHitDetection && _boxCollider != null)
                return;

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
            Quaternion hitRotation = GetHitRotation(sampleRotation);
            Vector3 center = GetHitCenter(samplePosition, hitRotation, scale);
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, scale) + Vector3.one * _sweepPadding;

            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _sweepResults,
                hitRotation,
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

        private Quaternion GetHitRotation(Quaternion fallbackRotation)
        {
            if (_aimDirection.sqrMagnitude <= 0.001f)
                return fallbackRotation;

            return Quaternion.LookRotation(_aimDirection.normalized, Vector3.up);
        }

        private Vector3 GetHitCenter(Vector3 samplePosition, Quaternion hitRotation, Vector3 scale)
        {
            Vector3 center = samplePosition + hitRotation * Vector3.Scale(_boxCollider.center, scale);
            center += _aimDirection.normalized * _aimForwardOffset;

            if (_isCrouching)
                center += Vector3.up * _crouchVerticalOffset;

            return center;
        }

        private void TryProcessHit(Collider other)
        {
            if (other == null || other.transform.root == transform.root)
                return;

            HitBodyPart bodyPart = other.GetComponent<HitBodyPart>();
            if (bodyPart == null)
                bodyPart = other.GetComponentInParent<HitBodyPart>();

            IDamageReceiver defender = other.GetComponent<IDamageReceiver>();
            if (defender == null)
                defender = other.GetComponentInParent<IDamageReceiver>();

            if (defender == null || _hitTargets.Contains(defender))
                return;

            // Player movement colliders/CharacterController are not damage hitboxes.
            // For players, only colliders marked with HitBodyPart can receive melee damage.
            if (defender is HealthSystem && bodyPart == null)
                return;

            if (NetworkClient.active && !NetworkServer.active && defender is HealthSystem)
                return;

            StatManager defenderStats = other.GetComponent<StatManager>();
            if (defenderStats == null)
                defenderStats = other.GetComponentInParent<StatManager>();

            if (defenderStats == null || _attackProcessor == null)
                return;

            float bodyPartMultiplier = bodyPart != null ? bodyPart.DamageMultiplier : 1f;

            Vector3 hitQueryPosition = transform.position + (_aimDirection.normalized * _aimForwardOffset);
            if (_isCrouching)
                hitQueryPosition += Vector3.up * _crouchVerticalOffset;

            Vector3 hitPosition = other.ClosestPoint(hitQueryPosition);
            if (_playerCombat != null && !_playerCombat.TryRegisterHitTarget(defender))
                return;

            _attackProcessor.ProcessHit(_currentAttackData, defenderStats, defender, hitPosition, bodyPartMultiplier: bodyPartMultiplier);
            _hitTargets.Add(defender);
        }
    }
}
