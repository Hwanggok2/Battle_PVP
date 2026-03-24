using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// reference-formulae.md의 스탯 합산 규칙을 일관되게 제공하는 순수 유틸.
    /// </summary>
    public static class StatMath
    {
        private const float BaseStat = 5f;
        private const float MaxInvested = 30f;
        private const float MaxItem = 10f;

        public static float PureTotal(float invested)
        {
            float inv = Clamp(invested, 0f, MaxInvested);
            return BaseStat + inv; // 아이템 배제
        }

        public static float FinalTotal(float invested, float item)
        {
            float inv = Clamp(invested, 0f, MaxInvested);
            float it = Clamp(item, 0f, MaxItem);
            return BaseStat + inv + it; // 아이템 포함
        }

        public static float PureTotal(StatSlot slot) => PureTotal(slot.Invested);
        public static float FinalTotal(StatSlot slot) => FinalTotal(slot.Invested, slot.Item);

        public static float FinalTotal(StatKind kind, StatContainer stats) => kind switch
        {
            StatKind.STR => FinalTotal(stats.STR),
            StatKind.CON => FinalTotal(stats.CON),
            StatKind.AGI => FinalTotal(stats.AGI),
            _ => FinalTotal(stats.DEF),
        };

        public static float PureTotal(StatKind kind, StatContainer stats) => kind switch
        {
            StatKind.STR => PureTotal(stats.STR),
            StatKind.CON => PureTotal(stats.CON),
            StatKind.AGI => PureTotal(stats.AGI),
            _ => PureTotal(stats.DEF),
        };

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}

