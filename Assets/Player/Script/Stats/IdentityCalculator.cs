using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// Identity 판정 로직을 "순수 C#"로 분리하기 위한 계산기.
    /// </summary>
    public sealed class IdentityCalculator
    {
        // Reference-formulae.md 기준 상수
        private const float BaseStat = 5f;
        private const float MaxInvested = 30f;
        private const float MinPureTotalDeltaForPolymath = 7f;

        // 전략가 중복 우선순위: STR > CON > AGI > DEF
        private static readonly StatKind[] PrimaryTieBreakOrder =
        {
            StatKind.STR,
            StatKind.CON,
            StatKind.AGI,
            StatKind.DEF
        };

        public readonly struct IdentityDebug
        {
            public IdentityType Type { get; }
            public StatKind PrimaryStat { get; }

            public bool IsMonostat { get; }
            public StatKind MonostatStat { get; }

            public float MaxPureTotal { get; }
            public float MinPureTotal { get; }
            public float PrimaryPureTotal { get; }

            public IdentityDebug(IdentityType type, StatKind primaryStat, bool isMonostat, StatKind monostatStat, float maxPureTotal, float minPureTotal, float primaryPureTotal)
            {
                Type = type;
                PrimaryStat = primaryStat;
                IsMonostat = isMonostat;
                MonostatStat = monostatStat;
                MaxPureTotal = maxPureTotal;
                MinPureTotal = minPureTotal;
                PrimaryPureTotal = primaryPureTotal;
            }
        }

        public Identity ResolveIdentity(StatContainer stats, out IdentityDebug debug)
        {
            float strPure = GetPureTotal(stats.STR);
            float conPure = GetPureTotal(stats.CON);
            float agiPure = GetPureTotal(stats.AGI);
            float defPure = GetPureTotal(stats.DEF);

            float maxPure = Math.Max(Math.Max(strPure, conPure), Math.Max(agiPure, defPure));
            float minPure = Math.Min(Math.Min(strPure, conPure), Math.Min(agiPure, defPure));

            // Monostat: Invested == 30
            bool strMono = IsMaxInvested(stats.STR.Invested);
            bool conMono = IsMaxInvested(stats.CON.Invested);
            bool agiMono = IsMaxInvested(stats.AGI.Invested);
            bool defMono = IsMaxInvested(stats.DEF.Invested);

            StatKind chosenPrimary;
            bool isMonostat = false;
            StatKind monostatStat = StatKind.STR;

            if (strMono || conMono || agiMono || defMono)
            {
                isMonostat = true;
                monostatStat = ChoosePrimaryAmong(stats, StatKind.STR, StatKind.CON, StatKind.AGI, StatKind.DEF);
                chosenPrimary = monostatStat;

                var identity = new Identity(IdentityType.Monostat, chosenPrimary);
                debug = new IdentityDebug(identity.Type, identity.PrimaryStat, isMonostat, monostatStat, maxPure, minPure, GetPureTotalFromKind(chosenPrimary, stats));
                return identity;
            }

            // Polymath: (Max PureTotal - Min PureTotal) <= 7
            if ((maxPure - minPure) <= MinPureTotalDeltaForPolymath)
            {
                chosenPrimary = ChoosePrimaryByMaxPure(strPure, conPure, agiPure, defPure);
                var identity = new Identity(IdentityType.Polymath, chosenPrimary);
                debug = new IdentityDebug(identity.Type, identity.PrimaryStat, false, StatKind.STR, maxPure, minPure, GetPureTotalFromKind(chosenPrimary, stats));
                return identity;
            }

            // Strategist: 위 조건 미충족 시
            chosenPrimary = ChoosePrimaryByMaxPure(strPure, conPure, agiPure, defPure);
            var strategist = new Identity(IdentityType.Strategist, chosenPrimary);
            debug = new IdentityDebug(strategist.Type, strategist.PrimaryStat, false, StatKind.STR, maxPure, minPure, GetPureTotalFromKind(chosenPrimary, stats));
            return strategist;
        }

        private static bool IsMaxInvested(float invested)
        {
            // == 30과의 "부동소수 오차"를 방지하기 위한 epsilon 비교
            const float eps = 0.0001f;
            return invested >= (MaxInvested - eps);
        }

        private static float GetPureTotal(StatSlot slot)
        {
            float invested = Clamp(slot.Invested, 0f, MaxInvested);
            return BaseStat + invested;
        }

        private static float GetPureTotalFromKind(StatKind kind, StatContainer stats) => kind switch
        {
            StatKind.STR => GetPureTotal(stats.STR),
            StatKind.CON => GetPureTotal(stats.CON),
            StatKind.AGI => GetPureTotal(stats.AGI),
            _ => GetPureTotal(stats.DEF),
        };

        private static StatKind ChoosePrimaryAmong(StatContainer stats, StatKind s1, StatKind s2, StatKind s3, StatKind s4)
        {
            // Monostat에서 multiple 후보가 생길 경우에도 결정성을 보장하기 위해 tie-break order를 사용.
            // (설계상 Monostat은 단일 후보를 기대하지만, 입력이 깨져도 확정적으로 동작.)
            if (IsMaxInvested(GetInvestedByKind(stats, StatKind.STR))) return StatKind.STR;
            if (IsMaxInvested(GetInvestedByKind(stats, StatKind.CON))) return StatKind.CON;
            if (IsMaxInvested(GetInvestedByKind(stats, StatKind.AGI))) return StatKind.AGI;
            return StatKind.DEF;
        }

        private static float GetInvestedByKind(StatContainer stats, StatKind kind) => kind switch
        {
            StatKind.STR => stats.STR.Invested,
            StatKind.CON => stats.CON.Invested,
            StatKind.AGI => stats.AGI.Invested,
            _ => stats.DEF.Invested,
        };

        private static StatKind ChoosePrimaryByMaxPure(float strPure, float conPure, float agiPure, float defPure)
        {
            // 동점이면 전략가 스펙의 우선순위 사용: STR > CON > AGI > DEF
            float max = Math.Max(Math.Max(strPure, conPure), Math.Max(agiPure, defPure));
            if (Math.Abs(strPure - max) <= 0.0001f) return StatKind.STR;
            if (Math.Abs(conPure - max) <= 0.0001f) return StatKind.CON;
            if (Math.Abs(agiPure - max) <= 0.0001f) return StatKind.AGI;
            return StatKind.DEF;
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}

