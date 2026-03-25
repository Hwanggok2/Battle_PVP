using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.VFX
{
    /// <summary>
    /// Stat/Identity 변경 이벤트를 받아, 인스턴스별 셰이더 파라미터를 즉시 갱신합니다.
    /// - GC 최소화: MaterialPropertyBlock 1개 재사용 + IdentityChanged 이벤트에만 반응
    /// - 적용 파라미터: _GlitchAmount, _StatColor
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VFXLinker : MonoBehaviour
    {
        [SerializeField] private StatManager _statManager;

        [Tooltip("PropertyBlock을 적용할 렌더러들. 비워두면 자식 렌더러를 자동 탐색합니다.")]
        [SerializeField] private Renderer[] _renderers;

        [Header("Glitch Amount")]
        [SerializeField] private float _glitchAmountDefault = 0.25f;
        [SerializeField] private float _glitchAmountMonostat = 1.0f;

        [Header("Stat Colors")]
        [SerializeField] private Color _colorStr = Color.red;
        [SerializeField] private Color _colorAgi = Color.green;
        [SerializeField] private Color _colorCon = Color.yellow;
        [SerializeField] private Color _colorDef = Color.blue;

        private MaterialPropertyBlock _block;

        private static readonly int GlitchAmountId = Shader.PropertyToID("_GlitchAmount");
        private static readonly int StatColorId = Shader.PropertyToID("_StatColor");

        private void Awake()
        {
            if (_statManager == null)
                _statManager = GetComponent<StatManager>();

            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);

            _block = new MaterialPropertyBlock();

            if (_statManager != null)
                Apply(_statManager.CurrentIdentity);
        }

        private void OnEnable()
        {
            if (_statManager == null)
                return;

            _statManager.IdentityChanged += OnIdentityChanged;
            Apply(_statManager.CurrentIdentity);
        }

        private void OnDisable()
        {
            if (_statManager == null)
                return;

            _statManager.IdentityChanged -= OnIdentityChanged;
        }

        private void OnIdentityChanged(Identity identity)
        {
            if (this == null) return;
            Apply(identity);
        }

        private void Apply(Identity identity)
        {
            float glitchAmount = identity.Type == IdentityType.Monostat
                ? _glitchAmountMonostat
                : _glitchAmountDefault;

            Color statColor = identity.PrimaryStat switch
            {
                StatKind.STR => _colorStr,
                StatKind.AGI => _colorAgi,
                StatKind.CON => _colorCon,
                _ => _colorDef, // DEF
            };

            _block.SetFloat(GlitchAmountId, glitchAmount);
            _block.SetColor(StatColorId, statColor);

            if (_renderers == null)
                return;

            // IdentityChanged는 드물게 발생하므로, 이 단계에서만 SetPropertyBlock을 수행합니다.
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null)
                    continue;

                r.SetPropertyBlock(_block);
            }
        }
    }
}

