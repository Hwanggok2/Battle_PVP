using System.Collections.Generic;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BattlePvp.Combat
{
    [DefaultExecutionOrder(1100)]
    public class MeleeHitBox : MonoBehaviour
    {
        [SerializeField] private AttackProcessor _attackProcessor;
        [SerializeField] private AttackData _currentAttackData;

        private Collider _collider;
        private BoxCollider _boxCollider;
        private readonly HashSet<IDamageReceiver> _hitTargets = new HashSet<IDamageReceiver>();
        private readonly CombatPhysicsQuery _sweepQuery = new CombatPhysicsQuery();
        private readonly ContactComparer _contactComparer = new ContactComparer();

        private sealed class ContactComparer : IComparer<Collider>
        {
            public Vector3 Center;
            public int Compare(Collider a, Collider b)
            {
                if (a == b) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int distance = (a.ClosestPoint(Center) - Center).sqrMagnitude.CompareTo((b.ClosestPoint(Center) - Center).sqrMagnitude);
                if (distance != 0) return distance;
                distance = (a.bounds.center - Center).sqrMagnitude.CompareTo((b.bounds.center - Center).sqrMagnitude);
                return distance != 0 ? distance : a.GetInstanceID().CompareTo(b.GetInstanceID());
            }
        }
        [SerializeField] private LayerMask _targetLayers = ~0;

        [Header("Swept Hit Detection")]
        [SerializeField] private bool _useSweptHitDetection = true;
        [SerializeField] private float _sweepSampleSpacing = 0.15f;
        [SerializeField] private float _sweepPadding = 0.02f;

        [Header("Debug")]
        [SerializeField] private bool _drawDebugHitPath = false;
        [SerializeField] private bool _drawDebugHitPathInGame = false;
        [SerializeField] private float _debugHitPathDuration = 3f;
        [SerializeField] private Color _debugHitPathColor = new Color(1f, 0.2f, 0.05f, 0.35f);
        [SerializeField] private float _debugHitPathLineWidth = 0.025f;

        private bool _hitBoxActive;
        private Transform _poseSource;
        internal Transform PoseSource => _poseSource != null ? _poseSource : transform;

        public void SetPoseSource(Transform source)
        {
            if (_poseSource == source) return;
            _poseSource = source;
            _hasSweepPose = false;
            _serverHitPoses.Clear();
            _pendingInitialOverlap = _hitBoxActive;
            CaptureCurrentPose();
        }
        private bool _pendingInitialOverlap;
        private bool _pendingEnd;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private PlayerCombat _playerCombat;
        private Animator _sampleAnimator;
        private int _sampleState;
        private AnimationHitWindow _animationWindow;
        private bool _hasSweepPose;
        private float _previousPhase;
        private readonly CombatHitPoseHistory _serverHitPoses = new CombatHitPoseHistory();

        public bool ValidateServerHit(Vector3 point, double hitTime, Vector3 attackerPosition)
        {
            if (!NetworkServer.active) return false;
            // Only poses sampled after animation/aim correction are authoritative.
            return _serverHitPoses.Contains(hitTime, point - attackerPosition, 0.25d);
        }

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
            if (_hitBoxActive) CombatPhysicsQuery.SyncAnimatedTransforms();
            if (_sampleAnimator != null && _useSweptHitDetection && _boxCollider != null)
            {
                var state = _sampleAnimator.GetCurrentAnimatorStateInfo(1);
                if (state.fullPathHash == _sampleState)
                {
                    float phase = state.normalizedTime;
                    if (_hitBoxActive && _hasSweepPose && _animationWindow.Clip(_previousPhase, phase, out float from, out float to))
                        ProcessSweptBox(from, to);
                    else if (_hitBoxActive && !_hasSweepPose && phase >= _animationWindow.Start && phase <= _animationWindow.End)
                        ProcessCurrentOverlaps();
                    CaptureCurrentPose();
                    _hasSweepPose = true;
                    _previousPhase = phase;
                    _pendingInitialOverlap = false;
                    if (_pendingEnd) DisableHitBox();
                    UpdateDebugHitBoxRenderers();
                    return;
                }
            }
            // Animation events precede the additive aim pose. Query only after that pose is applied.
            if (_hitBoxActive && _pendingInitialOverlap)
            {
                _pendingInitialOverlap = false;
                CaptureCurrentPose();
                ProcessCurrentOverlaps();
                UpdateDebugHitBoxRenderers();
                if (_pendingEnd) DisableHitBox();
                return;
            }
            if (!_hitBoxActive || !_useSweptHitDetection || _boxCollider == null)
            {
                UpdateDebugHitBoxRenderers();
                if (_pendingEnd) DisableHitBox();
                return;
            }

            ProcessSweptBox();
            CaptureCurrentPose();
            if (_pendingEnd) DisableHitBox();

            UpdateDebugHitBoxRenderers();
        }

        public void SetAttackData(AttackData data)
        {
            _currentAttackData = data;
            _serverHitPoses.Clear();
            _hasSweepPose = false;
            _sampleAnimator = null;
        }

        public void BeginAnimationSampling(Animator animator)
        {
            _sampleAnimator = animator;
            _sampleState = animator.GetCurrentAnimatorStateInfo(1).fullPathHash;
            _animationWindow = AnimationHitWindow.Melee(animator, 1);
            CaptureCurrentPose();
            _previousPhase = 0;
            _hasSweepPose = true;
        }

        public void EnableHitBox()
        {
            if (_collider != null)
                _collider.enabled = true;

            _hitBoxActive = true;
            _hitTargets.Clear();
            _currentDebugHitBoxRenderer = null;
            _pendingInitialOverlap = true;
            _pendingEnd = false;
        }

        // Animation events run before the final corrected pose of the hit window.
        public void EndHitWindow() { if (_hitBoxActive) _pendingEnd = true; }

        public void DisableHitBox()
        {
            _hitBoxActive = false;
            _pendingInitialOverlap = false;
            _pendingEnd = false;
            if (_collider != null)
                _collider.enabled = false;
        }

        private void OnDisable()
        {
            DisableHitBox();
            CleanupOwnedDebugHitBoxRenderers();
        }

        private void OnDestroy()
        {
            CleanupOwnedDebugHitBoxRenderers();
            if (_debugHitPathMaterial != null) Destroy(_debugHitPathMaterial);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_useSweptHitDetection && _boxCollider != null)
                return;

            TryProcessHit(other, transform.position);
        }

        private void ProcessCurrentOverlaps()
        {
            if (_collider == null)
                return;

            if (_boxCollider != null)
            {
                ProcessBoxOverlap(PoseSource.position, PoseSource.rotation);
                return;
            }

            Bounds bounds = _collider.bounds;
            int count = _sweepQuery.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, _targetLayers);

            ProcessContacts(count, bounds.center);
        }

        private void ProcessSweptBox(float from = 0f, float to = 1f)
        {
            if (_sampleAnimator != null && _playerCombat != null)
            {
                float phase = _sampleAnimator.GetCurrentAnimatorStateInfo(1).normalizedTime;
                if (_playerCombat.TrySampleMeleeMotion(_previousPhase, out Pose expectedFrom) &&
                    _playerCombat.TrySampleMeleeMotion(phase, out Pose expectedTo))
                {
                    var actualFrom = new Pose(_previousPosition, _previousRotation);
                    var actualTo = new Pose(PoseSource.position, PoseSource.rotation);
                    int steps = Mathf.Clamp(Mathf.CeilToInt((phase - _previousPhase) * (to - from) * 240), 1, 128);
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = Mathf.Lerp(from, to, i / (float)steps);
                        _playerCombat.TrySampleMeleeMotion(Mathf.Lerp(_previousPhase, phase, t), out Pose sample);
                        sample = MeleeMotionSample.MatchEndpoints(sample, expectedFrom, expectedTo, actualFrom, actualTo, t);
                        ProcessBoxOverlap(sample.position, sample.rotation);
                    }
                    return;
                }
            }
            Vector3 start = Vector3.Lerp(_previousPosition, PoseSource.position, from);
            Vector3 end = Vector3.Lerp(_previousPosition, PoseSource.position, to);
            Quaternion startRotation = Quaternion.Slerp(_previousRotation, PoseSource.rotation, from);
            Quaternion endRotation = Quaternion.Slerp(_previousRotation, PoseSource.rotation, to);
            float distance = Vector3.Distance(start, end);
            float angle = Quaternion.Angle(startRotation, endRotation);
            Vector3 scale = Abs(PoseSource.lossyScale);
            float radius = Vector3.Scale(Abs(_boxCollider.center) + _boxCollider.size * .5f, scale).magnitude;
            float thickness = Mathf.Min(_boxCollider.size.x * scale.x, _boxCollider.size.y * scale.y) + 2f * _sweepPadding;
            float spacing = Mathf.Max(.01f, Mathf.Min(_sweepSampleSpacing, thickness));
            int samples = Mathf.Clamp(Mathf.CeilToInt((distance + angle * Mathf.Deg2Rad * radius) / spacing), 1, 64);

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 samplePosition = Vector3.Lerp(start, end, t);
                Quaternion sampleRotation = Quaternion.Slerp(startRotation, endRotation, t);
                ProcessBoxOverlap(samplePosition, sampleRotation);
            }
        }

        private void ProcessBoxOverlap(Vector3 samplePosition, Quaternion sampleRotation)
        {
            if (_boxCollider == null)
                return;

            Vector3 scale = Abs(PoseSource.lossyScale);
            Quaternion hitRotation = sampleRotation;
            Vector3 center = GetHitCenter(samplePosition, hitRotation, scale);
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, scale) + Vector3.one * _sweepPadding;

            RecordDebugHitBoxPose(center, halfExtents, hitRotation);
            if (NetworkServer.active)
                _serverHitPoses.Record(NetworkTime.time, center - transform.root.position, halfExtents, hitRotation);

            int count = _sweepQuery.OverlapBox(center, halfExtents, hitRotation, _targetLayers);

            ProcessContacts(count, center);
        }

        private void ProcessContacts(int count, Vector3 center)
        {
            // Physics overlap order is unspecified. Resolve simultaneous regions from the sampled blade,
            // while retaining the chronological order of the sweep and one hit per target per attack.
            _contactComparer.Center = center;
            System.Array.Sort(_sweepQuery.Colliders, 0, count, _contactComparer);
            for (int i = 0; i < count; i++) TryProcessHit(_sweepQuery.Colliders[i], center);
        }

        private void CaptureCurrentPose()
        {
            _previousPosition = PoseSource.position;
            _previousRotation = PoseSource.rotation;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private Vector3 GetHitCenter(Vector3 samplePosition, Quaternion hitRotation, Vector3 scale)
        {
            return samplePosition + hitRotation * Vector3.Scale(_boxCollider.center, scale);
        }

        private void TryProcessHit(Collider other, Vector3 hitQueryPosition)
        {
            if (!_hitBoxActive || !isActiveAndEnabled || other == null || other.transform.root == transform.root)
                return;

            if (_playerCombat != null && NetworkClient.active && !NetworkServer.active && !_playerCombat.isLocalPlayer)
                return;

            CombatHitTargets.Resolve(other, out IDamageReceiver defender,
                out StatManager defenderStats, out HitBodyPart bodyPart);

            if (defender == null || _hitTargets.Contains(defender))
                return;

            // Player movement colliders/CharacterController are not damage hitboxes.
            // For players, only colliders marked with HitBodyPart can receive melee damage.
            if (defender is HealthSystem && bodyPart == null)
                return;

            if (defenderStats == null || _attackProcessor == null)
                return;

            float bodyPartMultiplier = bodyPart != null ? bodyPart.DamageMultiplier : 1f;

            Vector3 hitPosition = other.ClosestPoint(hitQueryPosition);
            if (NetworkServer.active && !CombatValidation.HasClearPath(
                _playerCombat != null ? _playerCombat.transform.position + Vector3.up : transform.position,
                hitPosition, transform, other.transform))
                return;

            if (NetworkClient.active && !NetworkServer.active && defender is HealthSystem)
            {
                if (_playerCombat != null && !_playerCombat.TryRegisterHitTarget(defender))
                    return;

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
                bodyPart: bodyPart != null ? bodyPart.Part : BodyPart.Body,
                popupPredictionId: _playerCombat != null ? _playerCombat.CurrentAttackPredictionId : 0);
            _hitTargets.Add(defender);
        }

        private void RecordDebugHitBoxPose(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            if (!Debug.isDebugBuild || !_drawDebugHitPath || _debugHitPathDuration <= 0f)
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
            int previousCount = debugRenderer.Positions.Count;
            AppendBoxLinePositions(debugRenderer.Positions, center, halfExtents, rotation);
            debugRenderer.Renderer.positionCount = debugRenderer.Positions.Count;
            for (int i = previousCount; i < debugRenderer.Positions.Count; i++)
                debugRenderer.Renderer.SetPosition(i, debugRenderer.Positions[i]);
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
            float now = Time.time;
            for (int i = _debugHitBoxPoses.Count - 1; i >= 0; i--)
                if (_debugHitBoxPoses[i].ExpireTime <= now) _debugHitBoxPoses.RemoveAt(i);
            if (_debugHitBoxRenderers.Count == 0)
                return;

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
