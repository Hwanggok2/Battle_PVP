using BattlePvp.Combat;
using BattlePvp.Stats;
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

        [Header("Primary Stat Color")]
        [SerializeField] private Color _strColor = Color.red;
        [SerializeField] private Color _agiColor = Color.green;
        [SerializeField] private Color _conColor = Color.yellow;
        [SerializeField] private Color _defColor = Color.blue;

        private IIdentitySource _identitySource;
        private IPlayerStatusSource _statusSource;
        private IDamageReceiver _hpReader;
        private Material _runtimeMaterial;
        private Identity _currentIdentity;
        private float _overlapPercent;
        private float _reassembleProgress;

        private void Awake()
        {
            if (_targetGraphic == null)
                _targetGraphic = GetComponent<Graphic>();

            _identitySource = _identitySourceBehaviour as IIdentitySource;
            _statusSource = _statusSourceBehaviour as IPlayerStatusSource;
            _hpReader = _statusSourceBehaviour as IDamageReceiver;

            _reassembleProgress = Mathf.Clamp01(_defaultReassembleProgress);
        }

        private void OnEnable()
        {
            EnsureRuntimeMaterial();

            if (_identitySource != null)
            {
                _identitySource.IdentityChanged += OnIdentityChanged;
                _currentIdentity = _identitySource.CurrentIdentity;
            }

            if (_statusSource != null)
                _statusSource.OverflowChanged += OnOverflowChanged;

            // 시작 시점 즉시 반영(이벤트 대기 없이 초기 상태를 보장)
            PullInitialOverlapFromHpReader();
            ApplyAll();
        }

        private void OnDisable()
        {
            if (_identitySource != null)
                _identitySource.IdentityChanged -= OnIdentityChanged;

            if (_statusSource != null)
                _statusSource.OverflowChanged -= OnOverflowChanged;
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

        private void OnIdentityChanged(Identity identity)
        {
            _currentIdentity = identity;
            ApplyAll();
        }

        private void OnOverflowChanged(bool isOverflow, float overlapPercent)
        {
            _overlapPercent = isOverflow ? Mathf.Clamp01(overlapPercent) : 0f;
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

        private void PullInitialOverlapFromHpReader()
        {
            if (_hpReader == null || _hpReader.MaxHp <= 0f)
            {
                _overlapPercent = 0f;
                return;
            }

            float raw = (_hpReader.CurrentHp - _hpReader.MaxHp) / _hpReader.MaxHp;
            _overlapPercent = Mathf.Clamp01(raw);
        }

        private void ApplyAll()
        {
            if (_runtimeMaterial == null)
                return;

            _runtimeMaterial.SetFloat(GlitchAmountId, ResolveGlitchAmount(_currentIdentity.Type));
            _runtimeMaterial.SetColor(StatColorId, ResolveStatColor(_currentIdentity.PrimaryStat));
            _runtimeMaterial.SetFloat(EmissionPulseId, _emissionPulse);
            _runtimeMaterial.SetFloat(OverlapPercentId, _overlapPercent);
            _runtimeMaterial.SetFloat(ReassembleProgressId, _reassembleProgress);
            _runtimeMaterial.SetFloat(MirrorActiveId, ResolveMirrorActive(_currentIdentity.Type));
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

        private float ResolveMirrorActive(IdentityType type)
        {
            return _mirrorWhenPolymath && type == IdentityType.Polymath ? 1f : 0f;
        }
    }
}

