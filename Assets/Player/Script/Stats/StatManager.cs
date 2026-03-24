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

        private void Awake()
        {
            _identityCalculator = new IdentityCalculator();
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
            var next = _identityCalculator.ResolveIdentity(_stats, out var debug);

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
        /// 현재 스탯을 교체 적용한다.
        /// </summary>
        public void ApplyStats(StatContainer stats, bool recalculateIdentity = true)
        {
            _stats = stats;

            if (recalculateIdentity)
                RecalculateIdentity();

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

