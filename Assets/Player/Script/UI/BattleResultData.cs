using System;
using System.Globalization;

namespace BattlePvp.UI
{
    public readonly struct PersonalBattleResult
    {
        public readonly string PlayerName, WinnerName, MostKilledBy, MostKilled;
        public readonly int Rank, MostKilledByCount, MostKilledCount;
        public readonly float DamageTaken, DamageDealt;

        public PersonalBattleResult(string playerName, int rank, string winnerName, float damageTaken,
            float damageDealt, string mostKilledBy, int mostKilledByCount, string mostKilled, int mostKilledCount)
        {
            PlayerName = playerName;
            Rank = rank;
            WinnerName = winnerName;
            DamageTaken = damageTaken;
            DamageDealt = damageDealt;
            MostKilledBy = mostKilledBy;
            MostKilledByCount = mostKilledByCount;
            MostKilled = mostKilled;
            MostKilledCount = mostKilledCount;
        }
    }

    public sealed class BattleResultLabels
    {
        public string NicknamePrefix, RankPrefix, DamageTakenPrefix, DamageDealtPrefix, WinnerPrefix;
        public string MostKilledByPrefix, MostKilledPrefix, RestartPrompt;
    }

    public static class BattleResultText
    {
        public static PersonalBattleResult ForRichText(PersonalBattleResult result) => new PersonalBattleResult(
            EscapeName(result.PlayerName, "Unknown"), result.Rank,
            UserDisplayText.EscapeRich(UserDisplayText.SingleLine(result.WinnerName, UserDisplayText.WinnerListLimit, "Unknown")),
            result.DamageTaken, result.DamageDealt, EscapeName(result.MostKilledBy, "None"), result.MostKilledByCount,
            EscapeName(result.MostKilled, "None"), result.MostKilledCount);

        private static string EscapeName(string value, string fallback) =>
            UserDisplayText.EscapeRich(UserDisplayText.SingleLine(value, UserDisplayText.NameLimit, fallback));

        public static string Build(PersonalBattleResult result, BattleResultLabels labels) =>
            $"{labels.NicknamePrefix}{result.PlayerName}\n" +
            $"{labels.RankPrefix}{result.Rank}\n" +
            $"{labels.DamageTakenPrefix}{FormatDamage(result.DamageTaken)}\n" +
            $"{labels.DamageDealtPrefix}{FormatDamage(result.DamageDealt)}\n" +
            $"{labels.WinnerPrefix}{result.WinnerName}\n" +
            $"{labels.MostKilledByPrefix}{FormatOpponent(result.MostKilledBy, result.MostKilledByCount)}\n" +
            $"{labels.MostKilledPrefix}{FormatOpponent(result.MostKilled, result.MostKilledCount)}\n\n" +
            labels.RestartPrompt;

        public static string FormatDamage(float damage) =>
            (float.IsFinite(damage) ? Math.Max(0f, damage) : 0f).ToString("0.#", CultureInfo.InvariantCulture);

        public static string FormatOpponent(string name, int count) =>
            $"{(string.IsNullOrWhiteSpace(name) ? "None" : name)} ({Math.Max(0, count)})";
    }
}
