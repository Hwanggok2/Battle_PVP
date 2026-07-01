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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("Debug")]
        [SerializeField] private bool _drawDebugHitPath = true;
        [SerializeField] private bool _drawDebugHitPathInGame = true;
        [SerializeField] private float _debugHitPathDuration = 3f;
        [SerializeField] private Color _debugHitPathColor = new Color(1f, 0.2f, 0.05f, 0.35f);
        [SerializeField] private float _debugHitPathLineWidth = 0.025f;
#endif

        private bool _hitBoxActive;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _aimDirection = Vector3.forward;
        private bool _isCrouching;
        private PlayerCombat _playerCombat;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<DebugHitBoxPose> _debugHitBoxPoses = new List<DebugHitBoxPose>(128);
        private readonly List<DebugHitBoxRenderer> _debugHitBoxRenderers = new List<DebugHitBoxRenderer>(128);
        private Material _debugHitPathMaterial;

        private struct DebugHitBoxPose
        {
            public Vector3 Center;
            public Vector3 HalfExtents;
            public Quaternion Rotation;
            public float ExpireTime;
        }

        private struct DebugHitBoxRenderer
        {
            public LineRenderer Renderer;
            public float ExpireTime;
        }
#endif

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
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UpdateDebugHitBoxRenderers();
#endif
                return;
            }

            ProcessSweptBox();
            CaptureCurrentPose();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateDebugHitBoxRenderers();
#endif
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordDebugHitBoxPose(center, halfExtents, hitRotation);
#endif

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void RecordDebugHitBoxPose(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            if (!_drawDebugHitPath || _debugHitPathDuration <= 0f)
                return;

            float expireTime = Time.time + _debugHitPathDuration;
            _debugHitBoxPoses.Add(new DebugHitBoxPose
            {
                Center = center,
                HalfExtents = halfExtents,
                Rotation = rotation,
                ExpireTime = expireTime
            });

            if (_drawDebugHitPathInGame && Application.isPlaying)
                CreateDebugHitBoxRenderer(center, halfExtents, rotation, expireTime);
        }

        private void CreateDebugHitBoxRenderer(Vector3 center, Vector3 halfExtents, Quaternion rotation, float expireTime)
        {
            GameObject lineObject = new GameObject("Debug Melee HitBox");
            lineObject.hideFlags = HideFlags.DontSave;
            lineObject.transform.SetPositionAndRotation(center, rotation);

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = false;
            lineRenderer.positionCount = 24;
            lineRenderer.widthMultiplier = Mathf.Max(0.001f, _debugHitPathLineWidth);
            lineRenderer.material = GetDebugHitPathMaterial();
            lineRenderer.startColor = _debugHitPathColor;
            lineRenderer.endColor = _debugHitPathColor;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.SetPositions(BuildBoxLinePositions(halfExtents));

            _debugHitBoxRenderers.Add(new DebugHitBoxRenderer
            {
                Renderer = lineRenderer,
                ExpireTime = expireTime
            });
        }

        private Material GetDebugHitPathMaterial()
        {
            if (_debugHitPathMaterial != null)
                return _debugHitPathMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            _debugHitPathMaterial = new Material(shader);
            _debugHitPathMaterial.hideFlags = HideFlags.DontSave;
            return _debugHitPathMaterial;
        }

        private static Vector3[] BuildBoxLinePositions(Vector3 halfExtents)
        {
            Vector3 a = new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 b = new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 c = new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 d = new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 e = new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 f = new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 g = new Vector3(halfExtents.x, halfExtents.y, halfExtents.z);
            Vector3 h = new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z);

            return new[]
            {
                a, b, b, c, c, d, d, a,
                e, f, f, g, g, h, h, e,
                a, e, b, f, c, g, d, h
            };
        }

        private void UpdateDebugHitBoxRenderers()
        {
            if (_debugHitBoxRenderers.Count == 0)
                return;

            float now = Time.time;
            for (int i = _debugHitBoxRenderers.Count - 1; i >= 0; i--)
            {
                DebugHitBoxRenderer debugRenderer = _debugHitBoxRenderers[i];
                if (debugRenderer.Renderer == null)
                {
                    _debugHitBoxRenderers.RemoveAt(i);
                    continue;
                }

                float remaining = Mathf.Clamp01((debugRenderer.ExpireTime - now) / Mathf.Max(0.01f, _debugHitPathDuration));
                if (remaining <= 0f)
                {
                    Destroy(debugRenderer.Renderer.gameObject);
                    _debugHitBoxRenderers.RemoveAt(i);
                    continue;
                }

                Color color = _debugHitPathColor;
                color.a *= remaining;
                debugRenderer.Renderer.startColor = color;
                debugRenderer.Renderer.endColor = color;
            }
        }

        private void OnDrawGizmos()
        {
            if (!_drawDebugHitPath || _debugHitBoxPoses == null)
                return;

            float now = Application.isPlaying ? Time.time : 0f;
            for (int i = _debugHitBoxPoses.Count - 1; i >= 0; i--)
            {
                DebugHitBoxPose pose = _debugHitBoxPoses[i];
                if (Application.isPlaying && pose.ExpireTime < now)
                {
                    _debugHitBoxPoses.RemoveAt(i);
                    continue;
                }

                float remaining = Application.isPlaying
                    ? Mathf.Clamp01((pose.ExpireTime - now) / Mathf.Max(0.01f, _debugHitPathDuration))
                    : 1f;

                Color color = _debugHitPathColor;
                color.a *= remaining;

                Matrix4x4 oldMatrix = Gizmos.matrix;
                Color oldColor = Gizmos.color;
                Gizmos.matrix = Matrix4x4.TRS(pose.Center, pose.Rotation, Vector3.one);
                Gizmos.color = color;
                Gizmos.DrawWireCube(Vector3.zero, pose.HalfExtents * 2f);
                Gizmos.matrix = oldMatrix;
                Gizmos.color = oldColor;
            }
        }
#endif
    }
}
