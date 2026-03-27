using System;
using UnityEngine;

namespace BattlePvp.Stats
{
    /// <summary>
    /// 스탯을 기준으로 Identity를 판정하고 상태 변경 이벤트를 방출하는 MonoBehaviour 골격.
    /// </summary>
    public sealed class StatManager : MonoBehaviour, IIdentitySource
    {
        [Header("Stat Data")]
        [SerializeField] private StatContainer _stats;

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

        private void OnEnable()
        {
            if (_autoRecalculateOnEnable)
                RecalculateIdentity();
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

        private BattlePvp.CameraLogic.FollowCamera _followCamera;
        private Vector3 _originalCameraOffset;
        private bool _cameraInitialized = false;

        private void Start()
        {
            InitializeCameraReference();
            // 씬 진입 시 초기 스케일/카메라 적용
            ApplyVisualScaling();
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
        /// 현재 스탯을 교체 적용한다.
        /// </summary>
        public void ApplyStats(StatContainer stats, bool recalculateIdentity = true)
        {
            _stats = stats;

            if (recalculateIdentity)
                RecalculateIdentity();

            // 스케일 및 카메라 즉시 반영 (Task 3, 5)
            ApplyVisualScaling();

            // Identity가 먼저 결정된 후 다른 시스템들이 스탯 변화를 인지해야 
            // 새로운 Identity 보너스가 정확히 반영됩니다.
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

