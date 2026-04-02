using System.Text;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Managers;
using BattlePvp.Combat;
using BattlePvp.Networking;
using System.Collections;

namespace BattlePvp.UI
{
    /// <summary>
    /// Canvas_Customizer의 전반적인 관리자. 
    /// "DB 수치와 UI 수치가 100% 일치할 때까지 끈질기게 업데이트 루프를 돌리는 추격(Catch-up) 시스템"이 핵심입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatCustomizerController : MonoBehaviour
    {
        public static StatCustomizerController Instance { get; private set; }

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
        [SerializeField] private TMP_Text _remainPointsText;

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
        private StatContainer _baseStats;     
        private StatContainer _virtualStats;  

        private readonly StringBuilder _sb = new StringBuilder(64);

        private bool _isInitializedFromGlobal = false;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }
            
            _identityCalculator = new IdentityCalculator();
            if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
            if (_playerHealth == null) _playerHealth = GetComponentInParent<HealthSystem>();
            if (_floatingMessageCanvasGroup != null) _floatingMessageCanvasGroup.alpha = 0f;
        }

        private void Update()
        {
            // [추격 루프] 초기화가 완료되지 않았다면 지속적으로 DB 수치에 UI를 맞춥니다.
            if (!_isInitializedFromGlobal)
            {
                if (_statManager == null) TryFindTarget();

                if (GlobalDataManager.Instance != null)
                {
                    var saved = GlobalDataManager.Instance.SavedStats;
                    float totalDB = saved.STR.Invested + saved.AGI.Invested + saved.CON.Invested + saved.DEF.Invested;

                    // DB 데이터가 아직 채워지지 않았다면(0인 상태) 대기합니다.
                    if (totalDB < 0.1f) return;

                    // UI가 DB 값과 일치하는지 검사합니다.
                    if (!IsUISyncedWithSavedData(saved))
                    {
                        // 일치하지 않는다면 매 프레임 UI를 강제 주입합니다! (사용자 제안 추격 루프)
                        _baseStats = saved;
                        _virtualStats = _baseStats;
                        RefreshSliderVisuals();
                        RebuildBudgetAndPreview();
                        
                        // 타겟 플레이어에게도 일단 1회 주입
                        if (_statManager != null)
                        {
                            _statManager.ApplyStats(_baseStats, recalculateIdentity: true);
                        }
                    }
                    else
                    {
                        // 모든 수치가 완벽히 일치하면 루프를 종료합니다.
                        _isInitializedFromGlobal = true;
                        Debug.Log($"[StatCustomizer] SYNC SUCCESS! All sliders matches DB. (STR:{(int)saved.STR.Invested}, AGI:{(int)saved.AGI.Invested}, CON:{(int)saved.CON.Invested})");
                    }
                }
            }
        }

        private bool IsUISyncedWithSavedData(StatContainer saved)
        {
            // 슬라이더의 현재 값이 DB의 투자값과 정수 단위로 일치하는지 확인합니다.
            bool s = _str != null && Mathf.RoundToInt(_str.Invested) == Mathf.RoundToInt(saved.STR.Invested);
            bool a = _agi != null && Mathf.RoundToInt(_agi.Invested) == Mathf.RoundToInt(saved.AGI.Invested);
            bool c = _con != null && Mathf.RoundToInt(_con.Invested) == Mathf.RoundToInt(saved.CON.Invested);
            bool d = _def != null && Mathf.RoundToInt(_def.Invested) == Mathf.RoundToInt(saved.DEF.Invested);
            return s && a && c && d;
        }

        private void OnEnable()
        {
            if (GlobalDataManager.Instance != null)
                GlobalDataManager.Instance.OnSavedStatsUpdated += OnGlobalStatsUpdated;

            TryFindTarget();
            _isInitializedFromGlobal = false; // 창을 열 때마다 일치 여부를 다시 확인합니다.

            Hook(_str); Hook(_agi); Hook(_con); Hook(_def);

            if (_applyButton != null)
                _applyButton.onClick.AddListener(Apply);

            RefreshSliderVisuals();
            RebuildBudgetAndPreview();
        }

        private void OnDisable()
        {
            if (GlobalDataManager.Instance != null)
                GlobalDataManager.Instance.OnSavedStatsUpdated -= OnGlobalStatsUpdated;

            Unhook(_str); Unhook(_agi); Unhook(_con); Unhook(_def);
            if (_applyButton != null) _applyButton.onClick.RemoveListener(Apply);
        }

        private void OnGlobalStatsUpdated(StatContainer updatedStats)
        {
            // 새로운 데이터가 오면 추격 루프가 다시 동작하도록 합니다.
            _isInitializedFromGlobal = false;
            Debug.Log("[StatCustomizer] Persistent sync restarted due to data update.");
        }

        private void RefreshSliderVisuals()
        {
            if (_str != null) { _str.SetItem(_baseStats.STR.Item); _str.SetInvestedWithoutNotify(_baseStats.STR.Invested); }
            if (_agi != null) { _agi.SetItem(_baseStats.AGI.Item); _agi.SetInvestedWithoutNotify(_baseStats.AGI.Invested); }
            if (_con != null) { _con.SetItem(_baseStats.CON.Item); _con.SetInvestedWithoutNotify(_baseStats.CON.Invested); }
            if (_def != null) { _def.SetItem(_baseStats.DEF.Item); _def.SetInvestedWithoutNotify(_baseStats.DEF.Invested); }
        }

        private void TryFindTarget()
        {
            if (_statManager == null)
            {
                _statManager = FindFirstObjectByType<StatManager>();
                if (_statManager != null) Debug.Log($"[StatCustomizer] Targeting: {_statManager.gameObject.name}");
            }
            
            if (_playerHealth == null && _statManager != null)
                _playerHealth = _statManager.GetComponent<HealthSystem>();

            if (PlayerHUD.Instance != null && _statManager != null && _playerHealth != null)
                PlayerHUD.Instance.SetTarget(_statManager, _playerHealth);
        }

        private void LoadFromTarget()
        {
            if (_statManager == null) return;
            _baseStats = _statManager.GetStatsCopy();
            _virtualStats = _baseStats;
            RefreshSliderVisuals();
        }

        private void Hook(StatSlider s) { if (s != null) s.InvestedChanged += OnInvestedChanged; }
        private void Unhook(StatSlider s) { if (s != null) s.InvestedChanged -= OnInvestedChanged; }

        private void OnInvestedChanged(StatSlider changed, float _)
        {
            int total = GetTotalInvested();
            if (total > TotalInvestedBudget && changed != null)
            {
                int overflow = total - TotalInvestedBudget;
                float next = Mathf.Max(0f, (int)changed.Invested - overflow);
                changed.SetInvestedWithoutNotify(next);
            }
            
            SyncVirtualFromSliders();
            RebuildBudgetAndPreview();

            // 사용자가 직접 슬라이더를 조작하기 시작했다면, 더 이상 추격 루프가 방해하지 않도록 합니다.
            _isInitializedFromGlobal = true;
        }

        private int GetTotalInvested()
        {
            int s = _str != null ? Mathf.RoundToInt(_str.Invested) : 0;
            int a = _agi != null ? Mathf.RoundToInt(_agi.Invested) : 0;
            int c = _con != null ? Mathf.RoundToInt(_con.Invested) : 0;
            int d = _def != null ? Mathf.RoundToInt(_def.Invested) : 0;
            return s + a + c + d;
        }

        public int GetRemainPoints() => TotalInvestedBudget - GetTotalInvested();

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
            if (_pointsText != null) _pointsText.text = $"{used} / {TotalInvestedBudget}";
            if (_remainPointsText != null) _remainPointsText.text = $"잔여 스탯: {remain}";

            Identity id = _identityCalculator.ResolveIdentity(_virtualStats, out _);
            if (_identityName != null) {
                _sb.Clear();
                _sb.Append(id.PrimaryStat); _sb.Append(' '); _sb.Append(id.Type.ToString().ToUpperInvariant());
                _identityName.text = _sb.ToString();
            }
            if (_identityIcon != null && _spriteSet != null) _identityIcon.sprite = _spriteSet.Resolve(id);

            if (_statManager != null)
            {
                _statManager.CalculatePreviewStats(_virtualStats, out float atk, out float def, out float maxHp, out float pene, out float regen, out float moveSpd, out float atkSpd);
                UpdatePreviewText(_previewAtkText, $"공격력 : {atk:F0}");
                UpdatePreviewText(_previewDefText, $"방어력 : {def:F1}%");
                UpdatePreviewText(_previewMaxHpText, $"최대 체력 : {maxHp:F0}");
                UpdatePreviewText(_previewPeneText, $"물리 관통력 : {pene:F1}%");
                UpdatePreviewText(_previewRegenText, $"재생력 : {regen:F1}/s");
                UpdatePreviewText(_previewMoveSpdText, $"이동속도 : {moveSpd:F2}");
                UpdatePreviewText(_previewAtkSpdText, $"공격속도 : {atkSpd:F2}");
            }
            if (_applyButton != null) _applyButton.interactable = remain >= 0;
        }

        private void UpdatePreviewText(TMP_Text textRef, string newValue)
        {
            if (textRef == null) return;
            if (textRef.text != newValue)
            {
                textRef.text = newValue;
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

        private Coroutine _floatingMessageRoutine;
        public void ShowFloatingMessage(string msg)
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
            if (_statManager == null) return;
            if (StatManager.IsMonostat(_virtualStats) && _playerHealth != null && _playerHealth.IsDead)
            {
                ShowFloatingMessage("사망 시 몰빵형 캐릭터로 전환할 수 없습니다.");
                LoadFromTarget();
                RebuildBudgetAndPreview();
                return;
            }

            var investedOnly = default(StatContainer);
            investedOnly.STR.Invested = _virtualStats.STR.Invested;
            investedOnly.AGI.Invested = _virtualStats.AGI.Invested;
            investedOnly.CON.Invested = _virtualStats.CON.Invested;
            investedOnly.DEF.Invested = _virtualStats.DEF.Invested;

            _statManager.ApplyInvestedOnly(investedOnly, recalculateIdentity: true);
            if (_playerHealth != null) _playerHealth.RefillHealth();

            var currentStats = _statManager.GetStatsCopy();
            GlobalDataManager.Instance.SavedStats = currentStats;
            if (PlayFabBattleManager.Instance != null)
            {
                _statManager.CalculatePreviewStats(currentStats, out float atk, out float defP, out float hp, out float pene, out float regen, out float move, out float atkSpd);
                PlayFabBattleManager.Instance.SavePlayerStats(currentStats, atk, hp, defP, pene, regen, move, atkSpd);
            }
            LoadFromTarget();
            RebuildBudgetAndPreview();
        }
    }
}
