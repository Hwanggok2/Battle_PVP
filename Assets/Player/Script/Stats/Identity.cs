namespace BattlePvp.Stats
{
    /// <summary>
    /// Identity 판정 결과.
    /// </summary>
    public readonly struct Identity
    {
        public IdentityType Type { get; }
        public StatKind PrimaryStat { get; }

        public Identity(IdentityType type, StatKind primaryStat)
        {
            Type = type;
            PrimaryStat = primaryStat;
        }

        public override string ToString() => $"{Type} (Primary={PrimaryStat})";
    }
}

