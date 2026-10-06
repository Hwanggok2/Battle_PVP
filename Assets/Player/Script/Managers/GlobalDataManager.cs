using UnityEngine;
using BattlePvp.Stats;
using UnityEngine.SceneManagement;
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
        private const int DefaultStatPresetSlotCount = 3;

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
        [SerializeField] private int _selectedStatPresetSlot = 0;
        [SerializeField] private StatContainer[] _statPresetSlots = new StatContainer[DefaultStatPresetSlotCount];
        [SerializeField] private bool[] _statPresetSlotUsed = new bool[DefaultStatPresetSlotCount];
        [SerializeField] private StatContainer _strategistTargetPreset;
        [SerializeField] private bool _hasStrategistTargetPreset;
        [SerializeField] private string _playerNickname = "Unknown";
        [SerializeField] private int _cumulativeKills;
        [SerializeField] private int _cumulativeDeaths;
        private bool _hasLoadedPlayerStats;
        private bool _isPlayerStatsLoadInFlight;
        private bool _hasLoadedCombatRecord;
        private int _pendingKills;
        private int _pendingDeaths;
        private int _profileSession;
        private int _profileLoadRequest;
        private int _profileStateVersion;
        private readonly System.Collections.Generic.List<System.Action<bool>> _profileLoadCallbacks =
            new System.Collections.Generic.List<System.Action<bool>>();

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
        public event System.Action PlayerBindingRequested;
        public event System.Action<int, StatContainer, bool> OnStatPresetSlotChanged;
        public event System.Action<StatContainer, bool> OnStrategistTargetPresetChanged;
        public event System.Action<int, int> OnCombatRecordUpdated;

        public int CumulativeKills => _cumulativeKills;
        public int CumulativeDeaths => _cumulativeDeaths;
        public float CumulativeKillsPerDeath => _cumulativeDeaths <= 0 ? _cumulativeKills : _cumulativeKills / (float)_cumulativeDeaths;
        public bool HasLoadedPlayerStats => _hasLoadedPlayerStats;
        public bool IsPlayerStatsLoadInFlight => _isPlayerStatsLoadInFlight;
        public bool HasLoadedCombatRecord => _hasLoadedCombatRecord;
        public int ProfileSessionVersion => _profileSession;
        public bool IsCurrentActiveInstance => _instance == this && isActiveAndEnabled;

        public PlayerProfileSnapshot CaptureProfileSnapshot()
        {
            EnsureStatPresetArrays();
            return new PlayerProfileSnapshot
            {
                Stats = _savedStats,
                Slots = (StatContainer[])_statPresetSlots.Clone(),
                SlotUsed = (bool[])_statPresetSlotUsed.Clone(),
                SelectedSlot = _selectedStatPresetSlot,
                StrategistPreset = _strategistTargetPreset,
                HasStrategistPreset = _hasStrategistTargetPreset
            };
        }

        public void BeginPlayerSession()
        {
            _profileSession++;
            _profileLoadRequest++;
            var callbacks = TakeProfileLoadCallbacks();
            _hasLoadedPlayerStats = false;
            _hasLoadedCombatRecord = false;
            _isPlayerStatsLoadInFlight = false;
            _savedStats = default;
            _statPresetSlots = new StatContainer[DefaultStatPresetSlotCount];
            _statPresetSlotUsed = new bool[DefaultStatPresetSlotCount];
            _selectedStatPresetSlot = 0;
            _strategistTargetPreset = default;
            _hasStrategistTargetPreset = false;
            _cumulativeKills = _cumulativeDeaths = _pendingKills = _pendingDeaths = 0;
            int session = _profileSession;
            int stateVersion = ++_profileStateVersion;
            PublishPresetState(session, stateVersion);
            NotifySubscribers(OnCombatRecordUpdated, subscriber => ((System.Action<int, int>)subscriber)(0, 0), session, stateVersion);
            CompleteProfileLoadCallbacks(callbacks, false, session);
        }

        public StatContainer SavedStats 
        { 
            get => _savedStats; 
            set 
            {
                // [추가] 로드된 스탯이 총합 30을 넘지 않도록 강제 보정 (데이터 무결성 확보)
                _savedStats = ClampStatBudget(value, 30);
                EnsureStatPresetArrays();
                float total = _savedStats.STR.Invested + _savedStats.AGI.Invested + _savedStats.CON.Invested + _savedStats.DEF.Invested;
                if (total > 0.1f && _selectedStatPresetSlot >= 0 && _selectedStatPresetSlot < _statPresetSlots.Length)
                {
                    _statPresetSlots[_selectedStatPresetSlot] = _savedStats;
                    _statPresetSlotUsed[_selectedStatPresetSlot] = true;
                    OnStatPresetSlotChanged?.Invoke(_selectedStatPresetSlot, _savedStats, true);
                }
                OnSavedStatsUpdated?.Invoke(_savedStats);
                Debug.Log($"[GlobalDataManager] SavedStats Updated (Clamped to 30): {_savedStats.STR.Invested}/{_savedStats.AGI.Invested}/{_savedStats.CON.Invested}/{_savedStats.DEF.Invested}");
            }
        }

        public int SelectedStatPresetSlot => _selectedStatPresetSlot;
        public bool HasStrategistTargetPreset => _hasStrategistTargetPreset;
        public StatContainer StrategistTargetPreset => _strategistTargetPreset;
        public int StatPresetSlotCount
        {
            get
            {
                EnsureStatPresetArrays();
                return _statPresetSlots.Length;
            }
        }

        public bool HasCompleteSavedStats()
        {
            return IsCompleteStatPreset(_savedStats);
        }

        public bool HasValidStrategistTargetPreset()
        {
            return _hasStrategistTargetPreset
                && IsCompleteStatPreset(_strategistTargetPreset)
                && IsStrategistPreset(_strategistTargetPreset);
        }

        public static bool IsCompleteStatPreset(StatContainer stats)
        {
            return StatValidation.IsCompletePreset(stats);
        }

        public static bool IsStrategistPreset(StatContainer stats)
        {
            Identity identity = new IdentityCalculator().ResolveIdentity(stats, out _);
            return identity.Type == IdentityType.Strategist;
        }

        public bool HasStatPresetSlot(int slotIndex)
        {
            return slotIndex >= 0
                && _statPresetSlotUsed != null
                && slotIndex < _statPresetSlotUsed.Length
                && _statPresetSlotUsed[slotIndex];
        }

        public StatContainer GetStatPresetSlot(int slotIndex)
        {
            if (_statPresetSlots == null || slotIndex < 0 || slotIndex >= _statPresetSlots.Length)
                return default;

            return _statPresetSlots[slotIndex];
        }

        public void ApplyLoadedStatPresetData(
            StatContainer[] slots,
            bool[] slotUsed,
            int selectedSlot,
            StatContainer strategistTargetPreset,
            bool hasStrategistTargetPreset)
        {
            SetLoadedStatPresetData(slots, slotUsed, selectedSlot, strategistTargetPreset, hasStrategistTargetPreset);
            PublishPresetState(_profileSession, _profileStateVersion);
        }

        private void SetLoadedStatPresetData(StatContainer[] slots, bool[] slotUsed, int selectedSlot,
            StatContainer strategistTargetPreset, bool hasStrategistTargetPreset)
        {
            EnsureStatPresetArrays();

            int copyCount = slots != null ? Mathf.Min(slots.Length, _statPresetSlots.Length) : 0;
            for (int i = 0; i < copyCount; i++)
            {
                _statPresetSlots[i] = ClampStatBudget(slots[i], 30);
                _statPresetSlotUsed[i] = slotUsed != null && i < slotUsed.Length && slotUsed[i];
            }

            _selectedStatPresetSlot = Mathf.Clamp(selectedSlot, 0, _statPresetSlots.Length - 1);
            _savedStats = _statPresetSlotUsed[_selectedStatPresetSlot] ? _statPresetSlots[_selectedStatPresetSlot] : default;
            _strategistTargetPreset = ClampStatBudget(strategistTargetPreset, 30);
            _hasStrategistTargetPreset = hasStrategistTargetPreset;
            _hasLoadedPlayerStats = true;
            _profileStateVersion++;
        }

        private void PublishPresetState(int session, int stateVersion)
        {
            int selected = _selectedStatPresetSlot;
            StatContainer saved = _savedStats;
            bool used = _statPresetSlotUsed[selected];
            StatContainer strategist = _strategistTargetPreset;
            bool hasStrategist = _hasStrategistTargetPreset;
            NotifySubscribers(OnStatPresetSlotChanged,
                subscriber => ((System.Action<int, StatContainer, bool>)subscriber)(selected, saved, used), session, stateVersion);
            NotifySubscribers(OnStrategistTargetPresetChanged,
                subscriber => ((System.Action<StatContainer, bool>)subscriber)(strategist, hasStrategist), session, stateVersion);
            NotifySubscribers(OnSavedStatsUpdated, subscriber => ((System.Action<StatContainer>)subscriber)(saved), session, stateVersion);
        }

        public void ApplyLoadedPlayerStats(StatContainer stats)
        {
            _hasLoadedPlayerStats = true;
            SavedStats = stats;
        }

        public void SelectStatPresetSlot(int slotIndex)
        {
            EnsureStatPresetArrays();
            if (slotIndex < 0 || slotIndex >= _statPresetSlots.Length)
                return;

            _selectedStatPresetSlot = slotIndex;
            StatContainer selectedStats = _statPresetSlotUsed[slotIndex] ? _statPresetSlots[slotIndex] : default;
            _savedStats = selectedStats;
            OnStatPresetSlotChanged?.Invoke(_selectedStatPresetSlot, selectedStats, _statPresetSlotUsed[slotIndex]);
            OnSavedStatsUpdated?.Invoke(_savedStats);
            TryInjectToPlayer();
        }

        public void SaveSelectedStatPresetSlot(StatContainer stats)
        {
            SaveStatPresetSlot(_selectedStatPresetSlot, stats, selectAfterSave: true);
        }

        public void SaveStatPresetSlot(int slotIndex, StatContainer stats, bool selectAfterSave, bool applyToPlayer = true)
        {
            EnsureStatPresetArrays();
            if (slotIndex < 0 || slotIndex >= _statPresetSlots.Length)
                return;

            StatContainer clamped = ClampStatBudget(stats, 30);
            _statPresetSlots[slotIndex] = clamped;
            _statPresetSlotUsed[slotIndex] = true;

            if (selectAfterSave)
            {
                _selectedStatPresetSlot = slotIndex;
                _savedStats = clamped;
                OnSavedStatsUpdated?.Invoke(_savedStats);
                if (applyToPlayer) TryInjectToPlayer();
            }

            OnStatPresetSlotChanged?.Invoke(slotIndex, clamped, true);
        }

        public void SaveStrategistTargetPreset(StatContainer stats)
        {
            _strategistTargetPreset = ClampStatBudget(stats, 30);
            _hasStrategistTargetPreset = true;
            OnStrategistTargetPresetChanged?.Invoke(_strategistTargetPreset, true);
        }

        private void EnsureStatPresetArrays()
        {
            if (_statPresetSlots == null || _statPresetSlots.Length < DefaultStatPresetSlotCount)
                System.Array.Resize(ref _statPresetSlots, DefaultStatPresetSlotCount);

            if (_statPresetSlotUsed == null || _statPresetSlotUsed.Length < _statPresetSlots.Length)
                System.Array.Resize(ref _statPresetSlotUsed, _statPresetSlots.Length);
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
            if (!_hasLoadedCombatRecord)
            {
                _pendingKills += Mathf.Max(0, killsDelta);
                _pendingDeaths += Mathf.Max(0, deathsDelta);
                return;
            }
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

            if (GetComponent<LocalPlayerProfileBinding>() == null)
                gameObject.AddComponent<LocalPlayerProfileBinding>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            CancelProfileLoad();
        }

        private void OnDisable() => CancelProfileLoad();

        private void CancelProfileLoad()
        {
            _profileSession++;
            _profileLoadRequest++;
            _isPlayerStatsLoadInFlight = false;
            CompleteProfileLoadCallbacks(TakeProfileLoadCallbacks(), false, _profileSession);
        }

        public void EnsurePlayerStatsLoadedForCurrentScene()
        {
            EnsurePlayerStatsLoadedForScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// 새로운 씬에서 Player 오브젝트를 찾아 저장된 데이터를 주입(Dependency Injection)하고 초기화합니다.
        /// </summary>
        public void TryInjectToPlayer()
        {
            if (!_hasLoadedPlayerStats) return;
            PlayerBindingRequested?.Invoke();
        }

        private void EnsurePlayerStatsLoadedForScene(string sceneName)
        {
            if (IsPlayerStatScene(sceneName)) LoadProfileForCurrentAccount();
        }

        public void LoadProfileForCurrentAccount(System.Action<bool> completed = null)
        {
            if (this == null || !isActiveAndEnabled)
            {
                NotifyProfileCallback(completed, false);
                return;
            }
            if (_hasLoadedPlayerStats && _hasLoadedCombatRecord)
            {
                NotifyProfileCallback(completed, true);
                return;
            }
            if (PlayFabAuthManager.Instance == null || !PlayFabAuthManager.Instance.IsLoggedIn() ||
                PlayFabBattleManager.Instance == null)
            {
                NotifyProfileCallback(completed, false);
                return;
            }

            BeginProfileLoad(PlayFabBattleManager.Instance.LoadPlayerProfile, completed);
        }

        private void BeginProfileLoad(System.Action<System.Action<PlayerProfileLoadResult>> load, System.Action<bool> completed)
        {
            if (completed != null) _profileLoadCallbacks.Add(completed);
            if (_isPlayerStatsLoadInFlight) return;
            _isPlayerStatsLoadInFlight = true;
            int session = _profileSession;
            int request = ++_profileLoadRequest;
            try { load(result => FinishProfileLoad(session, request, result)); }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                FinishProfileLoad(session, request, new PlayerProfileLoadResult(ProfileLoadStatus.Failure));
            }
        }

        private void FinishProfileLoad(int session, int request, PlayerProfileLoadResult result)
        {
            if (session != _profileSession || request != _profileLoadRequest || !_isPlayerStatsLoadInFlight) return;
            // Detach this request's waiters before state notifications can reset the session or
            // start another request. A reentrant request owns a different callback collection.
            var callbacks = TakeProfileLoadCallbacks();
            _isPlayerStatsLoadInFlight = false;
            bool succeeded = result != null && result.Succeeded && result.Profile != null;
            if (succeeded)
            {
                PlayerProfileSnapshot profile = result.Profile;
                SetLoadedStatPresetData(profile.Slots, profile.SlotUsed, profile.SelectedSlot,
                    profile.StrategistPreset, profile.HasStrategistPreset);
                bool hasPendingRecord = _pendingKills != 0 || _pendingDeaths != 0;
                _cumulativeKills = Mathf.Max(0, result.Kills + _pendingKills);
                _cumulativeDeaths = Mathf.Max(0, result.Deaths + _pendingDeaths);
                _pendingKills = _pendingDeaths = 0;
                _hasLoadedCombatRecord = true;
                int stateVersion = _profileStateVersion;
                int kills = _cumulativeKills;
                int deaths = _cumulativeDeaths;
                if (hasPendingRecord)
                {
                    try { PlayFabBattleManager.Instance?.SaveCombatRecord(kills, deaths); }
                    catch (System.Exception error) { Debug.LogException(error); }
                }
                PublishPresetState(session, stateVersion);
                NotifySubscribers(OnCombatRecordUpdated,
                    subscriber => ((System.Action<int, int>)subscriber)(kills, deaths), session, stateVersion);
                NotifySubscribers(PlayerBindingRequested, subscriber => ((System.Action)subscriber)(), session, stateVersion);
            }
            else
            {
                // Keep cached data and remain retryable. A network error is not an empty account.
                Debug.LogWarning("[GlobalDataManager] Profile load failed: " + result?.Error);
            }
            CompleteProfileLoadCallbacks(callbacks, succeeded, session);
        }

        private System.Action<bool>[] TakeProfileLoadCallbacks()
        {
            var callbacks = _profileLoadCallbacks.ToArray();
            _profileLoadCallbacks.Clear();
            return callbacks;
        }

        private void CompleteProfileLoadCallbacks(System.Action<bool>[] callbacks, bool succeeded, int session)
        {
            foreach (var callback in callbacks) NotifyProfileCallback(callback, succeeded && session == _profileSession);
        }

        private static void NotifyProfileCallback(System.Action<bool> callback, bool succeeded)
        {
            try { callback?.Invoke(succeeded); }
            catch (System.Exception error) { Debug.LogException(error); }
        }

        private void NotifySubscribers(System.Delegate subscribers, System.Action<System.Delegate> notify, int session, int stateVersion)
        {
            if (subscribers == null) return;
            foreach (System.Delegate subscriber in subscribers.GetInvocationList())
            {
                if (session != _profileSession || stateVersion != _profileStateVersion) return;
                try { notify(subscriber); }
                catch (System.Exception error) { Debug.LogException(error); }
            }
        }

        private static bool IsPlayerStatScene(string sceneName)
        {
            return sceneName == "Lobby" || sceneName == "Battle" || sceneName == "Battle_wait" || sceneName == "Battle_waiting";
        }
    }
}
