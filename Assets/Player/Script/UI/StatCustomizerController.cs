using System.Text;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Managers;
using BattlePvp.Combat;
using System.Collections;

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

        [Header("Derived Stats Preview")]
        [SerializeField] private TMP_Text _previewAtkText;
        [SerializeField] private TMP_Text _previewDefText;
        [SerializeField] private TMP_Text _previewMaxHpText;
        [SerializeField] private TMP_Text _previewPeneText;
        [SerializeField] private TMP_Text _previewRegenText;
        [SerializeField] private TMP_Text _previewMoveSpdText;
        [SerializeField] private TMP_Text _previewAtkSpdText;

        [Header("Apply & Restrictions")]
        [SerializeField] private Button _applyButton;
        [SerializeField] private CanvasGroup _floatingMessageCanvasGroup;
        [SerializeField] private TMP_Text _floatingMessageText;
        
        [Header("Death Dimmed Overlay")]
        [SerializeField] private Image _dimOverlay;

        private HealthSystem _playerHealth;

        private IdentityCalculator _identityCalculator;
        private StatContainer _baseStats;     // item 포함된 원본 스탯(아이템 유지용)
        private StatContainer _virtualStats;  // 투자값만 실시간 변경되는 가상 스탯

        private readonly StringBuilder _sb = new StringBuilder(64);

        private void Awake()
        {
            _identityCalculator = new IdentityCalculator();

            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();
                
            if (_playerHealth == null)
                _playerHealth = GetComponentInParent<HealthSystem>();
                
            if (_floatingMessageCanvasGroup != null)
                _floatingMessageCanvasGroup.alpha = 0f;
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
            
            // 딤 오버레이 체크
            if (_dimOverlay != null && _playerHealth != null)
            {
                _dimOverlay.gameObject.SetActive(_playerHealth.IsDead);
            }
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

        public int GetRemainPoints()
        {
            return TotalInvestedBudget - GetTotalInvested();
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

            // Derived Stats (ATK, DEF, MaxHP, Pene, Regen, MoveSpeed, AttackSpeed) 실시간 계산
            if (_statManager != null)
            {
                _statManager.CalculatePreviewStats(_virtualStats, out float atk, out float def, out float maxHp, out float pene, out float regen, out float moveSpd, out float atkSpd);
                
                // Juice Effect 적용해 텍스트 업데이트
                UpdatePreviewText(_previewAtkText, $"{atk:F0}");
                UpdatePreviewText(_previewDefText, $"{def:F1}%");
                UpdatePreviewText(_previewMaxHpText, $"{maxHp:F0}");
                UpdatePreviewText(_previewPeneText, $"{pene:F1}%");
                UpdatePreviewText(_previewRegenText, $"{regen:F1}/s");
                UpdatePreviewText(_previewMoveSpdText, $"{moveSpd:F2}");
                UpdatePreviewText(_previewAtkSpdText, $"{atkSpd:F2}");
            }

            // Apply 버튼 활성/비활성
            if (_applyButton != null)
                _applyButton.interactable = remain >= 0;

            // 로비 UI 방 진입 버튼 처리 (옵저버 대용: 글로벌 인스턴스 접근)
            if (LobbyUIManager.Instance != null)
            {
                LobbyUIManager.Instance.UpdateRoomButtonsInteractable(remain == 0);
            }
        }

        private void UpdatePreviewText(TMP_Text textRef, string newValue)
        {
            if (textRef == null) return;
            if (textRef.text != newValue)
            {
                textRef.text = newValue;
                // 간단한 Juice 효과
                StartCoroutine(JuiceTextEffect(textRef.transform));
            }
        }

        private IEnumerator JuiceTextEffect(Transform t)
        {
            Vector3 originalScale = Vector3.one;
            t.localScale = originalScale * 1.2f;
            
            float elapsed = 0f;
            float duration = 0.2f;
            while(elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                t.localScale = Vector3.Lerp(originalScale * 1.2f, originalScale, elapsed / duration);
                yield return null;
            }
            t.localScale = originalScale;
        }

        private bool IsMonostat(StatContainer sc)
        {
            return (sc.STR.Invested >= 30f || sc.CON.Invested >= 30f || sc.AGI.Invested >= 30f || sc.DEF.Invested >= 30f);
        }

        private Coroutine _floatingMessageRoutine;
        private void ShowFloatingMessage(string msg)
        {
            if (_floatingMessageCanvasGroup == null || _floatingMessageText == null) return;
            _floatingMessageText.text = msg;
            
            if (_floatingMessageRoutine != null) StopCoroutine(_floatingMessageRoutine);
            _floatingMessageRoutine = StartCoroutine(CoShowFloatingMessage());
        }

        private IEnumerator CoShowFloatingMessage()
        {
            _floatingMessageCanvasGroup.alpha = 1f;
            yield return new WaitForSeconds(1f);
            float elapsed = 0f;
            while(elapsed < 1f)
            {
                elapsed += Time.deltaTime;
                _floatingMessageCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed);
                yield return null;
            }
        }

        private void Apply()
        {
            if (_statManager == null)
                return;

            // 순수 몰빵형 사망 중 검증
            if (IsMonostat(_baseStats) && _playerHealth != null && _playerHealth.IsDead)
            {
                ShowFloatingMessage("몰빵형 캐릭터는 스탯 변경이 불가능합니다");
                // 원상복구
                LoadFromTarget();
                RebuildBudgetAndPreview();
                return;
            }

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

