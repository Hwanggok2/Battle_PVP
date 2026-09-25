using System;
using System.Collections.Generic;

namespace BattlePvp.Combat
{
    public static class CompetitionRanking
    {
        // Competition ranking: equal scores share a place, e.g. 1, 1, 3.
        public static int GetRank<T>(int points, IReadOnlyList<T> entries, Func<T, int> pointsOf)
        {
            if (entries == null || entries.Count == 0) return 0;
            int rank = 1;
            for (int i = 0; i < entries.Count; i++)
                if (pointsOf(entries[i]) > points) rank++;
            return rank;
        }
    }
}
