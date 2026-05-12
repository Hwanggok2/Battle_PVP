using UnityEngine;
using BattlePvp.Stats;
using UnityEngine.SceneManagement;

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
        
        public event System.Action<StatContainer> OnSavedStatsUpdated;

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
            TryInjectToPlayer();
        }

        /// <summary>
        /// 새로운 씬에서 Player 오브젝트를 찾아 저장된 데이터를 주입(Dependency Injection)하고 초기화합니다.
        /// </summary>
        public void TryInjectToPlayer()
        {
            // "Player"라는 태그를 가진 오브젝트나 StatManager가 붙은 오브젝트를 찾습니다.
            var statManager = FindFirstObjectByType<StatManager>();
            if (statManager != null)
            {
                Debug.Log($"[GlobalDataManager] Found StatManager in scene {SceneManager.GetActiveScene().name}. Injecting stats: STR={_savedStats.STR.Invested}");
                statManager.ApplyStats(_savedStats, recalculateIdentity: true);
            }
            else
            {
                Debug.Log("[GlobalDataManager] No StatManager found in current scene to inject stats.");
            }
        }
    }
}
