using System;
using BattlePvp.Combat;
using BattlePvp.Stats;
using UnityEngine;

/// <summary>
/// 공격(AttackData)과 스탯(StatManager)을 결합해, 실제 피해를 계산/적용하는 전투 컴포넌트 초안입니다.
/// - GC 최소화: hot path에서는 new/할당을 지양하고, DamageCalculator 인스턴스를 캐시합니다.
/// - reference-formulae.md의 핵심은 DamageCalculator를 통해 동일하게 처리합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AttackProcessor : MonoBehaviour
{
    [Header("Self (Attacker)")]
    [SerializeField] private StatManager _attackerStats;

    [Tooltip("IDamageReceiver를 구현한 컴포넌트 (Unity에서는 인터페이스를 바로 드래그할 수 없어 MonoBehaviour로 받습니다).")]
    [SerializeField] private MonoBehaviour _attackerHealth;

    private DamageCalculator _damageCalculator;

    private IDamageReceiver _attackerDamageReceiver;

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private float _currentAtk;
    [SerializeField] private float _currentPene;
    [SerializeField] private float _lastHitPower;
    [SerializeField] private float _lastHitPene;

    private void Awake()
    {
        _damageCalculator = _damageCalculator ?? new DamageCalculator();

        if (_attackerHealth != null)
            _attackerDamageReceiver = _attackerHealth as IDamageReceiver;

        if (_attackerStats == null)
            _attackerStats = GetComponent<StatManager>();

        if (_attackerDamageReceiver == null)
        {
            // TODO: 런타임에만 필요한 경우가 많으므로, 여기서는 조용히 no-op에 가깝게 동작하도록 둡니다.
        }

        RefreshFromStats();
    }

    private void OnEnable()
    {
        if (_attackerStats != null)
        {
            _attackerStats.StatsChanged += OnStatsChanged;
            RefreshFromStats();
        }
    }

    private void OnDisable()
    {
        if (_attackerStats != null)
            _attackerStats.StatsChanged -= OnStatsChanged;
    }

    private void OnStatsChanged(StatContainer _) => RefreshFromStats();

    public void RefreshFromStats()
    {
        if (_attackerStats == null) return;

        float str = _attackerStats.GetFinalTotal(StatKind.STR);
        _currentAtk = str * 4f;
        _currentPene = str * 0.3f;

        Identity id = _attackerStats.CurrentIdentity;
        if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.STR)
        {
            _currentAtk *= 1.4f;
            _currentPene += 18f;
        }
    }

    /// <summary>
    /// 공격 1타(히트 1회)에 대한 피해를 계산해 적용합니다.
    /// </summary>
    /// <param name="attackData">공격 프리셋</param>
    /// <param name="defenderStats">피격자 StatManager</param>
    /// <param name="defender">피격자 HP 수신자</param>
    /// <param name="defenderGuard">선택적 가드 컴포넌트 (없으면 null)</param>
    public void ProcessHit(AttackData attackData, StatManager defenderStats, IDamageReceiver defender, IGuard defenderGuard = null)
    {
        if (attackData == null)
            return;
        if (_attackerStats == null || defenderStats == null)
            return;
        if (defender == null)
            return;

        Identity attackerIdentity = _attackerStats.CurrentIdentity;
        Identity defenderIdentity = defenderStats.CurrentIdentity;

        // 1) ATK / Piercing 구성 (기획안: 1 STR당 ATK 4, 물관 0.3%)
        float attackerStrFinal = _attackerStats.GetFinalTotal(StatKind.STR);
        float baseAtk = attackerStrFinal * 4f;
        float basePene = attackerStrFinal * 0.3f;

        // AttackData.damage는 실제 공격의 세기를 곱해주는 계수
        float attackPower = baseAtk * attackData.damage;
        float penetrationPercent = basePene;

        if (attackerIdentity.Type == IdentityType.Monostat && attackerIdentity.PrimaryStat == StatKind.STR)
        {
            // Monostat STR: 공격력 +40%, 물관 18%
            attackPower *= 1.4f;
            penetrationPercent += 18f;

            // Monostat STR: 가드 파괴 (선택적 훅)
            if (defenderGuard != null && defenderGuard.IsGuarding)
                defenderGuard.BreakGuard();
        }

        penetrationPercent = Clamp(penetrationPercent, 0f, 100f);

        // 2) DEF_Eff 구성 (CurrentDEF + BonusEff 승산 중첩 + 0.75 hardcap)
        float defenderDefFinal = defenderStats.GetFinalTotal(StatKind.DEF);
        float defenderCurrentDefNormalized = defenderDefFinal / 100f; // editor sim과 동일한 스케일링 가정

        float bonusEffNormalized = 0f;
        if (defenderIdentity.Type == IdentityType.Monostat && defenderIdentity.PrimaryStat == StatKind.DEF)
        {
            // Monostat DEF: 방어 효율 +50%
            bonusEffNormalized = 0.5f;
        }

        // 3) 최종 피해 계산 (reference-formulae.md의 Prediction은 DamageCalculator에 위임)
        float finalDamage = _damageCalculator.PredictFinalDamage(
            attackPower,
            defenderCurrentDefNormalized,
            bonusEffNormalized,
            penetrationPercent);

        // 4) Monostat CON: 최종 데미지 -30%
        if (defenderIdentity.Type == IdentityType.Monostat && defenderIdentity.PrimaryStat == StatKind.CON)
            finalDamage *= 0.7f;

        if (finalDamage <= 0f)
            return;

        _lastHitPower = attackPower;
        _lastHitPene = penetrationPercent;

        // 5) 물리 피해 적용 (+ 컨텍스트 전달 가능하면 전달)
        // Thorns 반사는 HealthSystem이 "Physical 피해 수신 시" 처리한다.
        if (defender is IDamageReceiverWithContext ctx)
            ctx.ApplyDamage(finalDamage, DamageSource.Physical, attackPower, _attackerDamageReceiver);
        else
            defender.ApplyDamage(finalDamage, DamageSource.Physical);
    }

    private static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}

