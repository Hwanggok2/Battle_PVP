using System;
using BattlePvp.Combat;
using BattlePvp.Stats;

internal static class CombatSkillPlanRegression
{
    public static void Run(Action<bool, string> require)
    {
        var identities = new[]
        {
            new Identity(IdentityType.Monostat, StatKind.STR),
            new Identity(IdentityType.Monostat, StatKind.AGI),
            new Identity(IdentityType.Monostat, StatKind.CON),
            new Identity(IdentityType.Monostat, StatKind.DEF),
            new Identity(IdentityType.Strategist, StatKind.STR),
            new Identity(IdentityType.Polymath, StatKind.STR)
        };
        var expected = new[]
        {
            new[] { JobSkillKind.MonostatStrLifesteal, JobSkillKind.Hook }, new[] { JobSkillKind.MonostatAgiPoison, JobSkillKind.Stealth },
            new[] { JobSkillKind.MonostatConKick, JobSkillKind.Berserk }, new[] { JobSkillKind.MonostatDefTaunt, JobSkillKind.Bash },
            new[] { JobSkillKind.StrategistRoll, JobSkillKind.StrategistPresetChange },
            new[] { JobSkillKind.PolymathRoll, JobSkillKind.PolymathWeaponSwap }
        };
        int[][] available = { new[]{0,100,101,102}, new[]{1,103,104}, new[]{2,105,102,106}, new[]{3,107,108,109}, new[]{10,11,110,111}, new[]{20,22,110,112} };
        for (int i = 0; i < identities.Length; i++)
        {
            require(CombatSkillRules.SlotCount(identities[i]) == expected[i].Length, "skill slot count");
            require(!CombatSkillRules.TrySelect(identities[i], -1, out _), "negative skill slot rejected");
            require(!CombatSkillRules.TrySelect(identities[i], expected[i].Length, out _), "out of range skill slot rejected");
            for (int slot = 0; slot < expected[i].Length; slot++)
                require(CombatSkillRules.TrySelect(identities[i], slot, out JobSkillKind selected) && selected == expected[i][slot], "skill selection matches identity");
            foreach (JobSkillKind kind in Enum.GetValues<JobSkillKind>())
                require(CombatSkillRules.Allows(identities[i], kind) == Array.Exists(available[i], entry => entry == (int)kind), "server and owner share skill availability");
        }
        require(CombatSkillRules.SlotCount(new Identity((IdentityType)99, StatKind.STR)) == 0, "unknown identity has no skills");
        require(!CombatSkillRules.TrySelect(new Identity(IdentityType.Monostat, (StatKind)99), 0, out _), "unknown monostat rejected");
        require(!CombatSkillRules.LocksCastMovement(JobSkillKind.MonostatAgiPoison), "poison preserves movement exception");
        require(!CombatSkillRules.LocksCastMovement(JobSkillKind.PolymathWeaponSwap), "weapon swap preserves cast movement exception");

        CheckPlans(require);
        CheckPresets(require);
    }

    private static void CheckPlans(Action<bool, string> require)
    {
        var advancedKinds = new[] { JobSkillKind.MonostatConKick, JobSkillKind.MonostatDefTaunt,
            JobSkillKind.StrategistRoll, JobSkillKind.StrategistPresetChange, JobSkillKind.PolymathRoll,
            JobSkillKind.PolymathPresetChange, JobSkillKind.PolymathWeaponSwap };
        foreach (JobSkillKind kind in advancedKinds)
        {
            bool kick = kind == JobSkillKind.MonostatConKick;
            bool roll = kind == JobSkillKind.StrategistRoll || kind == JobSkillKind.PolymathRoll;
            require(AdvancedSkillPlan.TryBegin(kind, default, 10d, 9.9d, 0.7f, 35f, true, out AdvancedSkillPlan plan), "valid cast plan accepted");
            require(Math.Abs(plan.Execution.CastCompleteAt - 10.6d) < 0.00001d && Math.Abs(plan.Execution.CooldownUntil - 44.9d) < 0.00001d, "accepted timestamp owns deadlines");
            require(plan.ApplyAtStart == roll && plan.HasHitWindow == kick && !plan.AppliesImmediately, "existing start/end/hit-window schedule");
            require(plan.CanApplyAtFinish(true, true) == (!kick && !roll), "effect completes exactly at its chosen phase");
            require(!plan.CanApplyAtFinish(false, true) && !plan.CanApplyAtFinish(true, false), "death or changed identity suppresses completion effect");
            require(!AdvancedSkillPlan.TryBegin(kind, plan.Execution, 11d, 11d, 0.7f, 35f, true, out _), "active cast cannot restart");
            CombatSkillExecution cancelled = plan.Execution.Cancel();
            require(!AdvancedSkillPlan.TryBegin(kind, cancelled, 44d, 44d, 0.7f, 35f, true, out _), "cancel keeps cooldown");
            require(AdvancedSkillPlan.TryBegin(kind, cancelled, 44.9d, 44.9d, 0.7f, 35f, true, out _), "cancelled cast can retry at cooldown boundary");
            require(AdvancedSkillPlan.TryBegin(kind, default, 1d, 1d, 0f, 0f, false, out AdvancedSkillPlan instant) && instant.AppliesImmediately == !kick, "zero-cast no-animation exception preserved");
            require(!instant.CanApplyAtFinish(true, true), "instant or hit-window effect not repeated at cast finish");
        }
        require(!default(AdvancedSkillPlan).CanApplyAtFinish(true, true), "rejected/default plan cannot apply");
        require(!AdvancedSkillPlan.TryBegin(JobSkillKind.MonostatStrLifesteal, default, 1d, 1d, 1f, 1f, true, out _), "strength keeps dedicated execution channel");
        require(!AdvancedSkillPlan.TryBegin((JobSkillKind)999, default, 1d, 1d, 1f, 1f, true, out _), "unknown skill plan rejected");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            require(!AdvancedSkillPlan.TryBegin(JobSkillKind.StrategistRoll, default, 1d, 1d, invalid, 1f, true, out AdvancedSkillPlan rejectedCast), "invalid cast duration rejected");
            require(!rejectedCast.CanApplyAtFinish(true, true), "rejected cast output cannot apply an effect");
            require(!AdvancedSkillPlan.TryBegin(JobSkillKind.StrategistRoll, default, 1d, 1d, 1f, invalid, true, out AdvancedSkillPlan rejectedCooldown), "invalid cooldown rejected");
            require(!rejectedCooldown.CanApplyAtFinish(true, true), "rejected cooldown output cannot apply an effect");
        }
    }

    private static void CheckPresets(Action<bool, string> require)
    {
        StatContainer current = Preset(30f, 0f, 0f, 0f), requested = Preset(0f, 0f, 30f, 0f);
        require(CombatPresetPlan.TryCreate(true, current, requested, default, false, out CombatPresetPlan outward), "complete strategist target accepted");
        require(outward.Target.AGI.Invested == 30f && outward.ReturnPreset.STR.Invested == 30f && outward.HasReturnPreset, "outward plan preserves return investment");
        require(current.STR.Invested == 30f && requested.AGI.Invested == 30f, "planning does not mutate caller stats");
        require(CombatPresetPlan.TryCreate(true, requested, requested, outward.ReturnPreset, true, out CombatPresetPlan inward) &&
            inward.Target.STR.Invested == 30f && !inward.HasReturnPreset, "second accepted swap returns to origin");
        require(!CombatPresetPlan.TryCreate(true, current, default, outward.ReturnPreset, true, out _), "incomplete target rejected without replacing accepted return state");
        require(outward.HasReturnPreset && outward.ReturnPreset.STR.Invested == 30f, "rejected plan leaves previous immutable plan unchanged");
        require(CombatPresetPlan.TryCreate(false, current, default, outward.ReturnPreset, true, out CombatPresetPlan nonStrategist) && nonStrategist.HasReturnPreset, "non-strategist preset keeps return state unchanged");
        requested.STR.Item = 40f;
        require(CombatPresetPlan.DominantStat(requested) == StatKind.STR, "preset bonus dominance includes item value");
        require(CombatPresetPlan.TryCreate(true, requested, outward.Target, current, true, out inward) && !inward.HasReturnPreset, "return comparison uses investment only");
        require(CombatPresetPlan.DominantStat(Preset(10f, 10f, 10f, 10f)) == StatKind.STR, "dominance tie starts with strength");
        require(CombatPresetPlan.DominantStat(Preset(0f, 10f, 10f, 10f)) == StatKind.AGI, "dominance tie agility before constitution and defense");
        require(CombatPresetPlan.DominantStat(Preset(0f, 10f, 0f, 10f)) == StatKind.CON, "dominance tie constitution before defense");

        CombatPresetPlan.ResolveVitals(100f, 80f, 150f, 0.5f, 30f, out float hp, out float shield);
        require(hp == 80f && shield == 55f, "maxHP increase grants shield without healing");
        CombatPresetPlan.ResolveVitals(150f, 130f, 100f, 0.5f, 0f, out hp, out shield);
        require(hp == 100f && shield == 30f, "maxHP decrease converts overflow health to shield");
        CombatPresetPlan.ResolveVitals(150f, 50f, 100f, 0.5f, 0f, out hp, out shield);
        require(hp == 50f && shield == 0f, "injured preset decrease preserves current health");
        var settings = new CombatPresetBonusSettings { StrAttackMultiplier = 1.2f, StrDuration = 5f,
            AgiMoveMultiplier = 1.15f, AgiAttackSpeedMultiplier = 1.35f, AgiDuration = 4f,
            ConShieldRatio = 0.2f, DefInvulnerableSeconds = 5f };
        CombatPresetBonus str = CombatPresetBonus.Resolve(StatKind.STR, 100f, settings);
        require(str.AttackMultiplier == 1.2f && str.Duration == 5f && str.Shield == 0f, "strength bonus settings preserved");
        CombatPresetBonus agi = CombatPresetBonus.Resolve(StatKind.AGI, 100f, settings);
        require(agi.MoveMultiplier == 1.15f && agi.AttackSpeedMultiplier == 1.35f && agi.Duration == 4f, "agility bonus settings preserved");
        require(CombatPresetBonus.Resolve(StatKind.CON, 100f, settings).Shield == 20f, "constitution shield uses target maxHP");
        require(CombatPresetBonus.Resolve(StatKind.DEF, 100f, settings).Duration == 5f, "defense invulnerability duration preserved");
        var swap = new CombatWeaponSwapPlan(false, 1.5f, 1.3f, 1.2f, 3f);
        require(swap.BowEquipped && swap.NextAttackMultiplier == 1.5f && swap.MoveMultiplier == 1.2f && swap.MoveDuration == 3f, "swap preserves stronger unconsumed next attack");
        var back = new CombatWeaponSwapPlan(true, 1f, 1.3f, 1.2f, 3f);
        require(!back.BowEquipped && back.NextAttackMultiplier == 1.3f, "swapping back grants configured next attack");
    }

    private static StatContainer Preset(float str, float con, float agi, float def) => new StatContainer
    {
        STR = new StatSlot { Invested = str }, CON = new StatSlot { Invested = con },
        AGI = new StatSlot { Invested = agi }, DEF = new StatSlot { Invested = def }
    };
}
