namespace BattlePvp.Combat
{
    /// <summary>One monotonically advancing stream per retained player, including owner changes and uint wrap.</summary>
    public static class CombatRequestSequences
    {
        public static bool IsNewer(uint candidate, uint baseline) =>
            candidate != baseline && unchecked(candidate - baseline) < 0x80000000u;

        public static uint Next(uint current)
        {
            uint next = unchecked(current + 1u);
            return next == 0u ? 1u : next;
        }

        public static uint RestoreOwner(uint local, uint server) =>
            local == 0u || IsNewer(server, local) ? server : local;
    }
}
