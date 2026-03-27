using System.Text;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Managers;

namespace BattlePvp.UI
{
    /// <summary>
    /// Canvas_Customizer의 "50pt 분배기" + "Identity 미리보기"를 이벤트 기반으로 구동합니다.
    /// - 슬라이더 변경 -> 가상 투자 스탯 갱신 -> IdentityCalculator로 미리보기 즉시 갱신
    /// - Apply 버튼 -> StatManager.ApplyInvestedOnly 호출 (아이템 보너스는 유지)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatCustomizerController : MonoBehaviour
    {
        private const int TotalInvestedBudget = 30;

        [Header("Target")]
        [SerializeField] private StatManager _statManager;

        [Header("Rows")]
        [SerializeField] private StatSlider _str;
        [SerializeField] private StatSlider _agi;
        [SerializeField] private StatSlider _con;
        [SerializeField] private StatSlider _def;

        [Header("Budget UI")]
        [SerializeField] private TMP_Text _pointsText;

        [Header("Identity Preview")]
        [SerializeField] private Image _identityIcon;
        [SerializeField] private TMP_Text _identityName;
        [SerializeField] private IdentitySpriteSet _spriteSet;

        [Header("Apply")]
        [SerializeField] private Button _applyButton;

        private IdentityCalculator _identityCalculator;
        private StatContainer _baseStats;     // item 포함된 원본 스탯(아이템 유지용)
        private StatContainer _virtualStats;  // 투자값만 실시간 변경되는 가상 스탯

        private readonly StringBuilder _sb = new StringBuilder(64);

        private void Awake()
        {
            _identityCalculator = new IdentityCalculator();

            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();
        }

        private void OnEnable()
        {
            LoadFromTarget();

            Hook(_str);
            Hook(_agi);
            Hook(_con);
            Hook(_def);

            if (_applyButton != null)
                _applyButton.onClick.AddListener(Apply);

            RebuildBudgetAndPreview();
        }

        private void OnDisable()
        {
            Unhook(_str);
            Unhook(_agi);
            Unhook(_con);
            Unhook(_def);

            if (_applyButton != null)
                _applyButton.onClick.RemoveListener(Apply);
        }

        private void LoadFromTarget()
        {
            if (_statManager == null)
                return;

            _baseStats = _statManager.GetStatsCopy();
            _virtualStats = _baseStats;

            // 아이템 Fill 세팅 + 투자값 초기화
            if (_str != null) { _str.SetItem(_baseStats.STR.Item); _str.SetInvestedWithoutNotify(_baseStats.STR.Invested); }
            if (_agi != null) { _agi.SetItem(_baseStats.AGI.Item); _agi.SetInvestedWithoutNotify(_baseStats.AGI.Invested); }
            if (_con != null) { _con.SetItem(_baseStats.CON.Item); _con.SetInvestedWithoutNotify(_baseStats.CON.Invested); }
            if (_def != null) { _def.SetItem(_baseStats.DEF.Item); _def.SetInvestedWithoutNotify(_baseStats.DEF.Invested); }
        }

        private void Hook(StatSlider s)
        {
            if (s == null) return;
            s.InvestedChanged += OnInvestedChanged;
        }

        private void Unhook(StatSlider s)
        {
            if (s == null) return;
            s.InvestedChanged -= OnInvestedChanged;
        }

        private void OnInvestedChanged(StatSlider changed, float _)
        {
            // 총합 30을 초과하면, 변경한 슬라이더에서 초과분을 즉시 깎는다(가장 단순하면서 결정적인 UX).
            int total = GetTotalInvested();
            if (total > TotalInvestedBudget && changed != null)
            {
                int overflow = total - TotalInvestedBudget;
                float next = Mathf.Max(0f, changed.Invested - overflow);
                changed.SetInvestedWithoutNotify(next);
            }

            SyncVirtualFromSliders();
            RebuildBudgetAndPreview();
        }

        private int GetTotalInvested()
        {
            int s = _str != null ? (int)_str.Invested : 0;
            int a = _agi != null ? (int)_agi.Invested : 0;
            int c = _con != null ? (int)_con.Invested : 0;
            int d = _def != null ? (int)_def.Invested : 0;
            return s + a + c + d;
        }

        private void SyncVirtualFromSliders()
        {
            _virtualStats = _baseStats;

            if (_str != null) _virtualStats.STR.Invested = _str.Invested;
            if (_agi != null) _virtualStats.AGI.Invested = _agi.Invested;
            if (_con != null) _virtualStats.CON.Invested = _con.Invested;
            if (_def != null) _virtualStats.DEF.Invested = _def.Invested;
        }

        private void RebuildBudgetAndPreview()
        {
            int used = GetTotalInvested();
            int remain = TotalInvestedBudget - used;
            if (_pointsText != null)
                _pointsText.text = $"{used} / {TotalInvestedBudget}";

            // Identity Preview
            Identity id = _identityCalculator.ResolveIdentity(_virtualStats, out _);

            if (_identityName != null)
            {
                _sb.Clear();
                _sb.Append(id.PrimaryStat);
                _sb.Append(' ');
                _sb.Append(id.Type.ToString().ToUpperInvariant());
                _identityName.text = _sb.ToString();
            }

            if (_identityIcon != null && _spriteSet != null)
                _identityIcon.sprite = _spriteSet.Resolve(id);

            // Apply 버튼 활성/비활성
            if (_applyButton != null)
                _applyButton.interactable = remain >= 0;
        }

        private void Apply()
        {
            if (_statManager == null)
                return;

            // 아이템은 유지하고 투자만 적용
            var investedOnly = default(StatContainer);
            investedOnly.STR.Invested = _virtualStats.STR.Invested;
            investedOnly.AGI.Invested = _virtualStats.AGI.Invested;
            investedOnly.CON.Invested = _virtualStats.CON.Invested;
            investedOnly.DEF.Invested = _virtualStats.DEF.Invested;

            _statManager.ApplyInvestedOnly(investedOnly, recalculateIdentity: true);

            // [추가] 글로벌 매니저가 있을 경우 영구 데이터에 병합 저장
            if (GlobalDataManager.Instance != null)
            {
                GlobalDataManager.Instance.SavedStats = _statManager.GetStatsCopy();
                Debug.Log("[StatCustomizer] Saved stats to GlobalDataManager.");
            }

            // 적용 후 베이스 스냅샷 갱신(아이템/투자 모두 포함된 최신 상태)
            LoadFromTarget();
            RebuildBudgetAndPreview();
        }
    }
}

