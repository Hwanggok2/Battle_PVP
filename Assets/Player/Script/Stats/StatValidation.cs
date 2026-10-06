using System;

namespace BattlePvp.Stats
{
    /// <summary>Shared rules for untrusted preset requests. Item bonuses remain server owned.</summary>
    public static class StatValidation
    {
        public const float InvestmentBudget = 30f;
        private const float Epsilon = 0.0001f;

        public static bool IsValidPreset(StatContainer stats)
        {
            return IsValidSlot(stats.STR) && IsValidSlot(stats.CON) &&
                   IsValidSlot(stats.AGI) && IsValidSlot(stats.DEF) &&
                   stats.STR.Invested + stats.CON.Invested + stats.AGI.Invested + stats.DEF.Invested
                       <= InvestmentBudget + Epsilon;
        }

        public static bool TryValidateClientStats(StatContainer candidate, StatContainer serverCurrent,
            out StatContainer validated)
        {
            validated = serverCurrent;
            if (!IsValidPreset(candidate) ||
                !SameItem(candidate.STR, serverCurrent.STR) ||
                !SameItem(candidate.CON, serverCurrent.CON) ||
                !SameItem(candidate.AGI, serverCurrent.AGI) ||
                !SameItem(candidate.DEF, serverCurrent.DEF))
                return false;

            validated.STR.Invested = candidate.STR.Invested;
            validated.CON.Invested = candidate.CON.Invested;
            validated.AGI.Invested = candidate.AGI.Invested;
            validated.DEF.Invested = candidate.DEF.Invested;
            return true;
        }

        public static bool IsCompletePreset(StatContainer stats) => IsValidPreset(stats) &&
            Math.Abs(stats.STR.Invested + stats.CON.Invested + stats.AGI.Invested + stats.DEF.Invested - InvestmentBudget) <= Epsilon;

        public static bool CanChangeClientPreset(bool initialized, bool battleScene, bool dead,
            IdentityType nextIdentity)
        {
            if (!initialized) return true; // One validated initial load after spawning.
            if (!battleScene && !dead) return true;
            return dead;
        }

        private static bool SameItem(StatSlot candidate, StatSlot current) =>
            float.IsFinite(current.Item) && Math.Abs(candidate.Item - current.Item) <= Epsilon;

        private static bool IsValidSlot(StatSlot slot) =>
            float.IsFinite(slot.Invested) && slot.Invested >= 0f && slot.Invested <= InvestmentBudget &&
            float.IsFinite(slot.Item) && slot.Item >= 0f && slot.Item <= 10f;
    }
}
