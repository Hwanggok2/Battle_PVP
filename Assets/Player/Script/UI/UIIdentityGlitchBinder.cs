using BattlePvp.Combat;
using BattlePvp.Stats;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>
    /// Identity/HP Overflow 이벤트를 UI 셰이더 파라미터에 바인딩합니다.
    /// reference-vfx-params.md 규격의 프로퍼티명을 그대로 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class UIIdentityGlitchBinder : MonoBehaviour
    {
        private static readonly int GlitchAmountId = Shader.PropertyToID("_GlitchAmount");
        private static readonly int StatColorId = Shader.PropertyToID("_StatColor");
        private static readonly int EmissionPulseId = Shader.PropertyToID("_EmissionPulse");
        private static readonly int OverlapPercentId = Shader.PropertyToID("_OverlapPercent");
        private static readonly int ReassembleProgressId = Shader.PropertyToID("_ReassembleProgress");
        private static readonly int MirrorActiveId = Shader.PropertyToID("_MirrorActive");
        private static readonly int VignetteRadiusId = Shader.PropertyToID("_VignetteRadius");
        private static readonly int VignetteSoftnessId = Shader.PropertyToID("_VignetteSoftness");
        private static readonly int HealthAlphaBoostId = Shader.PropertyToID("_HealthAlphaBoost");
        private const float BaseAlpha = 0.25f;
        private const float LowHealthAlpha = 0.5f;

        [Header("Target")]
        [SerializeField] private Graphic _targetGraphic;

        [Header("Sources")]
        [Tooltip("IIdentitySource를 구현한 컴포넌트(예: StatManager)")]
        [SerializeField] private MonoBehaviour _identitySourceBehaviour;
        [Tooltip("IPlayerStatusSource를 구현한 컴포넌트(예: HealthSystem)")]
        [SerializeField] private MonoBehaviour _statusSourceBehaviour;

        [Header("Base VFX Tuning")]
        [SerializeField] [Range(0f, 1f)] private float _glitchMonostat = 1f;
        [SerializeField] [Range(0f, 1f)] private float _glitchPolymath = 0.45f;
        [SerializeField] [Range(0f, 1f)] private float _glitchStrategist = 0.65f;
        [SerializeField] private float _emissionPulse = 4f;
        [SerializeField] [Range(0f, 1f)] private float _defaultReassembleProgress = 1f;
        [SerializeField] private bool _mirrorWhenPolymath = true;

        [Header("Vignette (Noise Masking)")]
        [SerializeField] [Range(0f, 1f)] private float _vignetteMainRadius = 1.0f;
        [SerializeField] [Range(0f, 1f)] private float _vignetteMainSoftness = 1.0f;

        [Header("Primary Stat Color")]
        [SerializeField] private Color _strColor = Color.red;
        [SerializeField] private Color _agiColor = Color.green;
        [SerializeField] private Color _conColor = Color.yellow;
        [SerializeField] private Color _defColor = Color.blue;

        private IIdentitySource _identitySource;
        private IPlayerStatusSource _statusSource;
        private IDamageReceiver _hpReader;
        private StatManager _statManagerSource;
        private Material _runtimeMaterial;
        private Identity _currentIdentity;
        private StatContainer _currentStats;
        private bool _hasCurrentStats;
        private float _overlapPercent;
        private float _reassembleProgress;
        private float _hpPercent = 1f;
        private bool _hasAppliedMaterialValues;
        private float _lastGlitchAmount;
        private Color _lastStatColor;
        private float _lastEmissionPulse;
        private float _lastOverlapPercent;
        private float _lastReassembleProgress;
        private float _lastMirrorActive;
        private float _lastVignetteRadius;
        private float _lastVignetteSoftness;
        private float _lastHealthAlphaBoost;
        private Coroutine _resolveSourcesRoutine;
        private bool _isSubscribed;

        private void Awake()
        {
            if (_targetGraphic == null)
                _targetGraphic = GetComponent<Graphic>();

            RefreshSourceInterfaces();

            _reassembleProgress = Mathf.Clamp01(_defaultReassembleProgress);
        }

        private void OnEnable()
        {
            LocalGameSettings.Changed += ApplyVfxVisibility;
            ApplyVfxVisibility();
            StatManager.LocalChanged += OnLocalPlayerChanged;
            if (_targetGraphic != null)
            {
                Color color = _targetGraphic.color;
                color.a = BaseAlpha;
                _targetGraphic.color = color;
            }
            EnsureRuntimeMaterial();

            TryAutoResolveSources();
            RefreshSourceInterfaces();
            SubscribeSources();

            // 시작 시점 즉시 반영(이벤트 대기 없이 초기 상태를 보장)
            PullCurrentHpFromReader();

            if (_identitySource == null || _statusSource == null)
                _resolveSourcesRoutine = StartCoroutine(CoResolveSourcesWhenReady());
        }

        private void OnDisable()
        {
            LocalGameSettings.Changed -= ApplyVfxVisibility;
            StatManager.LocalChanged -= OnLocalPlayerChanged;
            if (_resolveSourcesRoutine != null)
            {
                StopCoroutine(_resolveSourcesRoutine);
                _resolveSourcesRoutine = null;
            }

            UnsubscribeSources();
        }

        private void ApplyVfxVisibility()
        {
            if (_targetGraphic != null) _targetGraphic.enabled = !LocalGameSettings.Current.hideVfx;
        }

        private void OnLocalPlayerChanged(StatManager local)
        {
            if (!IsSceneGlobalUi()) return;
            UnsubscribeSources();
            _identitySourceBehaviour = local;
            _statusSourceBehaviour = local != null ? local.GetComponent<HealthSystem>() : null;
            _hasCurrentStats = false;
            RefreshSourceInterfaces();
            SubscribeSources();
            PullCurrentHpFromReader();
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        /// <summary>
        /// 외부 타임라인/애니메이션에서 재조립 진행도(0..1)를 주입할 때 사용합니다.
        /// </summary>
        public void SetReassembleProgress(float progress)
        {
            _reassembleProgress = Mathf.Clamp01(progress);
            ApplyAll();
        }

        public void SetIdentity(Identity identity)
        {
            _currentIdentity = identity;
            EnsureRuntimeMaterial();
            ApplyAll();
        }

        public void SetIdentity(Identity identity, StatContainer stats)
        {
            _currentIdentity = identity;
            _currentStats = stats;
            _hasCurrentStats = true;
            EnsureRuntimeMaterial();
            ApplyAll();
        }

        private void OnIdentityChanged(Identity identity)
        {
            if (this == null) return;
            _currentIdentity = identity;
            PullCurrentStatsFromStatManager();
            ApplyAll();
        }

        private IEnumerator CoResolveSourcesWhenReady()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (isActiveAndEnabled && (_identitySource == null || _statusSource == null))
            {
                if (TryAutoResolveSources())
                {
                    UnsubscribeSources();
                    RefreshSourceInterfaces();
                    SubscribeSources();
                    PullCurrentHpFromReader();
                }

                yield return wait;
            }

            _resolveSourcesRoutine = null;
        }

        private void RefreshSourceInterfaces()
        {
            _identitySource = _identitySourceBehaviour != null ? _identitySourceBehaviour as IIdentitySource : null;
            _statusSource = _statusSourceBehaviour != null ? _statusSourceBehaviour as IPlayerStatusSource : null;
            _hpReader = _statusSourceBehaviour != null ? _statusSourceBehaviour as IDamageReceiver : null;
            _statManagerSource = _identitySourceBehaviour as StatManager;
            PullCurrentStatsFromStatManager();
        }

        private bool TryAutoResolveSources()
        {
            bool changed = false;
            StatManager parentStats = GetComponentInParent<StatManager>(true);

            if (parentStats != null && _identitySourceBehaviour != parentStats)
            {
                _identitySourceBehaviour = parentStats;
                changed = true;
            }

            if (_identitySourceBehaviour == null || _identitySourceBehaviour is not IIdentitySource)
            {
                StatManager localStats = ResolveScopedStatManager();

                if (localStats != null)
                {
                    _identitySourceBehaviour = localStats;
                    changed = true;
                }
            }

            HealthSystem parentHealth = parentStats != null ? parentStats.GetComponent<HealthSystem>() : null;
            if (parentHealth != null && _statusSourceBehaviour != parentHealth)
            {
                _statusSourceBehaviour = parentHealth;
                changed = true;
            }

            if (_statusSourceBehaviour == null ||
                _statusSourceBehaviour is not IPlayerStatusSource ||
                _statusSourceBehaviour is not IDamageReceiver)
            {
                HealthSystem localHealth = null;
                if (_identitySourceBehaviour != null)
                    localHealth = _identitySourceBehaviour.GetComponent<HealthSystem>();
                if (localHealth == null)
                    localHealth = ResolveScopedHealthSystem();

                if (localHealth != null)
                {
                    _statusSourceBehaviour = localHealth;
                    changed = true;
                }
            }

            return changed;
        }

        private StatManager ResolveScopedStatManager()
        {
            StatManager parentStats = GetComponentInParent<StatManager>(true);
            if (parentStats != null)
                return parentStats;

            if (!IsSceneGlobalUi())
                return null;

            return StatManager.Local;
        }

        private HealthSystem ResolveScopedHealthSystem()
        {
            HealthSystem parentHealth = GetComponentInParent<HealthSystem>(true);
            if (parentHealth != null)
                return parentHealth;

            if (!IsSceneGlobalUi())
                return null;

            if (StatManager.Local != null)
            {
                HealthSystem localHealth = StatManager.Local.GetComponent<HealthSystem>();
                if (localHealth != null)
                    return localHealth;
            }

            return null;
        }

        private bool IsSceneGlobalUi()
        {
            return GetComponentInParent<StatManager>(true) == null;
        }

        private void SubscribeSources()
        {
            if (_isSubscribed)
                return;

            if (_identitySource != null)
            {
                _identitySource.IdentityChanged += OnIdentityChanged;
                _currentIdentity = _identitySource.CurrentIdentity;
            }

            if (_statManagerSource != null)
                _statManagerSource.StatsChanged += OnStatsChanged;

            if (_statusSource != null)
            {
                _statusSource.OverflowChanged += OnOverflowChanged;
                _statusSource.HpChanged += OnHpChanged;
            }

            _isSubscribed = true;
        }

        private void UnsubscribeSources()
        {
            if (!_isSubscribed)
                return;

            if (_identitySource != null)
                _identitySource.IdentityChanged -= OnIdentityChanged;

            if (_statManagerSource != null)
                _statManagerSource.StatsChanged -= OnStatsChanged;

            if (_statusSource != null)
            {
                _statusSource.OverflowChanged -= OnOverflowChanged;
                _statusSource.HpChanged -= OnHpChanged;
            }

            _isSubscribed = false;
        }

        private void OnStatsChanged(StatContainer stats)
        {
            if (this == null) return;
            _currentStats = stats;
            _hasCurrentStats = true;
            if (_statManagerSource != null)
                _currentIdentity = _statManagerSource.CurrentIdentity;
            ApplyAll();
        }

        private void PullCurrentStatsFromStatManager()
        {
            if (_statManagerSource == null)
                return;

            _currentStats = _statManagerSource.GetStatsCopy();
            _hasCurrentStats = true;
        }

        private void OnOverflowChanged(bool isOverflow, float overlapPercent)
        {
            if (this == null) return;
            _overlapPercent = isOverflow ? Mathf.Clamp01(overlapPercent) : 0f;
            ApplyAll();
        }

        private void OnHpChanged(float current, float max)
        {
            if (this == null) return;
            _hpPercent = max > 0f ? Mathf.Clamp01(current / max) : 1f;
            ApplyAll();
        }

        private void EnsureRuntimeMaterial()
        {
            if (_targetGraphic == null || _runtimeMaterial != null)
                return;

            Material baseMat = _targetGraphic.material;
            if (baseMat == null)
                return;

            _runtimeMaterial = new Material(baseMat)
            {
                name = baseMat.name + " (UIIdentityGlitchBinder)"
            };
            _targetGraphic.material = _runtimeMaterial;
        }

        private void PullCurrentHpFromReader()
        {
            float current = _hpReader != null ? _hpReader.CurrentHp : 0f;
            float max = _hpReader != null ? _hpReader.MaxHp : 0f;
            _overlapPercent = max > 0f ? Mathf.Clamp01((current - max) / max) : 0f;
            OnHpChanged(current, max);
        }

        private void ApplyAll()
        {
            if (_runtimeMaterial == null)
                return;

            float dynamicPulse = Mathf.Lerp(10f, _emissionPulse, _hpPercent); // HP 낮을수록 10에 가까워짐
            // Shader-side pulsing avoids rebuilding the UI mesh every frame and preserves CanvasGroup fades.
            float healthAlphaBoost = (LowHealthAlpha / BaseAlpha - 1f) * (1f - _hpPercent);

            float glitchAmount = ResolveGlitchAmount(_currentIdentity.Type);
            Color statColor = ResolveIdentityColor(_currentIdentity);
            float mirrorActive = ResolveMirrorActive(_currentIdentity.Type);
            bool changed = !_hasAppliedMaterialValues
                           || !Mathf.Approximately(_lastGlitchAmount, glitchAmount)
                           || _lastStatColor != statColor
                           || !Mathf.Approximately(_lastEmissionPulse, dynamicPulse)
                           || !Mathf.Approximately(_lastOverlapPercent, _overlapPercent)
                           || !Mathf.Approximately(_lastReassembleProgress, _reassembleProgress)
                           || !Mathf.Approximately(_lastMirrorActive, mirrorActive)
                           || !Mathf.Approximately(_lastVignetteRadius, _vignetteMainRadius)
                           || !Mathf.Approximately(_lastVignetteSoftness, _vignetteMainSoftness)
                           || !Mathf.Approximately(_lastHealthAlphaBoost, healthAlphaBoost);

            if (!changed)
                return;

            _lastGlitchAmount = glitchAmount;
            _lastStatColor = statColor;
            _lastEmissionPulse = dynamicPulse;
            _lastOverlapPercent = _overlapPercent;
            _lastReassembleProgress = _reassembleProgress;
            _lastMirrorActive = mirrorActive;
            _lastVignetteRadius = _vignetteMainRadius;
            _lastVignetteSoftness = _vignetteMainSoftness;
            _lastHealthAlphaBoost = healthAlphaBoost;
            _hasAppliedMaterialValues = true;

            _runtimeMaterial.SetFloat(GlitchAmountId, glitchAmount);
            _runtimeMaterial.SetVector(StatColorId, (Vector4)statColor);
            _runtimeMaterial.SetFloat(EmissionPulseId, dynamicPulse);
            _runtimeMaterial.SetFloat(OverlapPercentId, _overlapPercent);
            _runtimeMaterial.SetFloat(ReassembleProgressId, _reassembleProgress);
            _runtimeMaterial.SetFloat(MirrorActiveId, mirrorActive);
            _runtimeMaterial.SetFloat(VignetteRadiusId, _vignetteMainRadius);
            _runtimeMaterial.SetFloat(VignetteSoftnessId, _vignetteMainSoftness);
            _runtimeMaterial.SetFloat(HealthAlphaBoostId, healthAlphaBoost);
            if (_targetGraphic != null)
                _targetGraphic.SetMaterialDirty();
        }

        private float ResolveGlitchAmount(IdentityType type)
        {
            return type switch
            {
                IdentityType.Monostat => _glitchMonostat,
                IdentityType.Polymath => _glitchPolymath,
                _ => _glitchStrategist,
            };
        }

        private Color ResolveStatColor(StatKind stat)
        {
            return stat switch
            {
                StatKind.STR => _strColor,
                StatKind.AGI => _agiColor,
                StatKind.CON => _conColor,
                _ => _defColor,
            };
        }

        private Color ResolveIdentityColor(Identity identity)
        {
            return _hasCurrentStats
                ? StatVfxColor.Resolve(identity, _currentStats, _strColor, _agiColor, _conColor, _defColor)
                : ResolveStatColor(identity.PrimaryStat);
        }

        private float ResolveMirrorActive(IdentityType type)
        {
            return _mirrorWhenPolymath && type == IdentityType.Polymath ? 1f : 0f;
        }
    }
}

