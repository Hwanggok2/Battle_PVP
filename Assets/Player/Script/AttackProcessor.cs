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

    [Header("Debug")]
    [SerializeField] private bool _logHits;

    private DamageCalculator _damageCalculator;

    private IDamageReceiver _attackerDamageReceiver;
    private PlayerCombat _playerCombat;

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

        _playerCombat = GetComponent<PlayerCombat>();

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

    private void OnStatsChanged(StatContainer _)
    {
        if (this == null) return;
        RefreshFromStats();
    }

    public void RefreshFromStats()
    {
        if (_attackerStats == null) return;

        DerivedCombatStats derived = _attackerStats.GetDerivedStats();
        _currentAtk = derived.AttackPower;
        _currentPene = derived.PenetrationPercent;
    }

    /// <summary>
    /// 공격 1타(히트 1회)에 대한 피해를 계산해 적용합니다.
    /// </summary>
    /// <param name="attackData">공격 프리셋</param>
    /// <param name="defenderStats">피격자 StatManager</param>
    /// <param name="defender">피격자 HP 수신자</param>
    /// <param name="defenderGuard">선택적 가드 컴포넌트 (없으면 null)</param>
    public void ProcessHit(AttackData attackData, StatManager defenderStats, IDamageReceiver defender, Vector3 hitPosition, IGuard defenderGuard = null, float bodyPartMultiplier = 1f, BodyPart bodyPart = BodyPart.Body)
    {
        if (attackData == null)
            return;
        if (_attackerStats == null || defenderStats == null)
            return;
        if (defender == null || (defender is MonoBehaviour mb && mb == null))
            return;
        if (_attackerDamageReceiver is HealthSystem attackerHealth && attackerHealth.IsDead)
            return;

        Identity attackerIdentity = _attackerStats.CurrentIdentity;

        // 1) ATK / Piercing 구성 (기획안: 1 STR당 ATK 3, 물관 0.3%)
        DerivedCombatStats attackerDerived = _attackerStats.GetDerivedStats();

        // AttackData.damage는 실제 공격의 세기를 곱해주는 계수
        float attackPower = attackerDerived.AttackPower * attackData.damage;
        float penetrationPercent = attackerDerived.PenetrationPercent;
        if (_playerCombat != null)
            attackPower *= _playerCombat.AttackPowerBonusMultiplier;

        if (attackerIdentity.Type == IdentityType.Monostat && attackerIdentity.PrimaryStat == StatKind.STR)
        {
            // Monostat STR: 가드 파괴 (선택적 훅)
            if (defenderGuard != null && defenderGuard.IsGuarding)
                defenderGuard.BreakGuard();
        }

        penetrationPercent = Clamp(penetrationPercent, 0f, 100f);

        // 2) DEF_Eff 구성 (CurrentDEF + BonusEff 승산 중첩 + 0.75 hardcap)
        float defenderDefFinal = defenderStats.GetFinalTotal(StatKind.DEF);
        float defenderCurrentDefNormalized = defenderDefFinal / 100f; // editor sim과 동일한 스케일링 가정

        DerivedCombatStats defenderDerived = defenderStats.GetDerivedStats();
        float bonusEffNormalized = defenderDerived.DefenseBonusNormalized;

        // 3) 최종 피해 계산 (reference-formulae.md의 Prediction은 DamageCalculator에 위임)
        float finalDamage = _damageCalculator.PredictFinalDamage(
            attackPower,
            defenderCurrentDefNormalized,
            bonusEffNormalized,
            penetrationPercent);

        finalDamage *= defenderDerived.IncomingDamageMultiplier;

        finalDamage *= Mathf.Max(0f, bodyPartMultiplier);

        // [디버그] 최종 계산 데미지 로그
        if (_logHits)
        {
            Debug.Log($"[AttackProcessor] Hit! Power:{attackPower:F1}, Pene:{penetrationPercent:F1}%, DefEff:{defenderCurrentDefNormalized:F2}, BodyPartMul:{bodyPartMultiplier:F2}, FinalDamage:{finalDamage:F1}");
        }

        if (finalDamage <= 0f)
            return;

        _lastHitPower = attackPower;
        _lastHitPene = penetrationPercent;

        // 5) 물리 피해 적용 (+ 컨텍스트 전달 가능하면 전달)
        // Thorns 반사는 HealthSystem이 "Physical 피해 수신 시" 처리한다.
        float defenderHpBefore = defender.CurrentHp;
        if (_playerCombat == null)
            _playerCombat = GetComponent<PlayerCombat>();

        if (defender is IDamageReceiverWithContext ctx)
        {
            // defender가 여전히 유효한지 확인
            if (ctx is MonoBehaviour defenderMb && defenderMb == null) return;
            ctx.ApplyDamage(finalDamage, DamageSource.Physical, attackPower, _attackerDamageReceiver, hitPosition);
        }
        else
        {
            defender.ApplyDamage(finalDamage, DamageSource.Physical, hitPosition);
        }

        float actualDamage = Mathf.Max(0f, defenderHpBefore - defender.CurrentHp);
        _playerCombat?.NotifyConfirmedHit(bodyPart == BodyPart.Head);
        _playerCombat?.NotifyPhysicalDamageDealt(actualDamage, defender, hitPosition);
    }

    public float PredictHitDamage(AttackData attackData, StatManager defenderStats, float bodyPartMultiplier = 1f)
    {
        if (attackData == null || _attackerStats == null || defenderStats == null)
            return 0f;

        DerivedCombatStats attackerDerived = _attackerStats.GetDerivedStats();
        float attackPower = attackerDerived.AttackPower * attackData.damage;
        float penetrationPercent = attackerDerived.PenetrationPercent;
        if (_playerCombat != null)
            attackPower *= _playerCombat.AttackPowerBonusMultiplier;

        float defenderDef = defenderStats.GetFinalTotal(StatKind.DEF) / 100f;
        DerivedCombatStats defenderDerived = defenderStats.GetDerivedStats();
        float bonusDefense = defenderDerived.DefenseBonusNormalized;
        float finalDamage = _damageCalculator.PredictFinalDamage(
            attackPower,
            defenderDef,
            bonusDefense,
            Clamp(penetrationPercent, 0f, 100f));

        finalDamage *= defenderDerived.IncomingDamageMultiplier;

        return Mathf.Max(0f, finalDamage * Mathf.Max(0f, bodyPartMultiplier));
    }

    public bool ProcessSkillHit(float damageMultiplier, StatManager defenderStats, IDamageReceiver defender, Vector3 hitPosition, float bodyPartMultiplier = 1f, BodyPart bodyPart = BodyPart.Body)
    {
        if (damageMultiplier <= 0f || defenderStats == null || defender == null)
            return false;
        if (_attackerDamageReceiver is HealthSystem attackerHealth && attackerHealth.IsDead)
            return false;
        if (_playerCombat == null)
            _playerCombat = GetComponent<PlayerCombat>();

        float attackPower = _currentAtk * damageMultiplier;
        if (_playerCombat != null)
            attackPower *= _playerCombat.AttackPowerBonusMultiplier;
        float defenderDef = defenderStats.GetFinalTotal(StatKind.DEF) / 100f;
        DerivedCombatStats defenderDerived = defenderStats.GetDerivedStats();
        float bonusDefense = defenderDerived.DefenseBonusNormalized;
        float finalDamage = _damageCalculator.PredictFinalDamage(attackPower, defenderDef, bonusDefense, Mathf.Clamp(_currentPene, 0f, 100f));
        finalDamage *= defenderDerived.IncomingDamageMultiplier;
        finalDamage *= Mathf.Max(0f, bodyPartMultiplier);
        if (finalDamage <= 0f)
            return false;

        float hpBefore = defender.CurrentHp;
        if (defender is IDamageReceiverWithContext context)
        {
            context.ApplyDamage(finalDamage, DamageSource.Physical, attackPower, _attackerDamageReceiver, hitPosition);
        }
        else
        {
            defender.ApplyDamage(finalDamage, DamageSource.Physical, hitPosition);
        }

        float actualDamage = Mathf.Max(0f, hpBefore - defender.CurrentHp);
        _playerCombat?.NotifyConfirmedHit(bodyPart == BodyPart.Head);
        _playerCombat?.NotifyPhysicalDamageDealt(actualDamage, defender, hitPosition);
        return true;
    }

    public float PredictSkillHitDamage(float damageMultiplier, StatManager defenderStats, float bodyPartMultiplier = 1f)
    {
        if (damageMultiplier <= 0f || defenderStats == null)
            return 0f;

        float attackPower = _currentAtk * damageMultiplier;
        if (_playerCombat != null)
            attackPower *= _playerCombat.AttackPowerBonusMultiplier;

        float defenderDef = defenderStats.GetFinalTotal(StatKind.DEF) / 100f;
        DerivedCombatStats defenderDerived = defenderStats.GetDerivedStats();
        float bonusDefense = defenderDerived.DefenseBonusNormalized;
        float finalDamage = _damageCalculator.PredictFinalDamage(
            attackPower,
            defenderDef,
            bonusDefense,
            Mathf.Clamp(_currentPene, 0f, 100f));

        finalDamage *= defenderDerived.IncomingDamageMultiplier;

        return Mathf.Max(0f, finalDamage * Mathf.Max(0f, bodyPartMultiplier));
    }

    private static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

}

