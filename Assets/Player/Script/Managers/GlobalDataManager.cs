using UnityEngine;
using BattlePvp.Stats;
using UnityEngine.SceneManagement;
using Mirror;
using BattlePvp.Networking;

namespace BattlePvp.Managers
{
    /// <summary>
    /// Core Task 1: 씬 전환(Lobby <-> Battle) 시에도 파괴되지 않고 플레이어 데이터를 유지하는 싱글톤 매니저.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GlobalDataManager : MonoBehaviour
    {
        private static GlobalDataManager _instance;
        private static bool _applicationIsQuitting = false;

        public static GlobalDataManager Instance
        {
            get
            {
                if (_applicationIsQuitting) 
                {
                    return null;
                }

                if (_instance == null)
                {
                    // 씬에서 먼저 찾아봅니다.
                    _instance = FindFirstObjectByType<GlobalDataManager>();

                    // 씬에도 없다면 새로 생성합니다.
                    if (_instance == null)
                    {
                        var go = new GameObject("GlobalDataManager (Auto-Generated)");
                        _instance = go.AddComponent<GlobalDataManager>();
                        DontDestroyOnLoad(go);
                        Debug.Log("[GlobalDataManager] Automatic instance created and marked as DontDestroyOnLoad.");
                    }
                }
                return _instance;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationIsQuitting = true;
        }

        [Header("Persistent Data")]
        [SerializeField] private StatContainer _savedStats;
        [SerializeField] private string _playerNickname = "Unknown";
        [SerializeField] private int _cumulativeKills;
        [SerializeField] private int _cumulativeDeaths;
        private bool _isPlayerStatsLoadInFlight;
        private bool _isCombatRecordLoadInFlight;
        private bool _hasLoadedCombatRecord;

        /// <summary>
        /// 로그인 시 설정된 플레이어 닉네임. 씬 전환 후에도 유지됩니다.
        /// </summary>
        public string PlayerNickname
        {
            get => _playerNickname;
            set
            {
                _playerNickname = value;
                Debug.Log($"[GlobalDataManager] PlayerNickname set to: {_playerNickname}");
            }
        }
        
        public event System.Action<StatContainer> OnSavedStatsUpdated;
        public event System.Action<int, int> OnCombatRecordUpdated;

        public int CumulativeKills => _cumulativeKills;
        public int CumulativeDeaths => _cumulativeDeaths;
        public float CumulativeKillsPerDeath => _cumulativeDeaths <= 0 ? _cumulativeKills : _cumulativeKills / (float)_cumulativeDeaths;

        public StatContainer SavedStats 
        { 
            get => _savedStats; 
            set 
            {
                // [추가] 로드된 스탯이 총합 30을 넘지 않도록 강제 보정 (데이터 무결성 확보)
                _savedStats = ClampStatBudget(value, 30);
                OnSavedStatsUpdated?.Invoke(_savedStats);
                Debug.Log($"[GlobalDataManager] SavedStats Updated (Clamped to 30): {_savedStats.STR.Invested}/{_savedStats.AGI.Invested}/{_savedStats.CON.Invested}/{_savedStats.DEF.Invested}");
            }
        }

        public void SetCombatRecord(int kills, int deaths)
        {
            _cumulativeKills = Mathf.Max(0, kills);
            _cumulativeDeaths = Mathf.Max(0, deaths);
            _hasLoadedCombatRecord = true;
            OnCombatRecordUpdated?.Invoke(_cumulativeKills, _cumulativeDeaths);
            Debug.Log($"[GlobalDataManager] Combat record updated: K={_cumulativeKills}, D={_cumulativeDeaths}");
        }

        public void AddCombatRecord(int killsDelta, int deathsDelta)
        {
            SetCombatRecord(_cumulativeKills + killsDelta, _cumulativeDeaths + deathsDelta);
        }

        private StatContainer ClampStatBudget(StatContainer stats, int budget)
        {
            float total = stats.STR.Invested + stats.AGI.Invested + stats.CON.Invested + stats.DEF.Invested;
            if (total > budget)
            {
                float overflow = total - budget;
                // 초구가분을 가장 높은 스탯에서 삭감 (단순 보정 로직)
                if (stats.AGI.Invested >= overflow) stats.AGI.Invested -= overflow;
                else if (stats.STR.Invested >= overflow) stats.STR.Invested -= overflow;
                else if (stats.CON.Invested >= overflow) stats.CON.Invested -= overflow;
                else if (stats.DEF.Invested >= overflow) stats.DEF.Invested -= overflow;
            }
            return stats;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 씬 로드 시마다 플레이어를 찾아 데이터 주입
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[GlobalDataManager] Scene Loaded: {scene.name}. Attempting to inject stats.");
            EnsurePlayerStatsLoadedForScene(scene.name);
            EnsureCombatRecordLoadedForScene(scene.name);
            TryInjectToPlayer();
        }

        /// <summary>
        /// 새로운 씬에서 Player 오브젝트를 찾아 저장된 데이터를 주입(Dependency Injection)하고 초기화합니다.
        /// </summary>
        public void TryInjectToPlayer()
        {
            // "Player"라는 태그를 가진 오브젝트나 StatManager가 붙은 오브젝트를 찾습니다.
            var statManager = StatManager.Local;
            if (statManager == null && NetworkClient.localPlayer != null)
                statManager = NetworkClient.localPlayer.GetComponent<StatManager>();
            if (statManager != null)
            {
                Debug.Log($"[GlobalDataManager] Found StatManager in scene {SceneManager.GetActiveScene().name}. Injecting stats: STR={_savedStats.STR.Invested}");
                statManager.ApplyStats(_savedStats, recalculateIdentity: true);
                BattlePvp.UI.PlayerHUD.BindToPlayer(statManager);
            }
            else
            {
                Debug.Log("[GlobalDataManager] No StatManager found in current scene to inject stats.");
            }
        }

        private void EnsurePlayerStatsLoadedForScene(string sceneName)
        {
            if (!IsPlayerStatScene(sceneName) || HasUsableSavedStats() || _isPlayerStatsLoadInFlight)
                return;

            if (PlayFabAuthManager.Instance == null || !PlayFabAuthManager.Instance.IsLoggedIn())
                return;

            PlayFabBattleManager battleManager = PlayFabBattleManager.Instance;
            if (battleManager == null)
                battleManager = FindFirstObjectByType<PlayFabBattleManager>();

            if (battleManager == null)
            {
                Debug.LogWarning("[GlobalDataManager] PlayFabBattleManager not found. Player stats cannot be loaded for this scene yet.");
                return;
            }

            _isPlayerStatsLoadInFlight = true;
            battleManager.LoadPlayerStats(stats =>
            {
                _isPlayerStatsLoadInFlight = false;
                SavedStats = stats;
                TryInjectToPlayer();
                Debug.Log("[GlobalDataManager] Player stats loaded automatically for scene entry.");
            });
        }

        private void EnsureCombatRecordLoadedForScene(string sceneName)
        {
            if (!IsPlayerStatScene(sceneName) || _hasLoadedCombatRecord || _isCombatRecordLoadInFlight)
                return;

            if (PlayFabAuthManager.Instance == null || !PlayFabAuthManager.Instance.IsLoggedIn())
                return;

            PlayFabBattleManager battleManager = PlayFabBattleManager.Instance;
            if (battleManager == null)
                battleManager = FindFirstObjectByType<PlayFabBattleManager>();

            if (battleManager == null)
            {
                Debug.LogWarning("[GlobalDataManager] PlayFabBattleManager not found. Combat record cannot be loaded for this scene yet.");
                return;
            }

            _isCombatRecordLoadInFlight = true;
            battleManager.LoadCombatRecord((kills, deaths) =>
            {
                _isCombatRecordLoadInFlight = false;
                SetCombatRecord(kills, deaths);
                Debug.Log("[GlobalDataManager] Combat record loaded automatically for scene entry.");
            });
        }

        private bool HasUsableSavedStats()
        {
            return _savedStats.STR.Invested + _savedStats.AGI.Invested + _savedStats.CON.Invested + _savedStats.DEF.Invested > 0.1f;
        }

        private static bool IsPlayerStatScene(string sceneName)
        {
            return sceneName == "Lobby" || sceneName == "Battle" || sceneName == "Battle_wait" || sceneName == "Battle_waiting";
        }
    }
}
