using System;
using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    /// <summary>A proposed preset swap. Commit its return state only after the stats owner accepts Target.</summary>
    public readonly struct CombatPresetPlan
    {
        public readonly StatContainer Target;
        public readonly StatContainer ReturnPreset;
        public readonly bool HasReturnPreset;

        private CombatPresetPlan(StatContainer target, StatContainer returnPreset, bool hasReturnPreset)
        { Target = target; ReturnPreset = returnPreset; HasReturnPreset = hasReturnPreset; }

        public static bool TryCreate(bool strategist, StatContainer current, StatContainer requested,
            StatContainer previousReturn, bool hasPreviousReturn, out CombatPresetPlan plan)
        {
            plan = default;
            if (!strategist)
            {
                plan = new CombatPresetPlan(requested, previousReturn, hasPreviousReturn);
                return true;
            }
            float total = requested.STR.Invested + requested.AGI.Invested + requested.CON.Invested + requested.DEF.Invested;
            if (Math.Round(total) != 30d) return false;
            plan = hasPreviousReturn && SameInvestment(current, requested)
                ? new CombatPresetPlan(previousReturn, previousReturn, false)
                : new CombatPresetPlan(requested, current, true);
            return true;
        }

        private static bool SameInvestment(StatContainer a, StatContainer b) =>
            Math.Round(a.STR.Invested) == Math.Round(b.STR.Invested) &&
            Math.Round(a.AGI.Invested) == Math.Round(b.AGI.Invested) &&
            Math.Round(a.CON.Invested) == Math.Round(b.CON.Invested) &&
            Math.Round(a.DEF.Invested) == Math.Round(b.DEF.Invested);

        // Deliberately includes items for the existing preset bonus policy; identity eligibility uses pure investment elsewhere.
        public static StatKind DominantStat(StatContainer stats)
        {
            StatKind dominant = StatKind.STR;
            float best = stats.STR.Invested + stats.STR.Item;
            float agi = stats.AGI.Invested + stats.AGI.Item;
            float con = stats.CON.Invested + stats.CON.Item;
            float def = stats.DEF.Invested + stats.DEF.Item;
            if (agi > best) { dominant = StatKind.AGI; best = agi; }
            if (con > best) { dominant = StatKind.CON; best = con; }
            if (def > best) dominant = StatKind.DEF;
            return dominant;
        }

        public static void ResolveVitals(float oldMax, float oldCurrent, float newMax, float increaseShieldRatio,
            float bonusShield, out float currentHp, out float shield)
        {
            currentHp = Math.Min(oldCurrent, newMax);
            shield = Math.Max(0f, oldCurrent - currentHp) + Math.Max(0f, newMax - oldMax) * increaseShieldRatio + bonusShield;
        }
    }

    public struct CombatPresetBonusSettings
    {
        public float StrAttackMultiplier, StrDuration;
        public float AgiMoveMultiplier, AgiAttackSpeedMultiplier, AgiDuration;
        public float ConShieldRatio, DefInvulnerableSeconds;
    }

    /// <summary>The four existing strategist bonuses; contains no health, presentation or network references.</summary>
    public readonly struct CombatPresetBonus
    {
        public readonly StatKind Stat;
        public readonly float AttackMultiplier, MoveMultiplier, AttackSpeedMultiplier, Duration, Shield;

        private CombatPresetBonus(StatKind stat, float attack, float move, float attackSpeed, float duration, float shield)
        { Stat = stat; AttackMultiplier = attack; MoveMultiplier = move; AttackSpeedMultiplier = attackSpeed; Duration = duration; Shield = shield; }

        public static CombatPresetBonus Resolve(StatKind stat, float targetMaxHp, CombatPresetBonusSettings settings) => stat switch
        {
            StatKind.STR => new CombatPresetBonus(stat, settings.StrAttackMultiplier, 1f, 1f, settings.StrDuration, 0f),
            StatKind.AGI => new CombatPresetBonus(stat, 1f, settings.AgiMoveMultiplier, settings.AgiAttackSpeedMultiplier, settings.AgiDuration, 0f),
            StatKind.CON => new CombatPresetBonus(stat, 1f, 1f, 1f, 0f, Math.Max(0f, targetMaxHp * settings.ConShieldRatio)),
            StatKind.DEF => new CombatPresetBonus(stat, 1f, 1f, 1f, settings.DefInvulnerableSeconds, 0f),
            _ => new CombatPresetBonus(stat, 1f, 1f, 1f, 0f, 0f)
        };
    }

    public readonly struct CombatWeaponSwapPlan
    {
        public readonly bool BowEquipped;
        public readonly float NextAttackMultiplier, MoveMultiplier, MoveDuration;

        public CombatWeaponSwapPlan(bool wasBowEquipped, float existingNextAttack, float swapNextAttack,
            float moveMultiplier, float moveDuration)
        {
            BowEquipped = !wasBowEquipped;
            NextAttackMultiplier = Math.Max(existingNextAttack, swapNextAttack);
            MoveMultiplier = moveMultiplier;
            MoveDuration = moveDuration;
        }
    }
}
