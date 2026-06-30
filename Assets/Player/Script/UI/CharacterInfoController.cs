using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattlePvp.Stats;
using BattlePvp.Combat;
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

        [Header("Battle Record")]
        [SerializeField] private TMP_Text _killsText;
        [SerializeField] private TMP_Text _deathsText;
        [SerializeField] private TMP_Text _killsPerDeathText;

        private ScoreSystem _scoreSystem;

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
                BattlePvp.Managers.GlobalDataManager.Instance.OnCombatRecordUpdated += OnCombatRecordUpdated;
            }
            ScoreSystem.OnScoreUpdated += OnScoreUpdated;

            TryFindLocalPlayer();
            EnsureBattleRecordTexts();

            UpdateStatsDisplay();
        }

        private void OnDisable()
        {
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
                BattlePvp.Managers.GlobalDataManager.Instance.OnCombatRecordUpdated -= OnCombatRecordUpdated;
            }
            ScoreSystem.OnScoreUpdated -= OnScoreUpdated;

            if (_statManager != null)
            {
                _statManager.StatsChanged -= OnStatsChanged;
            }
        }

        private void OnGlobalStatsUpdated(StatContainer stats)
        {
            // 전역 데이터가 업데이트되면 UI도 즉시 갱신을 시도합니다.
            UpdateStatsDisplay();
        }

        private void TryFindLocalPlayer()
        {
            if (_statManager == null && StatManager.Local != null)
                _statManager = StatManager.Local;

            if (_statManager == null && NetworkClient.localPlayer != null)
                _statManager = NetworkClient.localPlayer.GetComponent<StatManager>();

            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();

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

            if (_statManager == null)
                _statManager = FindFirstObjectByType<StatManager>();

            if (_statManager != null)
            {
                _statManager.StatsChanged -= OnStatsChanged;
                _statManager.StatsChanged += OnStatsChanged;

                if (_scoreSystem == null)
                    _scoreSystem = _statManager.GetComponent<ScoreSystem>();
            }

            if (_scoreSystem == null && NetworkClient.localPlayer != null)
                _scoreSystem = NetworkClient.localPlayer.GetComponent<ScoreSystem>();
        }

        private void OnStatsChanged(StatContainer _)
        {
            // 스탯이 변경되면 UI를 즉시 갱신합니다.
            if (_infoPanel != null && _infoPanel.activeSelf)
            {
                UpdateStatsDisplay();
            }
        }

        private void OnScoreUpdated(ScoreSystem score)
        {
            if (_scoreSystem != null && score != null && score != _scoreSystem)
                return;

            if (_infoPanel == null || _infoPanel.activeSelf)
                UpdateBattleRecordDisplay();
        }

        private void OnCombatRecordUpdated(int kills, int deaths)
        {
            if (_infoPanel == null || _infoPanel.activeSelf)
                UpdateBattleRecordDisplay();
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
                EnsureBattleRecordTexts();
                UpdateStatsDisplay();
            }
        }

        /// <summary>
        /// UI를 표시할 때 호출되어 실제 스탯(FinalTotal)을 기반으로 UI를 갱신합니다.
        /// </summary>
        public void UpdateStatsDisplay()
        {
            TryFindLocalPlayer();

            // 로그인 ID 표시
            if (_loginIdText != null && BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                _loginIdText.text = BattlePvp.Managers.GlobalDataManager.Instance.PlayerNickname;
            }

            UpdateBattleRecordDisplay();

            StatContainer displayStats;
            
            if (TryGetSavedStats(out StatContainer savedStats))
            {
                displayStats = savedStats;
            }
            else if (_statManager != null)
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

        private static bool TryGetSavedStats(out StatContainer stats)
        {
            stats = default;

            if (BattlePvp.Managers.GlobalDataManager.Instance == null)
                return false;

            stats = BattlePvp.Managers.GlobalDataManager.Instance.SavedStats;
            float total = stats.STR.Invested + stats.AGI.Invested + stats.CON.Invested + stats.DEF.Invested;
            return total > 0.1f;
        }

        private void EnsureBattleRecordTexts()
        {
            if (_infoPanel == null)
                return;

            if (_killsText == null) _killsText = FindTextByName("kill");
            if (_deathsText == null) _deathsText = FindTextByName("death");
            if (_killsPerDeathText == null) _killsPerDeathText = FindTextByName("kda", "kd", "ratio");

            TMP_Text template = _atkSpdText != null ? _atkSpdText :
                                _regenText != null ? _regenText :
                                _defText != null ? _defText :
                                _infoPanel.GetComponentInChildren<TMP_Text>(true);

            Transform parent = template != null ? template.transform.parent : _infoPanel.transform;
            if (_killsText == null) _killsText = CreateRecordText(parent, template, "KillsText", 1);
            if (_deathsText == null) _deathsText = CreateRecordText(parent, template, "DeathsText", 2);
            if (_killsPerDeathText == null) _killsPerDeathText = CreateRecordText(parent, template, "KillsPerDeathText", 3);
        }

        private TMP_Text FindTextByName(params string[] tokens)
        {
            if (_infoPanel == null || tokens == null)
                return null;

            TMP_Text[] texts = _infoPanel.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text == null)
                    continue;

                string lowerName = text.name.ToLowerInvariant();
                foreach (string token in tokens)
                {
                    if (!string.IsNullOrEmpty(token) && lowerName.Contains(token))
                        return text;
                }
            }

            return null;
        }

        private TMP_Text CreateRecordText(Transform parent, TMP_Text template, string objectName, int lineOffset)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSize = template.fontSize;
                text.color = template.color;
                text.alignment = template.alignment;
                text.enableAutoSizing = template.enableAutoSizing;
                text.fontSizeMin = template.fontSizeMin;
                text.fontSizeMax = template.fontSizeMax;

                RectTransform sourceRect = template.rectTransform;
                RectTransform rect = text.rectTransform;
                rect.anchorMin = sourceRect.anchorMin;
                rect.anchorMax = sourceRect.anchorMax;
                rect.pivot = sourceRect.pivot;
                rect.sizeDelta = sourceRect.sizeDelta;

                if (parent.GetComponent<LayoutGroup>() == null)
                {
                    float lineHeight = Mathf.Max(sourceRect.rect.height, template.fontSize + 4f);
                    rect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, -lineHeight * lineOffset);
                }
            }

            return text;
        }

        private void UpdateBattleRecordDisplay()
        {
            EnsureBattleRecordTexts();

            if (_scoreSystem == null)
            {
                if (NetworkClient.localPlayer != null)
                    _scoreSystem = NetworkClient.localPlayer.GetComponent<ScoreSystem>();
                else if (_statManager != null)
                    _scoreSystem = _statManager.GetComponent<ScoreSystem>();
            }

            int kills = 0;
            int deaths = 0;

            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                kills = BattlePvp.Managers.GlobalDataManager.Instance.CumulativeKills;
                deaths = BattlePvp.Managers.GlobalDataManager.Instance.CumulativeDeaths;
            }
            else if (_scoreSystem != null)
            {
                kills = _scoreSystem.CurrentKills;
                deaths = _scoreSystem.CurrentDeaths;
            }

            float killsPerDeath = deaths <= 0 ? kills : kills / (float)deaths;

            if (_killsText != null) _killsText.text = $"Kills : {kills}";
            if (_deathsText != null) _deathsText.text = $"Deaths : {deaths}";
            if (_killsPerDeathText != null) _killsPerDeathText.text = $"K/D : {killsPerDeath:F2}";
        }
    }
}
