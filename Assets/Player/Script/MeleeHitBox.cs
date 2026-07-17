using System.Collections.Generic;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        [Header("Debug")]
        [SerializeField] private bool _drawDebugHitPath = true;
        [SerializeField] private bool _drawDebugHitPathInGame = true;
        [SerializeField] private float _debugHitPathDuration = 3f;
        [SerializeField] private Color _debugHitPathColor = new Color(1f, 0.2f, 0.05f, 0.35f);
        [SerializeField] private float _debugHitPathLineWidth = 0.025f;

        private bool _hitBoxActive;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _aimDirection = Vector3.forward;
        private bool _isCrouching;
        private PlayerCombat _playerCombat;

        private readonly List<DebugHitBoxPose> _debugHitBoxPoses = new List<DebugHitBoxPose>(128);
        private readonly List<DebugHitBoxRenderer> _debugHitBoxRenderers = new List<DebugHitBoxRenderer>(128);
        private DebugHitBoxRenderer _currentDebugHitBoxRenderer;
        private Material _debugHitPathMaterial;

        private struct DebugHitBoxPose
        {
            public Vector3 Center;
            public Vector3 HalfExtents;
            public Quaternion Rotation;
            public float ExpireTime;
        }

        private sealed class DebugHitBoxRenderer
        {
            public LineRenderer Renderer;
            public float ExpireTime;
            public readonly List<Vector3> Positions = new List<Vector3>(256);
        }

        private sealed class DebugHitBoxPathLifetime : MonoBehaviour
        {
            private LineRenderer _renderer;
            private Color _baseColor;
            private float _createdAt;
            private float _duration;

            public void Initialize(LineRenderer renderer, Color baseColor, float duration)
            {
                _renderer = renderer;
                _baseColor = baseColor;
                _duration = Mathf.Max(0.01f, duration);
                _createdAt = Time.unscaledTime;
            }

            private void Update()
            {
                float remaining = Mathf.Clamp01(1f - ((Time.unscaledTime - _createdAt) / _duration));
                if (remaining <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }

                if (_renderer == null)
                    return;

                Color color = _baseColor;
                color.a *= remaining;
                _renderer.startColor = color;
                _renderer.endColor = color;
            }
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ScheduleEditorDebugHitBoxCleanup()
        {
            EditorApplication.delayCall += CleanupOrphanedDebugHitBoxPaths;
        }
#endif

        private void Awake()
        {
            CleanupOrphanedDebugHitBoxPaths();

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
                UpdateDebugHitBoxRenderers();
                return;
            }

            ProcessSweptBox();
            CaptureCurrentPose();

            UpdateDebugHitBoxRenderers();
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
            _currentDebugHitBoxRenderer = null;
            CaptureCurrentPose();

            ProcessCurrentOverlaps();
        }

        public void DisableHitBox()
        {
            _hitBoxActive = false;
            if (_collider != null)
                _collider.enabled = false;
        }

        private void OnDisable()
        {
            CleanupOwnedDebugHitBoxRenderers();
        }

        private void OnDestroy()
        {
            CleanupOwnedDebugHitBoxRenderers();
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

            RecordDebugHitBoxPose(center, halfExtents, hitRotation);

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

            if (NetworkClient.active && !NetworkServer.active && defender is HealthSystem targetHealth)
            {
                if (_playerCombat != null && !_playerCombat.TryRegisterHitTarget(defender))
                    return;

                float predictedBuffMultiplier = _playerCombat != null ? _playerCombat.ConsumeNextAttackDamageMultiplier() : 1f;
                float predictedDamage = _attackProcessor.PredictHitDamage(
                    _currentAttackData,
                    defenderStats,
                    bodyPartMultiplier * predictedBuffMultiplier);
                targetHealth.ShowPredictedPhysicalDamagePopup(hitPosition, predictedDamage, _playerCombat != null ? _playerCombat.netId : 0);
                _playerCombat?.RequestServerMeleeHit(
                    defender,
                    bodyPart != null ? bodyPart.Part : BodyPart.Body,
                    hitPosition);
                _hitTargets.Add(defender);
                return;
            }

            if (_playerCombat != null && !_playerCombat.TryRegisterHitTarget(defender))
                return;

            float attackBuffMultiplier = _playerCombat != null ? _playerCombat.ConsumeNextAttackDamageMultiplier() : 1f;
            _attackProcessor.ProcessHit(
                _currentAttackData,
                defenderStats,
                defender,
                hitPosition,
                bodyPartMultiplier: bodyPartMultiplier * attackBuffMultiplier,
                bodyPart: bodyPart != null ? bodyPart.Part : BodyPart.Body);
            _hitTargets.Add(defender);
        }

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
            DebugHitBoxRenderer debugRenderer = GetOrCreateCurrentDebugHitBoxRenderer(expireTime);
            debugRenderer.ExpireTime = expireTime;
            AppendBoxLinePositions(debugRenderer.Positions, center, halfExtents, rotation);
            debugRenderer.Renderer.positionCount = debugRenderer.Positions.Count;
            debugRenderer.Renderer.SetPositions(debugRenderer.Positions.ToArray());
        }

        private DebugHitBoxRenderer GetOrCreateCurrentDebugHitBoxRenderer(float expireTime)
        {
            if (_currentDebugHitBoxRenderer != null && _currentDebugHitBoxRenderer.Renderer != null)
                return _currentDebugHitBoxRenderer;

            GameObject lineObject = new GameObject("Debug Melee HitBox Path");

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.positionCount = 0;
            lineRenderer.widthMultiplier = Mathf.Max(0.001f, _debugHitPathLineWidth);
            lineRenderer.material = GetDebugHitPathMaterial();
            lineRenderer.startColor = _debugHitPathColor;
            lineRenderer.endColor = _debugHitPathColor;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            var lifetime = lineObject.AddComponent<DebugHitBoxPathLifetime>();
            lifetime.Initialize(lineRenderer, _debugHitPathColor, _debugHitPathDuration);

            _currentDebugHitBoxRenderer = new DebugHitBoxRenderer
            {
                Renderer = lineRenderer,
                ExpireTime = expireTime
            };
            _debugHitBoxRenderers.Add(_currentDebugHitBoxRenderer);
            return _currentDebugHitBoxRenderer;
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

        private static void AppendBoxLinePositions(List<Vector3> positions, Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            Vector3 a = new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 b = new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 c = new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 d = new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 e = new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 f = new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 g = new Vector3(halfExtents.x, halfExtents.y, halfExtents.z);
            Vector3 h = new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z);

            AddLine(positions, center, rotation, a, b);
            AddLine(positions, center, rotation, b, c);
            AddLine(positions, center, rotation, c, d);
            AddLine(positions, center, rotation, d, a);
            AddLine(positions, center, rotation, e, f);
            AddLine(positions, center, rotation, f, g);
            AddLine(positions, center, rotation, g, h);
            AddLine(positions, center, rotation, h, e);
            AddLine(positions, center, rotation, a, e);
            AddLine(positions, center, rotation, b, f);
            AddLine(positions, center, rotation, c, g);
            AddLine(positions, center, rotation, d, h);
        }

        private static void AddLine(List<Vector3> positions, Vector3 center, Quaternion rotation, Vector3 from, Vector3 to)
        {
            positions.Add(center + rotation * from);
            positions.Add(center + rotation * to);
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
                    if (_currentDebugHitBoxRenderer == debugRenderer)
                        _currentDebugHitBoxRenderer = null;

                    _debugHitBoxRenderers.RemoveAt(i);
                    continue;
                }

                Color color = _debugHitPathColor;
                color.a *= remaining;
                debugRenderer.Renderer.startColor = color;
                debugRenderer.Renderer.endColor = color;
            }
        }

        private void CleanupOwnedDebugHitBoxRenderers()
        {
            for (int i = _debugHitBoxRenderers.Count - 1; i >= 0; i--)
            {
                DebugHitBoxRenderer debugRenderer = _debugHitBoxRenderers[i];
                if (debugRenderer.Renderer != null)
                    Destroy(debugRenderer.Renderer.gameObject);
            }

            _debugHitBoxRenderers.Clear();
            _debugHitBoxPoses.Clear();
            _currentDebugHitBoxRenderer = null;
        }

        private static void CleanupOrphanedDebugHitBoxPaths()
        {
#if UNITY_EDITOR
            LineRenderer[] lineRenderers = Application.isPlaying
                ? FindObjectsByType<LineRenderer>(FindObjectsSortMode.None)
                : Resources.FindObjectsOfTypeAll<LineRenderer>();
#else
            LineRenderer[] lineRenderers = FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
#endif
            for (int i = 0; i < lineRenderers.Length; i++)
            {
                LineRenderer lineRenderer = lineRenderers[i];
                if (lineRenderer == null || lineRenderer.gameObject.name != "Debug Melee HitBox Path")
                    continue;

#if UNITY_EDITOR
                if (!Application.isPlaying && EditorUtility.IsPersistent(lineRenderer.gameObject))
                    continue;
#endif

                if (Application.isPlaying)
                    Destroy(lineRenderer.gameObject);
                else
                    DestroyImmediate(lineRenderer.gameObject);
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
    }
}
