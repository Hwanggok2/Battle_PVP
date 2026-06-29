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

        [Header("Stat Data")]
        [SyncVar(hook = nameof(OnStatsSynced))]
        [SerializeField] private StatContainer _stats;

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
            var next = Calculator.ResolveIdentity(_stats, out var debug);

            // 불필요한 이벤트 방출 방지
            if (next.Type == CurrentIdentity.Type && next.PrimaryStat == CurrentIdentity.PrimaryStat)
                return;

            CurrentIdentity = next;
            _lastDebug = debug;
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
        public float GetFinalTotal(StatKind kind) => StatMath.FinalTotal(kind, _stats);

        /// <summary>
        /// 현재 스탯 스냅샷을 값 복사로 반환한다.
        /// </summary>
        public StatContainer GetStatsCopy() => _stats;

        /// <summary>
        /// 슬라이더 시뮬레이션용 데이터로 파생 스탯(ATK, DEF, MaxHP, Pene, Regen, MoveSpeed, AttackSpeed)을 즉시 계산합니다.
        /// </summary>
        public void CalculatePreviewStats(StatContainer virtualStats, out float previewAtk, out float previewDef, out float previewMaxHp, out float previewPene, out float previewRegen, out float previewMoveSpd, out float previewAtkSpd)
        {
            float vStr = StatMath.FinalTotal(virtualStats.STR);
            float vCon = StatMath.FinalTotal(virtualStats.CON);
            float vAgi = StatMath.FinalTotal(virtualStats.AGI);
            float vDef = StatMath.FinalTotal(virtualStats.DEF);

            // 미리보기용 Identity
            Identity vId = Calculator.ResolveIdentity(virtualStats, out _);

            // 1) ATK & Pene
            float baseAtk = vStr * 4f;
            float basePene = vStr * 0.3f;
            if (vId.Type == IdentityType.Monostat && vId.PrimaryStat == StatKind.STR)
            {
                baseAtk *= 1.4f;
                basePene += 18f;
            }
            previewAtk = baseAtk;
            previewPene = Mathf.Clamp(basePene, 0f, 100f);

            // 2) MaxHP & Regen (BaseMaxHp 100, MaxHpPerCon 15, RegenPerCon 0.15)
            float baseMaxHp = 100f + (vCon * 15f) + (vStr * 5f);
            float baseRegen = vCon * 0.15f;
            if (vId.Type == IdentityType.Monostat)
            {
                if (vId.PrimaryStat == StatKind.CON)
                {
                    baseMaxHp *= 1.6f;
                    baseRegen += 5f;
                }
                else if (vId.PrimaryStat == StatKind.AGI)
                {
                    baseMaxHp *= 0.7f;
                }
            }
            previewMaxHp = baseMaxHp;
            previewRegen = baseRegen;

            // 3) DEF 효율
            float currentDefNormalized = vDef / 100f;
            float bonusEff = (vId.Type == IdentityType.Monostat && vId.PrimaryStat == StatKind.DEF) ? 0.5f : 0f;
            float cur = Mathf.Clamp01(currentDefNormalized);
            float bonus = Mathf.Clamp01(bonusEff);
            float finalEff = 1f - (1f - cur) * (1f - bonus);
            finalEff = Mathf.Min(finalEff, 0.75f);
            previewDef = Mathf.Max(0f, finalEff) * 100f; // 백분율 표기

            // 4) MoveSpeed & AttackSpeed
            float baseMs = 3.0f + (vAgi * 0.04f);
            float baseAs = 0.6f + (vAgi * 0.02f);
            if (vId.Type == IdentityType.Monostat)
            {
                if (vId.PrimaryStat == StatKind.AGI)
                {
                    baseMs *= 1.2f;
                    baseAs *= 3f;
                }
                else if (vId.PrimaryStat == StatKind.STR)
                {
                    baseMs *= 0.75f;
                    baseAs *= 0.75f;
                }
                else if (vId.PrimaryStat == StatKind.DEF)
                {
                    baseMs *= 0.7f;
                }
            }
            previewMoveSpd = baseMs;
            previewAtkSpd = baseAs;
        }

        private BattlePvp.CameraLogic.FollowCamera _followCamera;
        private Vector3 _originalCameraOffset;
        private bool _cameraInitialized = false;

        private void OnEnable()
        {
            if (_autoRecalculateOnEnable)
                RecalculateIdentity();

            // [추가] 글로벌 데이터 업데이트 구독 (더미/로컬 플레이어 모두 대응)
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated += OnGlobalStatsUpdated;
                
                // 이미 데이터가 로드되어 있다면 즉시 주입
                var saved = BattlePvp.Managers.GlobalDataManager.Instance.SavedStats;
                float total = saved.STR.Invested + saved.AGI.Invested + saved.CON.Invested + saved.DEF.Invested;
                if (total > 0.1f)
                {
                    HandleInitialInjection(saved);
                }
            }
        }

        private void OnDisable()
        {
            if (Local == this) Local = null;

            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Local = this;
            
            Debug.Log("[StatManager] OnStartLocalPlayer: Initializing stats for Local Player.");
            
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                var saved = BattlePvp.Managers.GlobalDataManager.Instance.SavedStats;
                HandleInitialInjection(saved);
            }
            
            InitializeCameraReference();
            ApplyVisualScaling();
        }

        private void OnGlobalStatsUpdated(StatContainer updatedStats)
        {
            Debug.Log($"[StatManager] Global stats updated asynchronously. STR={updatedStats.STR.Invested}");
            HandleInitialInjection(updatedStats);
        }

        private void HandleInitialInjection(StatContainer saved)
        {
            // 스탯 합계가 0이면(신규 유저 등) 기본값 10/10/10/10 부여
            float total = saved.STR.Invested + saved.AGI.Invested + saved.CON.Invested + saved.DEF.Invested;
            
            string source = total <= 0.1f ? "Fallback (Default)" : "Saved Data";
            
            if (total <= 0.1f)
            {
                saved.STR.Invested = 10;
                saved.AGI.Invested = 10;
                saved.CON.Invested = 10;
                saved.DEF.Invested = 10;
            }

            Debug.Log($"[StatManager:{gameObject.name}] Injecting {source}: STR={saved.STR.Invested}, AGI={saved.AGI.Invested}, CON={saved.CON.Invested}, DEF={saved.DEF.Invested}");
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
            if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                BattlePvp.Managers.GlobalDataManager.Instance.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
            }
        }

        private void OnStatsSynced(StatContainer oldStats, StatContainer newStats)
        {
            // 서버로부터 동기화된 스탯을 로컬에 적용 (UI/Visual 반영)
            InternalApplyStats(newStats, true);
        }

        [Command]
        public void CmdUpdateStats(StatContainer stats)
        {
            _stats = stats;
            // 서버에서도 Identity 재계산 및 StatsChanged 이벤트 발생
            // → HealthSystem.OnStatsChanged가 서버에서 실행되어 HP SyncVar가 정확한 값으로 동기화됩니다.
            RecalculateIdentity();
            StatsChanged?.Invoke(_stats);
        }

        private void InitializeCameraReference()
        {
            if (_followCamera == null)
            {
                _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
                if (_followCamera != null && !_cameraInitialized)
                {
                    _originalCameraOffset = _followCamera.Offset;
                    _cameraInitialized = true;
                }
            }
        }

        private void ApplyVisualScaling()
        {
            InitializeCameraReference();

            // 조건: STR 또는 CON 몰빵(Monostat) 상태일 때만 1.2배 (Task 3 수정)
            // 전에는 AGI/DEF가 0이기만 하면 커졌으나, 이제는 확실히 한 스탯에 몰빵된 경우만 체크.
            // 0. 네트워크 컴포넌트 안전망 (netIdentity가 없으면 로컬 전력이 아님)
            bool giantLocal = (netIdentity != null && isLocalPlayer);
            bool isGiant = (CurrentIdentity.Type == IdentityType.Monostat) && 
                           (CurrentIdentity.PrimaryStat == StatKind.STR || CurrentIdentity.PrimaryStat == StatKind.CON);
            
            float targetScale = isGiant ? 1.2f : 1.0f;
            transform.localScale = new Vector3(targetScale, targetScale, targetScale);

            // 카메라 오프셋 비례 조정 (Task 5)
            if (_cameraInitialized && _followCamera != null)
            {
                _followCamera.Offset = _originalCameraOffset * targetScale;
                Debug.Log($"[StatManager] Scale applied: {targetScale}, Camera Offset: {_followCamera.Offset}");
            }
        }

        /// <summary>
        /// 현재 스탯을 교체 적용한다. (네트워크 동기화 포함)
        /// </summary>
        public void ApplyStats(StatContainer stats, bool recalculateIdentity = true)
        {
            // 로컬 플레이어라면 서버에 동기화 요청
            // 로컬 플레이어라면 서버에 동기화 요청 (netIdentity 존재 시에만)
            if (netIdentity != null && isLocalPlayer)
            {
                Debug.Log($"[StatManager:{gameObject.name}] localPlayer requesting CmdUpdateStats to Server.");
                CmdUpdateStats(stats);
            }
            
            // 즉각적인 피드백을 위해 로컬에서 먼저 적용
            InternalApplyStats(stats, recalculateIdentity);
        }

        private void InternalApplyStats(StatContainer stats, bool recalculateIdentity)
        {
            _stats = stats;

            if (recalculateIdentity)
                RecalculateIdentity();

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
    }
}

