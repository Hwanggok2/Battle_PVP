using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattlePvp.Stats;
using Mirror;

namespace BattlePvp.UI
{
    /// <summary>
    /// 캐릭터 상세 정보 UI 요소들을 관리합니다.
    /// 플레이어 아이콘 클릭 시 활성화되어 최신 스탯 정보를 표시합니다.
    /// </summary>
    public class CharacterInfoController : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private GameObject _infoPanel;
        [SerializeField] private Button _playerIconButton;
        [SerializeField] private StatManager _statManager;
        // 로그인 ID 표시용 레이블 (로비 매니저나 PlayFab 데이터 연동 필요)
        [SerializeField] private TMP_Text _loginIdText;

        [Header("Primary Stats (Left Column)")]
        [SerializeField] private TMP_Text _strText;
        [SerializeField] private TMP_Text _agiText;
        [SerializeField] private TMP_Text _conText;
        [SerializeField] private TMP_Text _defText;

        [Header("Derived Stats (Right Column)")]
        [SerializeField] private TMP_Text _atkText;
        [SerializeField] private TMP_Text _defRateText;
        [SerializeField] private TMP_Text _maxHpText;
        [SerializeField] private TMP_Text _peneText;
        [SerializeField] private TMP_Text _regenText;
        [SerializeField] private TMP_Text _moveSpdText;
        [SerializeField] private TMP_Text _atkSpdText;

        private void Awake()
        {
            if (_playerIconButton != null)
            {
                _playerIconButton.onClick.AddListener(ToggleInfoPanel);
            }
        }

        private void OnEnable()
        {
            // [추가] 글로벌 데이터 업데이트 구독 (데이터가 나중에 도착할 경우 대비)
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated += OnGlobalStatsUpdated;
            }

            TryFindLocalPlayer();

            if (_infoPanel != null && _infoPanel.activeSelf)
            {
                UpdateStatsDisplay();
            }
        }

        private void OnDisable()
        {
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
            }

            if (_statManager != null)
            {
                _statManager.StatsChanged -= OnStatsChanged;
            }
        }

        private void OnGlobalStatsUpdated(StatContainer stats)
        {
            // 전역 데이터가 업데이트되면 UI도 즉시 갱신을 시도합니다.
            if (_infoPanel != null && _infoPanel.activeSelf)
            {
                UpdateStatsDisplay();
            }
        }

        private void TryFindLocalPlayer()
        {
            // [수정] 단순히 첫 번째 StatManager가 아니라 '로컬 플레이어' 권한을 가진 객체를 찾습니다.
            if (_statManager == null)
            {
                var allManagers = FindObjectsByType<StatManager>(FindObjectsSortMode.None);
                foreach (var sm in allManagers)
                {
                    if (sm.isLocalPlayer)
                    {
                        _statManager = sm;
                        _statManager.StatsChanged += OnStatsChanged;
                        Debug.Log("[CharacterInfo] Successfully bound to Local Player.");
                        break;
                    }
                }
            }
        }

        private void OnStatsChanged(StatContainer _)
        {
            // 스탯이 변경되면 UI를 즉시 갱신합니다.
            if (_infoPanel != null && _infoPanel.activeSelf)
            {
                UpdateStatsDisplay();
            }
        }

        private void OnDestroy()
        {
            if (_playerIconButton != null)
            {
                _playerIconButton.onClick.RemoveListener(ToggleInfoPanel);
            }
        }

        /// <summary>
        /// 상태창을 열고 닫는 토글 기능
        /// </summary>
        public void ToggleInfoPanel()
        {
            if (_infoPanel == null) return;
            
            bool isActive = _infoPanel.activeSelf;
            _infoPanel.SetActive(!isActive);

            if (!isActive)
            {
                UpdateStatsDisplay();
            }
        }

        /// <summary>
        /// UI를 표시할 때 호출되어 실제 스탯(FinalTotal)을 기반으로 UI를 갱신합니다.
        /// </summary>
        public void UpdateStatsDisplay()
        {
            TryFindLocalPlayer();

            StatContainer displayStats;
            
            if (_statManager != null)
            {
                displayStats = _statManager.GetStatsCopy();
            }
            else if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                // 플레이어가 없으면 글로벌 데이터 매니저의 값을 예비용으로 사용합니다.
                displayStats = BattlePvp.Managers.GlobalDataManager.Instance.SavedStats;
            }
            else
            {
                return;
            }

            // 1) Primary Stats
            if (_strText != null) _strText.text = $"STR : {Mathf.RoundToInt(displayStats.STR.Invested + displayStats.STR.Item)}";
            if (_agiText != null) _agiText.text = $"AGI : {Mathf.RoundToInt(displayStats.AGI.Invested + displayStats.AGI.Item)}";
            if (_conText != null) _conText.text = $"CON : {Mathf.RoundToInt(displayStats.CON.Invested + displayStats.CON.Item)}";
            if (_defText != null) _defText.text = $"DEF : {Mathf.RoundToInt(displayStats.DEF.Invested + displayStats.DEF.Item)}";

            // 2) Derived Stats
            // StatManager 인스턴스가 있다면 그 정교한 계산 로직을 쓰고, 없다면 임시 계산기(IdentityCalculator)를 활용하거나
            // 프리뷰 로직이 포함된 유틸리티가 있다면 그것을 사용합니다.
            // 여기서는 StatManager가 없어도 미리보기 수치를 낼 수 있도록 IdentityCalculator를 활용한 계산을 StatManager 내 정적/공용 메서드로 호출할 수 있다고 가정합니다.
            // (StatManager.CalculatePreviewStats는 현재 public void이므로 인스턴스가 필요함. 임시로 Dummy 객체나 정적 접근 고민 필요)
            
            if (_statManager != null)
            {
                _statManager.CalculatePreviewStats(displayStats, out float atk, out float def, out float maxHp, out float pene, out float regen, out float moveSpd, out float atkSpd);
                _atkText.text = $"공격력 : {atk:F0}";
                _defRateText.text = $"방어력 : {def:F1}%";
                _maxHpText.text = $"최대 체력 : {maxHp:F0}";
                _peneText.text = $"물리 관통력 : {pene:F1}%";
                _regenText.text = $"재생력 : {regen:F1}/s";
                _moveSpdText.text = $"이동속도 : {moveSpd:F2}";
                _atkSpdText.text = $"공격속도 : {atkSpd:F2}";
            }
            else
            {
                // 플레이어 객체가 아직 스폰되지 않았을 때는 수치만이라도 대략적으로 표시 (혹은 0 처리)
                // 현재 StatManager에 계산 로직이 몰려 있으므로 최소한의 표시만 진행
                if (_atkText != null) _atkText.text = "로딩 중...";
            }
        }
    }
}
