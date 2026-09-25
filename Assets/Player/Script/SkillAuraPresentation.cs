using System;
using BattlePvp.Stats;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.Combat
{
    public enum SkillAuraShape { Sphere, Capsule, Cube }

    public readonly struct SkillAuraSettings
    {
        public readonly Material StrMaterial, AgiMaterial, ConMaterial, DefMaterial;
        public readonly SkillAuraShape Shape;
        public readonly bool FitToPlayer, PreferControllerBounds;
        public readonly Vector3 Scale, Offset;

        public SkillAuraSettings(Material str, Material agi, Material con, Material def,
            SkillAuraShape shape, bool fitToPlayer, bool preferControllerBounds, Vector3 scale, Vector3 offset)
        {
            StrMaterial = str;
            AgiMaterial = agi;
            ConMaterial = con;
            DefMaterial = def;
            Shape = shape;
            FitToPlayer = fitToPlayer;
            PreferControllerBounds = preferControllerBounds;
            Scale = scale;
            Offset = offset;
        }
    }

    /// <summary>Owns only the aura's visual objects and timed display. Combat supplies time and shield state.</summary>
    public sealed class SkillAuraPresentation : IDisposable
    {
        private readonly Transform _owner;
        private readonly CharacterController _characterController;
        private readonly SkillAuraSettings _settings;
        private GameObject _auraObject;
        private Transform _auraTransform;
        private Renderer _auraRenderer;
        private Material _runtimeMaterial;
        private Material _activeMaterialSource;
        private StatKind _activeStat = StatKind.STR;
        private bool _visible;
        private StatKind _timedStat = StatKind.STR;
        private double _timedUntil;
        private Bounds _cachedBounds;
        private bool _hasCachedBounds;

        public SkillAuraPresentation(Transform owner, CharacterController controller, SkillAuraSettings settings)
        {
            _owner = owner;
            _characterController = controller;
            _settings = settings;
        }

        public void Cancel()
        {
            _timedUntil = 0d;
            SetVisible(false);
        }

        public void Dispose()
        {
            Cancel();
            DestroyOwned(_auraObject);
            DestroyOwned(_runtimeMaterial);
            _auraObject = null;
            _auraTransform = null;
            _auraRenderer = null;
            _runtimeMaterial = null;
        }

        private static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Object.Destroy(owned);
            else Object.DestroyImmediate(owned);
        }

        public void ShowTimed(StatKind statKind, float durationSeconds, double now, float currentShield)
        {
            float clampedDuration = Mathf.Max(0f, durationSeconds);
            if (clampedDuration <= 0f)
                return;

            _timedStat = statKind;
            _timedUntil = now + clampedDuration;
            Update(now, currentShield);
        }

        public void Update(double now, float currentShield)
        {
            bool active = TryResolveAuraStat(now, currentShield, out StatKind auraStat);
            if (!active)
            {
                SetVisible(false);
                return;
            }

            bool wasVisible = _visible;
            SetVisible(true);
            if (_auraObject == null)
                return;

            if (wasVisible && _activeStat == auraStat)
                return;

            _activeStat = auraStat;
            ApplyMaterial();
            if (_runtimeMaterial != null)
            {
                if (_runtimeMaterial.HasProperty("_Pulse"))
                    _runtimeMaterial.SetFloat("_Pulse", 1f);
            }
        }

        private bool TryResolveAuraStat(double now, float currentShield, out StatKind auraStat)
        {
            if (now < _timedUntil)
            {
                auraStat = _timedStat;
                return true;
            }

            if (currentShield >= 1f)
            {
                auraStat = StatKind.CON;
                return true;
            }

            auraStat = StatKind.STR;
            return false;
        }

        private void SetVisible(bool visible)
        {
            if (visible)
                EnsureAura();

            if (_auraObject != null && _auraObject.activeSelf != visible)
                _auraObject.SetActive(visible);

            _visible = visible && _auraObject != null;
        }

        private void EnsureAura()
        {
            if (_auraObject != null)
                return;

            GameObject aura = GameObject.CreatePrimitive(ToPrimitiveType(_settings.Shape));
            aura.name = "Strategist_STR_Attack_Aura";
            _auraObject = aura;
            _auraTransform = aura.transform;
            _auraTransform.SetParent(_owner, false);
            RefreshTransform();

            Collider auraCollider = aura.GetComponent<Collider>();
            if (auraCollider != null)
                DestroyOwned(auraCollider);

            Renderer auraRenderer = aura.GetComponent<Renderer>();
            if (auraRenderer != null)
            {
                _auraRenderer = auraRenderer;
                auraRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                auraRenderer.receiveShadows = false;
                ApplyMaterial();
            }

            _auraObject.SetActive(false);
        }

        private void RefreshTransform()
        {
            if (_auraTransform == null)
                return;

            _auraTransform.localPosition = ResolveLocalPosition();
            _auraTransform.localRotation = Quaternion.identity;
            _auraTransform.localScale = ResolveLocalScale();
        }

        private void ApplyMaterial()
        {
            if (_auraRenderer == null)
                return;

            Material source = ResolveMaterial(_activeStat);
            if (_activeMaterialSource == source && _auraRenderer.sharedMaterial != null)
                return;

            _activeMaterialSource = source;
            if (source != null)
            {
                _auraRenderer.sharedMaterial = source;
                return;
            }

            if (_runtimeMaterial == null)
            {
                Shader shader = Shader.Find("BattlePVP/FresnelAura");
                if (shader != null)
                    _runtimeMaterial = new Material(shader);
            }

            if (_runtimeMaterial != null)
                _auraRenderer.sharedMaterial = _runtimeMaterial;
        }

        private Material ResolveMaterial(StatKind statKind)
        {
            return statKind switch
            {
                StatKind.AGI => _settings.AgiMaterial != null ? _settings.AgiMaterial : _settings.StrMaterial,
                StatKind.CON => _settings.ConMaterial != null ? _settings.ConMaterial : _settings.StrMaterial,
                StatKind.DEF => _settings.DefMaterial != null ? _settings.DefMaterial : _settings.StrMaterial,
                _ => _settings.StrMaterial
            };
        }

        private Vector3 ResolveLocalPosition()
        {
            if (_settings.FitToPlayer && _settings.PreferControllerBounds && _characterController != null)
                return _characterController.center + _settings.Offset;

            if (!_settings.FitToPlayer || !TryGetPlayerRenderBounds(out Bounds bounds))
                return Vector3.up + _settings.Offset;

            Vector3 worldCenter = bounds.center + (_owner != null ? _owner.TransformVector(_settings.Offset) : _settings.Offset);
            return _owner != null ? _owner.InverseTransformPoint(worldCenter) : worldCenter;
        }

        private Vector3 ResolveLocalScale()
        {
            if (_settings.FitToPlayer && _settings.PreferControllerBounds && _characterController != null)
            {
                float radius = Mathf.Max(0.01f, _characterController.radius);
                float height = Mathf.Max(radius * 2f, _characterController.height);
                Vector3 controllerSize = new Vector3(radius * 2f, height, radius * 2f);
                Vector3 controllerPrimitiveSize = GetPrimitiveLocalSize(_settings.Shape);
                return new Vector3(
                    controllerSize.x * Mathf.Max(0.01f, _settings.Scale.x) / controllerPrimitiveSize.x,
                    controllerSize.y * Mathf.Max(0.01f, _settings.Scale.y) / controllerPrimitiveSize.y,
                    controllerSize.z * Mathf.Max(0.01f, _settings.Scale.z) / controllerPrimitiveSize.z);
            }

            if (!_settings.FitToPlayer || !TryGetPlayerRenderBounds(out Bounds bounds))
                return _settings.Scale;

            Vector3 size = bounds.size;
            Vector3 scaledSize = new Vector3(
                Mathf.Max(0.01f, size.x * Mathf.Max(0.01f, _settings.Scale.x)),
                Mathf.Max(0.01f, size.y * Mathf.Max(0.01f, _settings.Scale.y)),
                Mathf.Max(0.01f, size.z * Mathf.Max(0.01f, _settings.Scale.z)));

            Vector3 primitiveSize = GetPrimitiveLocalSize(_settings.Shape);
            return new Vector3(
                scaledSize.x / primitiveSize.x,
                scaledSize.y / primitiveSize.y,
                scaledSize.z / primitiveSize.z);
        }

        private bool TryGetPlayerRenderBounds(out Bounds bounds)
        {
            if (_settings.PreferControllerBounds && TryGetCharacterControllerAuraBounds(out bounds))
                return true;

            if (_hasCachedBounds)
            {
                bounds = _cachedBounds;
                return true;
            }

            SkinnedMeshRenderer[] renderers = _owner.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool found = false;
            bounds = default;

            foreach (Renderer rendererComponent in renderers)
            {
                if (rendererComponent == null || rendererComponent.gameObject == _auraObject)
                    continue;

                if (rendererComponent.GetComponentInParent<Canvas>() != null)
                    continue;

                if (!found)
                {
                    bounds = rendererComponent.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(rendererComponent.bounds);
                }
            }

            _cachedBounds = bounds;
            _hasCachedBounds = found;
            return found;
        }

        private bool TryGetCharacterControllerAuraBounds(out Bounds bounds)
        {
            if (_characterController == null)
            {
                bounds = default;
                return false;
            }

            Transform ownerTransform = _owner;
            Vector3 worldCenter = ownerTransform.TransformPoint(_characterController.center);
            Vector3 scale = ownerTransform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float radius = Mathf.Max(0.01f, _characterController.radius * radiusScale);
            float height = Mathf.Max(radius * 2f, _characterController.height * Mathf.Abs(scale.y));
            bounds = new Bounds(worldCenter, new Vector3(radius * 2f, height, radius * 2f));
            return true;
        }

        private static PrimitiveType ToPrimitiveType(SkillAuraShape shape)
        {
            return shape switch
            {
                SkillAuraShape.Capsule => PrimitiveType.Capsule,
                SkillAuraShape.Cube => PrimitiveType.Cube,
                _ => PrimitiveType.Sphere
            };
        }

        private static Vector3 GetPrimitiveLocalSize(SkillAuraShape shape)
        {
            return shape switch
            {
                SkillAuraShape.Capsule => new Vector3(1f, 2f, 1f),
                _ => Vector3.one
            };
        }

    }
}
