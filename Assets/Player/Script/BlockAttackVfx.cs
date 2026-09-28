using BattlePvp.Logic;
using BattlePvp.UI;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace BattlePvp.Combat
{
    /// <summary>World-space sword afterimages; never used for hit detection.</summary>
    [DefaultExecutionOrder(20)]
    public sealed class BlockAttackVfx : MonoBehaviour
    {
        [SerializeField] private Mesh _cube;
        [SerializeField] private Material _material;
        [SerializeField] private Material _accentMaterial;
        [SerializeField] private Transform _blade;
        [SerializeField] private Vector3 _bladeBase = new Vector3(0, 0, .14f);
        [SerializeField] private Vector3 _bladeTip = new Vector3(0, 0, 1.09f);
        [SerializeField] private Material _bladeMaterial;
        private readonly BladeTrailGeometry _bladeTrail = new BladeTrailGeometry();
        private readonly List<Vector3> _bladeVertices = new List<Vector3>(1024);
        private readonly List<Color> _bladeColors = new List<Color>(1024);
        private readonly List<int> _bladeTriangles = new List<int>(1536);
        private Mesh _bladeMesh;
        private Animator _animator;
        private int _animationState;
        private bool _emitting, _finishEmission;
        private float _swingDuration;
        private MaterialPropertyBlock _bladeProperties;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LocalNearFade = Shader.PropertyToID("_LocalNearFade");
        private readonly Matrix4x4[] _trail = new Matrix4x4[18];
        private readonly Matrix4x4[] _sparks = new Matrix4x4[12];
        private Vector3 _origin;
        private Quaternion _rotation;
        private float _start = -10;
        private int _serial;
        private bool _bow, _local;
        private HealthSystem _health;
        private NetworkIdentity _identity;
        private Light _light;
        private BattlePvp.Stats.StatManager _stats;
        private BattlePvp.VFX.VFXLinker _statVfx;
        private Color _strokeColor = new Color(.04f, .28f, 1f, .3f);
        private void Awake()
        {
            _health = GetComponent<HealthSystem>(); _identity = GetComponent<NetworkIdentity>();
            _animator = GetComponent<Animator>();
            _stats = GetComponent<BattlePvp.Stats.StatManager>();
            _statVfx = GetComponent<BattlePvp.VFX.VFXLinker>();
            var lightObject = new GameObject("Attack Glow"); lightObject.transform.SetParent(transform, false);
            _light = lightObject.AddComponent<Light>(); _light.type = LightType.Point;
            _light.color = new Color(.2f, .85f, 1f); _light.range = 4; _light.shadows = LightShadows.None; _light.enabled = false;
        }
        public void Play(bool bow = false, float swingDuration = .5f)
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!CanShowAttackEffects(scene) ||
                _cube == null || _material == null || (_health != null && _health.IsDead)) return;
            _local = !NetworkClient.active || (_identity != null && _identity.isLocalPlayer);
            if (NetworkServer.active && !NetworkClient.active) return;
            var camera = _local && InputModeRules.UsesFpsLook(scene) ? Camera.main : null;
            // Anchor to the attacker in world space, never to a lobby/showcase camera.
            _origin = transform.position + Vector3.up * 1.2f;
            _rotation = camera != null ? camera.transform.rotation : transform.rotation;
            _bow = bow; _start = Time.time; _serial++;
            if (_stats != null)
                _strokeColor = BattlePvp.Stats.StatVfxColor.Resolve(_stats.CurrentIdentity, _stats.GetStatsCopy(),
                    _statVfx != null ? _statVfx.GetStatColor(BattlePvp.Stats.StatKind.STR) : Color.red,
                    _statVfx != null ? _statVfx.GetStatColor(BattlePvp.Stats.StatKind.AGI) : Color.green,
                    _statVfx != null ? _statVfx.GetStatColor(BattlePvp.Stats.StatKind.CON) : Color.yellow,
                    _statVfx != null ? _statVfx.GetStatColor(BattlePvp.Stats.StatKind.DEF) : Color.blue);
            _strokeColor.a = .3f;
            _swingDuration = Mathf.Max(.08f, swingDuration);
            _animationState = _animator != null ? _animator.GetCurrentAnimatorStateInfo(1).fullPathHash : 0;
            _emitting = !bow; _finishEmission = false;
            if (bow) _bladeTrail.Clear(); else _bladeTrail.BeginStroke();
        }
        private static bool CanShowAttackEffects(string scene) => scene == "Lobby" || InputModeRules.UsesFpsLook(scene);
        public void StopMelee() { if (!_bow) { _emitting = false; _finishEmission = false; } }
        // Animation events precede LateUpdate: retain the last corrected strike pose once.
        public void EndMeleeEmission() { if (!_bow && _emitting) _finishEmission = true; }
        private void OnDisable()
        {
            _start = -10; _emitting = _finishEmission = false; _bladeTrail.Clear();
            if (_light != null) _light.enabled = false;
        }
        private void OnDestroy()
        {
            if (_bladeMesh == null) return;
            if (Application.isPlaying) Destroy(_bladeMesh); else DestroyImmediate(_bladeMesh);
        }
        private void LateUpdate()
        {
            float age = Time.time - _start;
            if (_start < 0) return;
            if (age < 0 || (_bow && age >= .34f) ||
                !CanShowAttackEffects(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) ||
                (_health != null && _health.IsDead) || (_local && (GameInputController.IsPaused || GameInputController.IsTextInputActive)))
            { OnDisable(); return; }
            if (!_bow) { UpdateBladeTrail(age); return; }
            int count = LocalGameSettings.Current.quality == 0 ? 12 : 18;
            float flip = _serial % 2 == 0 ? -1 : 1;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)(count - 1), life = age - u * .14f;
                float fade = life < 0 ? 0 : Mathf.Clamp01(1 - life / .16f);
                float arc = u * Mathf.PI;
                var position = new Vector3(flip * (.95f - 1.9f * u), .12f - .45f * u + .3f * Mathf.Sin(arc), .8f + .45f * Mathf.Sin(arc));
                if (_bow) position = new Vector3((i % 2 == 0 ? -1 : 1) * .1f, -.12f, .85f + age * 10 + u * .3f);
                float taper = .35f + .65f * Mathf.Sin(arc);
                _trail[i] = Matrix4x4.TRS(_origin + _rotation * position,
                    _rotation * Quaternion.Euler(0, 0, flip * (-28f + 48f * u)),
                    new Vector3(_bow ? .035f : .16f, .035f * taper, .015f) * fade);
            }
            int sparkCount = LocalGameSettings.Current.quality == 0 ? 6 : 12;
            for (int i = 0; i < sparkCount; i++)
            {
                float u = i / (float)sparkCount, life = age - .05f - u * .06f;
                float fade = life < 0 ? 0 : Mathf.Clamp01(1 - life / .23f);
                float angle = i * 2.399963f + _serial * .73f;
                float spread = .025f + (i * 7 % 5) * .023f + Mathf.Max(0,life) * (.35f + (i * 3 % 7) * .23f);
                var position = new Vector3(-flip * .55f + Mathf.Cos(angle) * spread,
                    -.12f + Mathf.Sin(angle) * spread * .65f - life * life * 2, 1.05f + (i * 3 % 7) * .025f + life * (1 + i % 3 * .5f));
                if (_bow) { position.x *= .3f; position.z += age * 7; }
                _sparks[i] = Matrix4x4.TRS(_origin + _rotation * position,
                    _rotation * Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg),
                    new Vector3(.025f + i % 3 * .012f, .025f, .012f) * fade);
            }
            Draw(_trail, count, _material);
            Draw(_sparks, sparkCount, _accentMaterial != null ? _accentMaterial : _material);
            _light.enabled = _local && LocalGameSettings.Current.quality > 0 && age < .13f;
            if (_light.enabled) { _light.transform.position = _origin + _rotation * Vector3.forward; _light.intensity = .65f * Mathf.Max(0, 1 - age / .13f); }
        }
        private void UpdateBladeTrail(float age)
        {
            float now = _start + age;
            if (_emitting)
            {
                bool sameAnimation = _animator == null ? age < _swingDuration :
                    _animator.GetCurrentAnimatorStateInfo(1).fullPathHash == _animationState &&
                    _animator.GetCurrentAnimatorStateInfo(1).normalizedTime < 1f;
                if (!sameAnimation || _blade == null || !_blade.gameObject.activeInHierarchy) StopMelee();
                else
                {
                    _bladeTrail.Sample(_blade.TransformPoint(_bladeBase), _blade.TransformPoint(_bladeTip), now);
                    if (_finishEmission) StopMelee();
                }
            }
            _bladeTrail.Build(now, _bladeVertices, _bladeColors, _bladeTriangles);
            _bladeProperties ??= new MaterialPropertyBlock();
            if (_bladeVertices.Count > 0 && _bladeMaterial != null)
            {
                if (_bladeMesh == null)
                {
                    _bladeMesh = new Mesh { name = "World Blade Sweep", hideFlags = HideFlags.DontSave };
                    _bladeMesh.MarkDynamic();
                }
                _bladeMesh.Clear(); _bladeMesh.SetVertices(_bladeVertices); _bladeMesh.SetColors(_bladeColors);
                _bladeMesh.SetTriangles(_bladeTriangles, 0, true);
                _bladeProperties.SetColor(BaseColor, _strokeColor);
                _bladeProperties.SetFloat(LocalNearFade, _local && InputModeRules.UsesFpsLook(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) ? 1f : 0f);
                var parameters = new RenderParams(_bladeMaterial)
                {
                    worldBounds = _bladeMesh.bounds,
                    matProps = _bladeProperties, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
                };
                Graphics.RenderMesh(parameters, _bladeMesh, 0, Matrix4x4.identity);
            }
            if (!_emitting && _bladeVertices.Count == 0) _start = -10;
            if (_light != null)
            {
                _light.enabled = _local && _emitting && LocalGameSettings.Current.quality > 0 && _bladeVertices.Count > 0;
                if (_light.enabled) { _light.color = _strokeColor; _light.transform.position = _blade.TransformPoint((_bladeBase + _bladeTip) * .5f); _light.intensity = .35f; }
            }
        }

        private void Draw(Matrix4x4[] matrices, int count, Material material)
        {
            var parameters = new RenderParams(material) { worldBounds = new Bounds(_origin + _rotation * Vector3.forward * 2, Vector3.one * 12), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            if (SystemInfo.supportsInstancing) Graphics.RenderMeshInstanced(parameters, _cube, 0, matrices, count);
            else for (int i = 0; i < count; i++) Graphics.RenderMesh(parameters, _cube, 0, matrices[i]);
        }
    }
}
