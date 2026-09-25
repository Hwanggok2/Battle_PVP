using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    public enum JobSkillKind
    {
        MonostatStrLifesteal = 0,
        MonostatAgiPoison = 1,
        MonostatConKick = 2,
        MonostatDefTaunt = 3,
        StrategistRoll = 10,
        StrategistPresetChange = 11,
        PolymathRoll = 20,
        PolymathPresetChange = 21,
        PolymathWeaponSwap = 22
    }

    public enum AdvancedSkillEffect { None, KickWindow, TauntReady, Roll, PresetChange, WeaponSwap }

    /// <summary>Skill availability and application policy, independent of assets and network ownership.</summary>
    public static class CombatSkillRules
    {
        public static int SlotCount(Identity identity) => identity.Type switch
        {
            IdentityType.Monostat => 1,
            IdentityType.Strategist => 2,
            IdentityType.Polymath => 2,
            _ => 0
        };

        public static bool TrySelect(Identity identity, int slot, out JobSkillKind kind)
        {
            kind = default;
            if (slot < 0 || slot >= SlotCount(identity)) return false;
            switch (identity.Type)
            {
                case IdentityType.Monostat:
                    switch (identity.PrimaryStat)
                    {
                        case StatKind.STR: kind = JobSkillKind.MonostatStrLifesteal; return true;
                        case StatKind.AGI: kind = JobSkillKind.MonostatAgiPoison; return true;
                        case StatKind.CON: kind = JobSkillKind.MonostatConKick; return true;
                        case StatKind.DEF: kind = JobSkillKind.MonostatDefTaunt; return true;
                        default: return false;
                    }
                case IdentityType.Strategist:
                    kind = slot == 0 ? JobSkillKind.StrategistRoll : JobSkillKind.StrategistPresetChange;
                    return true;
                case IdentityType.Polymath:
                    kind = slot == 0 ? JobSkillKind.PolymathRoll : JobSkillKind.PolymathWeaponSwap;
                    return true;
                default: return false;
            }
        }

        public static bool Allows(Identity identity, JobSkillKind kind)
        {
            for (int slot = 0; slot < SlotCount(identity); slot++)
                if (TrySelect(identity, slot, out JobSkillKind selected) && kind == selected) return true;
            return false;
        }

        public static AdvancedSkillEffect EffectOf(JobSkillKind kind) => kind switch
        {
            JobSkillKind.MonostatConKick => AdvancedSkillEffect.KickWindow,
            JobSkillKind.MonostatDefTaunt => AdvancedSkillEffect.TauntReady,
            JobSkillKind.StrategistRoll or JobSkillKind.PolymathRoll => AdvancedSkillEffect.Roll,
            JobSkillKind.StrategistPresetChange or JobSkillKind.PolymathPresetChange => AdvancedSkillEffect.PresetChange,
            JobSkillKind.PolymathWeaponSwap => AdvancedSkillEffect.WeaponSwap,
            _ => AdvancedSkillEffect.None
        };

        public static bool LocksCastMovement(JobSkillKind kind) =>
            kind != JobSkillKind.MonostatAgiPoison && kind != JobSkillKind.PolymathWeaponSwap;
    }

    /// <summary>An accepted cast's immutable decisions. The owner retains coroutine and cancellation lifetimes.</summary>
    public readonly struct AdvancedSkillPlan
    {
        public readonly JobSkillKind Kind;
        public readonly AdvancedSkillEffect Effect;
        public readonly CombatSkillExecution Execution;
        public readonly bool AppliesImmediately;

        private AdvancedSkillPlan(JobSkillKind kind, CombatSkillExecution execution, bool immediately)
        { Kind = kind; Effect = CombatSkillRules.EffectOf(kind); Execution = execution; AppliesImmediately = immediately; }

        public bool ApplyAtStart => Effect == AdvancedSkillEffect.Roll;
        public bool HasHitWindow => Effect == AdvancedSkillEffect.KickWindow;
        public bool LocksMovement => CombatSkillRules.LocksCastMovement(Kind);

        public bool CanApplyAtFinish(bool alive, bool stillAssigned) =>
            Effect != AdvancedSkillEffect.None && alive && stillAssigned && !AppliesImmediately && !ApplyAtStart && !HasHitWindow;

        public static bool TryBegin(JobSkillKind kind, CombatSkillExecution current, double now, double startedAt,
            float castSeconds, float cooldownSeconds, bool hasCastAnimation, out AdvancedSkillPlan plan)
        {
            plan = default;
            AdvancedSkillEffect effect = CombatSkillRules.EffectOf(kind);
            if (effect == AdvancedSkillEffect.None ||
                !current.TryBegin(now, startedAt, castSeconds, cooldownSeconds, out CombatSkillExecution next)) return false;
            // Zero-cast rolls used to apply immediately when they had no animation, just like other non-kick skills.
            bool immediately = castSeconds <= 0f && !hasCastAnimation && effect != AdvancedSkillEffect.KickWindow;
            plan = new AdvancedSkillPlan(kind, next, immediately);
            return true;
        }
    }
}
