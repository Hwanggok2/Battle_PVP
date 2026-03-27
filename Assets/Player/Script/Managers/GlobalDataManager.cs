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
        public static GlobalDataManager Instance { get; private set; }

        [Header("Persistent Data")]
        [SerializeField] private StatContainer _savedStats;
        public StatContainer SavedStats { get => _savedStats; set => _savedStats = value; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
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
                Debug.Log($"[GlobalDataManager] Found StatManager in scene {SceneManager.GetActiveScene().name}. Injecting stats.");
                statManager.ApplyStats(_savedStats, recalculateIdentity: true);
            }
        }
    }
}
