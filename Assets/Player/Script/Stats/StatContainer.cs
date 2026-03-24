namespace BattlePvp.Stats
{
    /// <summary>
    /// STR/CON/AGI/DEF 각각의 투자/아이템 수치를 묶은 컨테이너.
    /// </summary>
    [System.Serializable]
    public struct StatContainer
    {
        public StatSlot STR;
        public StatSlot CON;
        public StatSlot AGI;
        public StatSlot DEF;
    }
}

