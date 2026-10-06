using System;
using UnityEngine;
using Mirror;

namespace BattlePvp.Stats
{
    /// <summary>
    /// 스탯을 기준으로 Identity를 판정하고 상태 변경 이벤트를 방출하는 MonoBehaviour 골격.
    /// </summary>
    public sealed class StatManager : Mirror.NetworkBehaviour, IIdentitySource
    {
        public static StatManager Local { get; private set; }
        public static event Action<StatManager> LocalChanged;
        private BattlePvp.Managers.GlobalDataManager _profileData;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLocalBinding()
        {
            Local = null;
            LocalChanged = null;
        }

        private static void SetLocal(StatManager player)
        {
            if (ReferenceEquals(Local, player)) return;
            Local = player;
            LocalChanged?.Invoke(player);
        }

        [Header("Stat Data")]
        [SyncVar(hook = nameof(OnStatsSynced))]
        [SerializeField] private StatContainer _stats;
        [SyncVar] private bool _serverStatsInitialized;
        private double _nextStatRequestAt;
        private double _initialStatsDeadline;
        public bool HasServerStats => _serverStatsInitialized;
        private BattlePvp.Characters.PlayerAppearance _combatAppearance;
        public bool HasServerCombatStats
        {
            get
            {
                if (!HasServerStats) return false;
                if (_combatAppearance == null) _combatAppearance = GetComponent<BattlePvp.Characters.PlayerAppearance>();
                return _combatAppearance == null || _combatAppearance.HasServerSelectionReady;
            }
        }
        public bool IsAllocationComplete => _serverStatsInitialized && StatValidation.IsCompletePreset(_stats);
        private uint _nextApplyRequestId;
        private uint _pendingApplyRequestId;
        private Action<bool, string> _pendingApplyCallback;
        private double _pendingApplyDeadline;
        private Coroutine _applyTimeoutRoutine;
        private const double ApplyRequestTimeoutSeconds = 10d;

        /// <summary>
        /// 캐릭터가 특정 한 스탯에만 투자했는지(몰빵형) 확인하는 유틸리티 메서드입니다.
        /// </summary>
        public static bool IsMonostat(StatContainer stats)
        {
            int categoriesWithPoints = 0;
            if (stats.STR.Invested > 0) categoriesWithPoints++;
            if (stats.AGI.Invested > 0) categoriesWithPoints++;
            if (stats.CON.Invested > 0) categoriesWithPoints++;
            if (stats.DEF.Invested > 0) categoriesWithPoints++;

            return categoriesWithPoints == 1;
        }

        [Header("Identity")]
        [SerializeField] private bool _autoRecalculateOnEnable = true;

        /// <summary>
        /// 현재 판정된 Identity.
        /// </summary>
        public Identity CurrentIdentity { get; private set; }

        /// <summary>
        /// Identity 변경 이벤트.
        /// </summary>
        public event Action<Identity> IdentityChanged;

        /// <summary>
        /// 스탯 데이터 변경 이벤트.
        /// 커스터마이저/UI/HealthSystem 등이 Update 없이 동기화할 수 있다.
        /// </summary>
        public event Action<StatContainer> StatsChanged;
        public event Action DerivedStatsChanged;

        [SyncVar(hook = nameof(OnCombatMultiplierChanged))] private float _allCombatMultiplier = 1f;
        [SyncVar(hook = nameof(OnCombatMultiplierChanged))] private float _defenseCombatMultiplier = 1f;
        private void OnCombatMultiplierChanged(float oldValue, float newValue) { _derivedStatsDirty = true; DerivedStatsChanged?.Invoke(); }
        public void SetCombatMultipliers(float all, float defense)
        {
            if (NetworkClient.active && !isServer) return;
            if (!float.IsFinite(all) || !float.IsFinite(defense) || all <= 0 || defense <= 0) return;
            if (Mathf.Approximately(all, _allCombatMultiplier) && Mathf.Approximately(defense, _defenseCombatMultiplier)) return;
            _allCombatMultiplier = all; _defenseCombatMultiplier = defense; OnCombatMultiplierChanged(0, 0);
        }
        private DerivedCombatStats _cachedDerivedStats;
        private StatBalanceConfig _cachedBalanceConfig;
        private int _cachedBalanceRevision = -1;
        private bool _derivedStatsDirty = true;
        private int _derivedCalculationCount;

        private IdentityCalculator _identityCalculator;
        private IdentityCalculator.IdentityDebug _lastDebug;

        /// <summary>
        /// Lazy-initialized calculator to prevent NullReferenceException if called before Awake.
        /// </summary>
        private IdentityCalculator Calculator => _identityCalculator ??= new IdentityCalculator();



        private void Awake()
        {
            // Optional: Ensure it's initialized on Awake if not already.
            _identityCalculator = Calculator;
        }



        /// <summary>
        /// 스탯(_stats) 기반 Identity를 다시 계산한다.
        /// </summary>
        public void RecalculateIdentity()
        {
            _derivedStatsDirty = true;
            var next = Calculator.ResolveIdentity(_stats, out var debug);
            _lastDebug = debug;

            // 불필요한 이벤트 방출 방지
            if (next.Type == CurrentIdentity.Type && next.PrimaryStat == CurrentIdentity.PrimaryStat)
                return;

            CurrentIdentity = next;
            _derivedStatsDirty = true;
            GetDerivedStats();
            IdentityChanged?.Invoke(CurrentIdentity);
        }

        /// <summary>
        /// identity 판정 디버그 값(최근 계산 결과)을 반환한다.
        /// </summary>
        public IdentityCalculator.IdentityDebug GetLastDebug() => _lastDebug;

        /// <summary>
        /// PureTotal(아이템 배제)을 스탯 종류별로 반환한다.
        /// </summary>
        public float GetPureTotal(StatKind kind) => StatMath.PureTotal(kind, _stats);

        /// <summary>
        /// FinalTotal(아이템 포함)을 스탯 종류별로 반환한다.
        /// </summary>
        public float GetFinalTotal(StatKind kind) => StatMath.FinalTotal(kind, _stats) * _allCombatMultiplier * (kind == StatKind.DEF ? _defenseCombatMultiplier : 1f);

        /// <summary>
        /// 현재 스탯 스냅샷을 값 복사로 반환한다.
        /// </summary>
        public StatContainer GetStatsCopy() => _stats;

        public BattlePvp.Characters.CharacterStatModifiers CharacterModifiers
        {
            get
            {
                var appearance = GetComponent<BattlePvp.Characters.PlayerAppearance>();
                var definition = appearance != null ? BattlePvp.Characters.CharacterCatalog.Instance?.Find(appearance.SelectedId) : null;
                return definition != null ? definition.CombatModifiers.Validated : BattlePvp.Characters.CharacterStatModifiers.Baseline;
            }
        }

        internal void RefreshCharacterStats()
        {
            var health = GetComponent<BattlePvp.Combat.HealthSystem>();
            bool initializedHealth = health != null && health.MaxHp > 0f && health.CurrentHp > 0f;
            float hpRatio = health != null && health.MaxHp > 0 ? health.CurrentHp / health.MaxHp : 1f;
            _derivedStatsDirty = true;
            DerivedStatsChanged?.Invoke();
            // Changing a lobby character preserves health percentage, including existing overflow.
            // SetCurrentHp itself enforces server/offline ownership and rejects dead players.
            if (initializedHealth)
            {
                health.RefreshFromStats(keepCurrentHpFlat: true);
                health.SetCurrentHp(health.MaxHp * hpRatio);
            }
        }

        public DerivedCombatStats GetDerivedStats()
        {
            StatBalanceConfig config = StatBalanceCalculator.Config;
            if (_derivedStatsDirty || _cachedBalanceConfig != config || _cachedBalanceRevision != config.Revision)
            {
                _cachedDerivedStats = CharacterModifiers.Apply(StatBalanceCalculator.Calculate(GetFinalTotal(StatKind.STR), GetFinalTotal(StatKind.CON), GetFinalTotal(StatKind.AGI), GetFinalTotal(StatKind.DEF), CurrentIdentity));
                _cachedBalanceConfig = config;
                _cachedBalanceRevision = config.Revision;
                _derivedStatsDirty = false;
                _derivedCalculationCount++;
            }
            return _cachedDerivedStats;
        }

        /// <summary>
        /// 슬라이더 시뮬레이션용 데이터로 파생 스탯(ATK, DEF, MaxHP, Pene, Regen, MoveSpeed, AttackSpeed)을 즉시 계산합니다.
        /// </summary>
        public void CalculatePreviewStats(StatContainer virtualStats, out float previewAtk, out float previewDef, out float previewMaxHp, out float previewPene, out float previewRegen, out float previewMoveSpd, out float previewAtkSpd)
        {
            Identity vId = Calculator.ResolveIdentity(virtualStats, out _);
            DerivedCombatStats derived = CharacterModifiers.Apply(StatBalanceCalculator.Calculate(virtualStats, vId));
            previewAtk = derived.AttackPower;
            previewDef = derived.DefenseEfficiencyPercent;
            previewMaxHp = derived.MaxHp;
            previewPene = derived.PenetrationPercent;
            previewRegen = derived.RegenPerSecond;
            previewMoveSpd = derived.MoveSpeed;
            previewAtkSpd = derived.AttackSpeed;
        }

        private BattlePvp.CameraLogic.FollowCamera _followCamera;
        private static readonly Vector3 DefaultCameraOffset = new Vector3(0.3f, 0.2f, -1.0f);
        // FollowCamera already scales eye height with the larger body; do not add another height bonus.
        private static readonly Vector3 MonostatCameraOffset = new Vector3(0.35f, DefaultCameraOffset.y, -1.0f);
        private bool _cameraInitialized = false;
        private bool OwnsFollowCamera => (netIdentity != null && isLocalPlayer) ||
            (!NetworkServer.active && !NetworkClient.active && _followCamera != null &&
             _followCamera.Target != null &&
             (_followCamera.Target == transform || _followCamera.Target.IsChildOf(transform)));

        private void OnEnable()
        {
            if (netIdentity != null && isLocalPlayer) SetLocal(this);
            StatBalanceConfig.BalanceChanged += OnBalanceChanged;
            _derivedStatsDirty = true;
            if (_autoRecalculateOnEnable)
                RecalculateIdentity();
            GetDerivedStats();

            if (netIdentity != null)
                return;

            // [추가] 글로벌 데이터 업데이트 구독 (더미/로컬 플레이어 모두 대응)
            _profileData = BattlePvp.Managers.GlobalDataManager.Instance;
            if (_profileData != null)
            {
                _profileData.OnSavedStatsUpdated += OnGlobalStatsUpdated;
                
                // 이미 데이터가 로드되어 있다면 즉시 주입
                var saved = _profileData.SavedStats;
                if (_profileData.HasLoadedPlayerStats)
                {
                    HandleInitialInjection(saved);
                }
            }
        }

        private void OnDisable()
        {
            FinishPendingApply(false, "플레이어가 비활성화되어 스탯 적용을 확인하지 못했습니다.");
            StatBalanceConfig.BalanceChanged -= OnBalanceChanged;
            if (Local == this) SetLocal(null);
            UnsubscribeProfile();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            SetLocal(this);
            
            Debug.Log("[StatManager] OnStartLocalPlayer: Initializing stats for Local Player.");
            BattlePvp.Networking.RoomConnectionDiagnostics.Record("local_player_stats_start");
            
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                var saved = BattlePvp.Managers.GlobalDataManager.Instance.SavedStats;
                HandleInitialInjection(saved);
            }
            
            InitializeCameraReference();
            ApplyVisualScaling();
        }

        public void BindAsLocalScenePlayer()
        {
            if ((!NetworkClient.active && !NetworkServer.active)) SetLocal(this);
        }

        private void OnGlobalStatsUpdated(StatContainer updatedStats)
        {
            if (SameSlot(_stats.STR, updatedStats.STR) && SameSlot(_stats.CON, updatedStats.CON) &&
                SameSlot(_stats.AGI, updatedStats.AGI) && SameSlot(_stats.DEF, updatedStats.DEF))
                return;
            Debug.Log($"[StatManager] Global stats updated asynchronously. STR={updatedStats.STR.Invested}");
            HandleInitialInjection(updatedStats);
        }

        private static bool SameSlot(StatSlot a, StatSlot b) => a.Invested == b.Invested && a.Item == b.Item;

        private void HandleInitialInjection(StatContainer saved)
        {
            // A reconnect restores the server's current build. Only explicit preset application may change it.
            if (NetworkClient.active && HasServerStats) return;
            // An empty allocation is valid in the waiting room; only explicit allocation enables match start.
            ApplyStats(saved, recalculateIdentity: true);
        }

        private void Start()
        {
            // NetworkIdentity가 없는 오브젝트(더미 등)를 위한 비네트워크 초기화
            if (TryGetComponent<Mirror.NetworkIdentity>(out var ni))
            {
                // 네트워크 개체인 경우 필요한 처리
            }
            else
            {
                Debug.Log($"[StatManager:{gameObject.name}] Non-network object detected. Initializing values locally.");
            }

            RecalculateIdentity();
            InitializeCameraReference();
            ApplyVisualScaling();
        }

        private void OnDestroy()
        {
            FinishPendingApply(false, "플레이어가 제거되어 스탯 적용을 확인하지 못했습니다.");
            if (Local == this) SetLocal(null);
            UnsubscribeProfile();
        }

        public override void OnStopLocalPlayer()
        {
            FinishPendingApply(false, "로컬 플레이어가 변경되어 스탯 적용을 확인하지 못했습니다.");
            if (Local == this) SetLocal(null);
            base.OnStopLocalPlayer();
        }

        private void UnsubscribeProfile()
        {
            if (_profileData != null) _profileData.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
            _profileData = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _initialStatsDeadline = NetworkTime.time + 10d;
            StartCoroutine(DisconnectUninitializedPlayer());
        }

        private System.Collections.IEnumerator DisconnectUninitializedPlayer()
        {
            yield return new WaitForSecondsRealtime(10f);
            if (isServer && !_serverStatsInitialized && connectionToClient != null)
            {
                BattlePvp.Networking.RoomConnectionDiagnostics.Record("server_stats_initialization_timeout");
                Debug.LogWarning("[StatManager] Disconnecting a player whose initial stats were not accepted within 10 seconds.");
                connectionToClient.Disconnect();
            }
        }

        private void OnStatsSynced(StatContainer oldStats, StatContainer newStats)
        {
            // 서버로부터 동기화된 스탯을 로컬에 적용 (UI/Visual 반영)
            InternalApplyStats(newStats, true);
        }

        [Command]
        public void CmdUpdateStats(StatContainer stats, uint requestId)
        {
            bool accepted = TryAcceptClientStats(stats);
            if (requestId != 0 && connectionToClient != null)
                TargetStatsRequestCompleted(connectionToClient, requestId, accepted, _stats);
        }

        private bool TryAcceptClientStats(StatContainer stats)
        {
            if ((!_serverStatsInitialized && NetworkTime.time > _initialStatsDeadline) ||
                NetworkTime.time < _nextStatRequestAt)
                return false;
            _nextStatRequestAt = NetworkTime.time + 0.2d;
            if (!StatValidation.TryValidateClientStats(stats, _stats, out StatContainer validated))
            {
                if (!_serverStatsInitialized) BattlePvp.Networking.RoomConnectionDiagnostics.Record("initial_stats_rejected");
                return false;
            }
            var health = GetComponent<BattlePvp.Combat.HealthSystem>();
            Identity next = Calculator.ResolveIdentity(validated, out _);
            bool battleScene = gameObject.scene.name == "Battle";
            if (!StatValidation.CanChangeClientPreset(_serverStatsInitialized, battleScene,
                    health != null && health.IsDead, next.Type))
                return false;

            _serverStatsInitialized = true;
            BattlePvp.Networking.RoomConnectionDiagnostics.Record("server_stats_accepted");
            InternalApplyStats(validated, true);
            return true;
        }

        public void RequestApplyStats(StatContainer stats, Action<bool, string> completed)
        {
            if (_pendingApplyRequestId != 0)
            {
                completed?.Invoke(false, "이전 스탯 적용 응답을 기다리는 중입니다.");
                return;
            }
            if (isLocalPlayer)
            {
                if (!isActiveAndEnabled)
                {
                    completed?.Invoke(false, "비활성화된 플레이어의 스탯은 적용할 수 없습니다.");
                    return;
                }
                unchecked { _nextApplyRequestId++; }
                if (_nextApplyRequestId == 0) _nextApplyRequestId++;
                _pendingApplyRequestId = _nextApplyRequestId;
                _pendingApplyCallback = completed;
                _pendingApplyDeadline = Time.realtimeSinceStartupAsDouble + ApplyRequestTimeoutSeconds;
                _applyTimeoutRoutine = StartCoroutine(WaitForApplyResponse());
                CmdUpdateStats(stats, _pendingApplyRequestId);
                return;
            }
            if ((!NetworkClient.active && !NetworkServer.active))
            {
                bool valid = StatValidation.IsValidPreset(stats);
                if (valid) InternalApplyStats(stats, true);
                completed?.Invoke(valid, valid ? null : "스탯 범위 또는 총 투자량을 확인하십시오.");
                return;
            }
            bool accepted = isServer && TryApplyServerPreset(stats);
            completed?.Invoke(accepted, accepted ? null : "현재 플레이어의 스탯을 적용할 수 없습니다.");
        }

        [TargetRpc]
        private void TargetStatsRequestCompleted(NetworkConnection target, uint requestId, bool accepted, StatContainer serverStats)
        {
            CompleteApplyResponse(requestId, accepted, serverStats);
        }

        private void CompleteApplyResponse(uint requestId, bool accepted, StatContainer serverStats)
        {
            ExpirePendingApplyRequest(Time.realtimeSinceStartupAsDouble);
            if (requestId == 0 || _pendingApplyRequestId != requestId) return;
            if (accepted && !isServer)
            {
                _serverStatsInitialized = true;
                InternalApplyStats(serverStats, true);
            }
            FinishPendingApply(accepted, accepted ? null : "서버가 스탯 변경을 거절했습니다. 변경 가능 상태와 투자량을 확인하십시오.");
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            FinishPendingApply(false, "연결이 종료되어 스탯 적용을 확인하지 못했습니다.");
        }

        private System.Collections.IEnumerator WaitForApplyResponse()
        {
            yield return new WaitForSecondsRealtime((float)ApplyRequestTimeoutSeconds);
            _applyTimeoutRoutine = null;
            ExpirePendingApplyRequest(Time.realtimeSinceStartupAsDouble);
        }

        private void ExpirePendingApplyRequest(double now)
        {
            if (_pendingApplyRequestId != 0 && (double.IsNaN(now) || double.IsInfinity(now) || now >= _pendingApplyDeadline))
                FinishPendingApply(false, "스탯 적용 확인 시간이 초과되었습니다. 현재 스탯을 확인하고 다시 시도하십시오.");
        }

        private void FinishPendingApply(bool accepted, string error)
        {
            if (_applyTimeoutRoutine != null) StopCoroutine(_applyTimeoutRoutine);
            _applyTimeoutRoutine = null;
            Action<bool, string> callback = _pendingApplyCallback;
            _pendingApplyCallback = null;
            _pendingApplyRequestId = 0;
            _pendingApplyDeadline = 0d;
            callback?.Invoke(accepted, error);
        }

        [Server]
        public bool TryApplyServerPreset(StatContainer stats)
        {
            if (!StatValidation.TryValidateClientStats(stats, _stats, out StatContainer validated))
                return false;
            _serverStatsInitialized = true;
            InternalApplyStats(validated, true);
            return true;
        }

        private void InitializeCameraReference()
        {
            if ((netIdentity == null || !isLocalPlayer) && (NetworkServer.active || NetworkClient.active))
                return;
            if (_followCamera == null)
                _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
            if (OwnsFollowCamera && _followCamera != null && !_cameraInitialized)
            {
                _followCamera.Offset = DefaultCameraOffset;
                _cameraInitialized = true;
            }
        }

        private void ApplyVisualScaling()
        {
            InitializeCameraReference();

            // 조건: STR 또는 CON 몰빵(Monostat) 상태일 때만 1.2배 (Task 3 수정)
            // 전에는 AGI/DEF가 0이기만 하면 커졌으나, 이제는 확실히 한 스탯에 몰빵된 경우만 체크.
            // 0. 네트워크 컴포넌트 안전망 (netIdentity가 없으면 로컬 전력이 아님)
            bool isGiant = (CurrentIdentity.Type == IdentityType.Monostat) &&
                           (CurrentIdentity.PrimaryStat == StatKind.STR || CurrentIdentity.PrimaryStat == StatKind.CON);
            
            float targetScale = isGiant ? 1.2f : 1.0f;
            transform.localScale = new Vector3(targetScale, targetScale, targetScale);

            // 카메라 오프셋 비례 조정 (Task 5)
            if (OwnsFollowCamera && _cameraInitialized && _followCamera != null)
            {
                _followCamera.Offset = isGiant ? MonostatCameraOffset : DefaultCameraOffset;
                Debug.Log($"[StatManager] Scale applied: {targetScale}, Camera Offset: {_followCamera.Offset}");
            }
        }

        /// <summary>
        /// 현재 스탯을 교체 적용한다. (네트워크 동기화 포함)
        /// </summary>
        public void ApplyStats(StatContainer stats, bool recalculateIdentity = true)
        {
            if (netIdentity != null && !isLocalPlayer && !isServer)
                return;
            // 로컬 플레이어라면 서버에 동기화 요청
            // 로컬 플레이어라면 서버에 동기화 요청 (netIdentity 존재 시에만)
            if (netIdentity != null && isLocalPlayer)
            {
                Debug.Log($"[StatManager:{gameObject.name}] localPlayer requesting CmdUpdateStats to Server.");
                CmdUpdateStats(stats, 0);
                return; // Only a server-accepted snapshot changes the live player.
            }

            if (netIdentity != null && isServer)
            {
                TryApplyServerPreset(stats);
                return;
            }
            
            // 즉각적인 피드백을 위해 로컬에서 먼저 적용
            InternalApplyStats(stats, recalculateIdentity);
        }

        /// <summary>
        /// 씬에 배치된 로비용 플레이어처럼 네트워크 권한이 아직 없는 표시 대상에 저장 스텟을 반영합니다.
        /// 서버/로컬 플레이어 동기화 용도가 아니라 로컬 씬 초기화 전용입니다.
        /// </summary>
        public void ApplyLocalSceneStats(StatContainer stats, bool recalculateIdentity = true)
        {
            InternalApplyStats(stats, recalculateIdentity);
        }

        private void InternalApplyStats(StatContainer stats, bool recalculateIdentity)
        {
            _stats = stats;
            _derivedStatsDirty = true;

            if (recalculateIdentity)
                RecalculateIdentity();

            GetDerivedStats();
            ApplyVisualScaling();
            StatsChanged?.Invoke(_stats);
        }

        /// <summary>
        /// 투자값만 교체 적용한다. (아이템 보너스는 유지)
        /// </summary>
        public void ApplyInvestedOnly(StatContainer investedOnly, bool recalculateIdentity = true)
        {
            var next = _stats;
            next.STR.Invested = investedOnly.STR.Invested;
            next.CON.Invested = investedOnly.CON.Invested;
            next.AGI.Invested = investedOnly.AGI.Invested;
            next.DEF.Invested = investedOnly.DEF.Invested;
            ApplyStats(next, recalculateIdentity);
        }

        private void OnBalanceChanged()
        {
#if UNITY_EDITOR
            // A different player's Update can publish an asset validation before
            // this component has received its NetworkIdentity binding.
            if (!CanPublishValidatedStats)
            {
                System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 1);
                return;
            }
            if (ApplyPendingEditorValidation()) return;
#endif
            GetDerivedStats();
            DerivedStatsChanged?.Invoke();
        }

#if UNITY_EDITOR
        private int _pendingEditorValidation;
        private bool CanPublishValidatedStats => netIdentity != null ||
            (!NetworkServer.active && !NetworkClient.active &&
             GetComponentInParent<NetworkIdentity>(true) == null);

        protected override void OnValidate()
        {
            base.OnValidate();
            _derivedStatsDirty = true;
            System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 1);
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || !CanPublishValidatedStats)
                return;

            StatBalanceConfig.PublishPendingEditorValidation();
            ApplyPendingEditorValidation();
        }

        private bool ApplyPendingEditorValidation()
        {
            if (System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 0) == 0)
                return false;

            RecalculateIdentity();
            GetDerivedStats();
            DerivedStatsChanged?.Invoke();
            return true;
        }
#endif
    }
}

