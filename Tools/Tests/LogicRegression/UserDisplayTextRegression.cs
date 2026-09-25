using System;
using BattlePvp.UI;

internal static class UserDisplayTextRegression
{
    internal static void Run(Action<bool, string> require)
    {
        CheckSingleLine(require);
        CheckEscaping(require);
        CheckBattleResult(require);
    }

    private static void CheckSingleLine(Action<bool, string> require)
    {
        require(UserDisplayText.NameLimit == 64 && UserDisplayText.MessageLimit == 120 && UserDisplayText.RoomNameLimit == 80 &&
            UserDisplayText.SingleLine(new string('M', 121), UserDisplayText.MessageLimit).Length == 120 &&
            UserDisplayText.SingleLine(new string('R', 81), UserDisplayText.RoomNameLimit).Length == 80,
            "The display limits keep the configured name, message and room-name boundaries.");
        require(UserDisplayText.SingleLine(null, 64, "Unknown") == "Unknown", "A missing display value uses its fallback.");
        require(UserDisplayText.SingleLine("", 64, "Unknown") == "Unknown", "An empty display value uses its fallback.");
        require(UserDisplayText.SingleLine(" \t\r\n\u2028\u2029 ", 64, "Unknown") == "Unknown",
            "Whitespace-only input cannot create an empty visible name.");
        require(UserDisplayText.SingleLine("valid", 0, "fallback") == "fallback" &&
            UserDisplayText.SingleLine("valid", -1, "fallback") == "fallback", "Nonpositive limits use the caller's fallback.");
        require(UserDisplayText.SingleLine("  한글 이름  ", 64) == "한글 이름", "Trimming preserves normal Korean and internal spaces.");
        require(UserDisplayText.SingleLine("한글😀e\u0301", 64) == "한글😀e\u0301", "Valid emoji and combining marks remain unchanged.");
        foreach (char control in new[] { '\0', '\n', '\r', '\t', '\v', '\f', '\u001B', '\u007F', '\u0085', '\u2028', '\u2029' })
            require(UserDisplayText.SingleLine("A" + control + "B", 64) == "A B",
                "Control U+" + ((int)control).ToString("X4") + " cannot insert another display row or truncate the following text.");
        require(UserDisplayText.SingleLine("\0\0", 64, "fallback") == "fallback", "Normalization that removes all visible content uses the fallback.");
        require(UserDisplayText.SingleLine(new string('A', 65), 64) == new string('A', 64), "A 65-character name is bounded at 64.");
        require(UserDisplayText.SingleLine(new string('가', 64), 64).Length == 64, "An exact-limit Korean name remains complete.");
        require(UserDisplayText.SingleLine("A\nBC", 3) == "A B", "Length limiting and control normalization share the same source boundary.");
        require(UserDisplayText.SingleLine(new string('a', 63) + "😀", 64) == new string('a', 63),
            "Truncation before an emoji's low surrogate must not leave a dangling high surrogate.");
        require(UserDisplayText.SingleLine(new string('a', 62) + "😀", 64) == new string('a', 62) + "😀",
            "An emoji that fits exactly at the UTF-16 limit remains intact.");
        require(UserDisplayText.SingleLine("😀tail", 1, "fallback") == "fallback", "A limit inside the first surrogate pair uses the fallback.");
        require(UserDisplayText.SingleLine("😀tail", 2) == "😀", "A two-unit limit preserves one complete emoji.");
        require(UserDisplayText.SingleLine("A😀B", 2) == "A", "An interior surrogate pair is not split.");
        require(UserDisplayText.SingleLine("safe", int.MaxValue) == "safe", "A large caller limit does not enlarge the original value.");
        require(BattlePvp.Combat.MatchLedger.NormalizeName(new string('a', 63) + "😀") == new string('a', 63),
            "The match ledger must not split an emoji before the UI receives the retained display name.");
        require(BattlePvp.Combat.MatchLedger.NormalizeName(new string('a', 62) + "😀tail") == new string('a', 62) + "😀",
            "A complete emoji at the ledger's name boundary survives truncation of the following suffix.");
    }

    private static void CheckEscaping(Action<bool, string> require)
    {
        const string opening = "<noparse><</noparse>";
        require(UserDisplayText.EscapePlain(null) == "" && UserDisplayText.EscapePlain("") == "", "Plain escaping accepts absent values.");
        require(UserDisplayText.EscapePlain("한글😀") == "한글😀", "Plain display does not alter normal Unicode.");
        require(UserDisplayText.EscapePlain(@"\n\r\t\v\u000A\U0000000A") == @"\\n\\r\\t\\v\\u000A\\U0000000A",
            "Every TMP control and Unicode escape begins with a doubled literal backslash.");
        require(UserDisplayText.EscapePlain(@"C:\names\\player\") == @"C:\\names\\\\player\\",
            "Consecutive and trailing backslashes are all preserved as literal display data.");
        require(UserDisplayText.EscapePlain("<size=500%>이름</size>") == "<size=500%>이름</size>",
            "Plain escaping preserves literal tags for a richText-disabled label instead of changing the stored name.");
        require(UserDisplayText.EscapePlain(UserDisplayText.SingleLine("A\nB\\n", 64)) == @"A B\\n",
            "A real newline is flattened while the two-character backslash-n stays literal.");
        require(UserDisplayText.EscapeRich(null) == "" && UserDisplayText.EscapeRich("") == "", "Rich-context escaping accepts absent values.");
        require(UserDisplayText.EscapeRich("한글😀 >") == "한글😀 >", "Rich-context escaping preserves normal text and closing delimiters.");
        require(UserDisplayText.EscapeRich("<") == opening && UserDisplayText.EscapeRich("<<") == opening + opening,
            "Each opening delimiter receives its own complete no-parse token.");
        require(UserDisplayText.EscapeRich("<b>이름</b>") == opening + "b>이름" + opening + "/b>",
            "Both opening and closing user tags remain literal in a rich label.");
        require(UserDisplayText.EscapeRich("</noparse><style=evil><br><color=red>X</color>") ==
            opening + "/noparse>" + opening + "style=evil>" + opening + "br>" + opening + "color=red>X" + opening + "/color>",
            "User no-parse closers and early style/line-break tags cannot escape into developer markup.");
        require(UserDisplayText.EscapeRich(@"\u003C<br>\U0000000A") == @"\\u003C" + opening + @"br>\\U0000000A",
            "Backslash and opening-tag defenses compose without modifying generated tokens.");
        require(Count(UserDisplayText.EscapeRich(new string('<', 64)), opening) == 64,
            "Escaping all 64 visible delimiters must not truncate generated protection mid-tag.");
    }

    private static void CheckBattleResult(Action<bool, string> require)
    {
        const string opening = "<noparse><</noparse>";
        const string rawName = "  <size=500%>나</size>\n\\u000A  ";
        const string rawKilledBy = "</noparse><br>상대";
        string[] winners = new string[8];
        for (int i = 0; i < winners.Length; i++) winners[i] = new string((char)('A' + i), 64);
        string winnerList = string.Join(", ", winners);
        var raw = new PersonalBattleResult(rawName, 3, winnerList, 123.5f, 456.26f, rawKilledBy, 7, "<b>상대2</b>", 9);
        PersonalBattleResult display = BattleResultText.ForRichText(raw);
        require(raw.PlayerName == rawName && raw.MostKilledBy == rawKilledBy && raw.WinnerName == winnerList,
            "Preparing a display snapshot must not normalize or escape the original result data.");
        require(display.PlayerName == opening + "size=500%>나" + opening + @"/size> \\u000A",
            "The displayed player name combines one-line normalization with rich-context escaping.");
        require(display.MostKilledBy == opening + "/noparse>" + opening + "br>상대" &&
            display.MostKilled == opening + "b>상대2" + opening + "/b>", "Both opponent name fields use the same safe display boundary.");
        require(winnerList.Length == 526 && UserDisplayText.WinnerListLimit == 526, "Eight 64-character joint winners need 526 UTF-16 units including separators.");
        require(display.WinnerName == winnerList && display.WinnerName.EndsWith(winners[7], StringComparison.Ordinal),
            "A joint-winner list must retain the eighth winner instead of applying the single-name limit.");
        require(Count(display.WinnerName, ", ") == 7, "All joint-winner separators remain present.");
        require(display.Rank == raw.Rank && display.DamageTaken == raw.DamageTaken && display.DamageDealt == raw.DamageDealt &&
            display.MostKilledByCount == raw.MostKilledByCount && display.MostKilledCount == raw.MostKilledCount,
            "Display escaping must not alter ranks, damage or opponent counts.");
        var labels = new BattleResultLabels
        {
            NicknamePrefix = "<color=yellow>Name:</color> ", RankPrefix = "Rank: ", DamageTakenPrefix = "Taken: ",
            DamageDealtPrefix = "Dealt: ", WinnerPrefix = "<b>Winners:</b> ", MostKilledByPrefix = "By: ",
            MostKilledPrefix = "Killed: ", RestartPrompt = "<color=green>Press Enter</color>"
        };
        string built = BattleResultText.Build(display, labels);
        require(built.StartsWith(labels.NicknamePrefix + display.PlayerName + "\nRank: 3\n", StringComparison.Ordinal),
            "Building a result preserves the trusted nickname label markup around the escaped value.");
        require(built.Contains(labels.WinnerPrefix + winnerList, StringComparison.Ordinal), "Developer winner-label markup and the full winner list survive Build.");
        require(built.EndsWith("\n\n" + labels.RestartPrompt, StringComparison.Ordinal), "The developer restart prompt and intentional blank line remain intact.");
        require(Count(built, "\n") == 8, "User newline/Unicode-escape text cannot introduce extra result rows.");
        require(built.Contains("Taken: 123.5\nDealt: 456.3\n", StringComparison.Ordinal), "The existing invariant damage formatting is preserved.");
        require(built.Contains("By: " + display.MostKilledBy + " (7)", StringComparison.Ordinal) &&
            built.Contains("Killed: " + display.MostKilled + " (9)", StringComparison.Ordinal), "Escaping names does not change the displayed opponent counts.");
        require(labels.NicknamePrefix == "<color=yellow>Name:</color> " && labels.RestartPrompt == "<color=green>Press Enter</color>",
            "Build leaves serialized developer labels unchanged.");
        var empty = BattleResultText.ForRichText(new PersonalBattleResult(null, -1, "\r\n", float.NaN, float.PositiveInfinity,
            "\0", -4, "\u2028", -5));
        require(empty.PlayerName == "Unknown" && empty.WinnerName == "Unknown" && empty.MostKilledBy == "None" && empty.MostKilled == "None",
            "Missing or invisible result names use their field-specific fallbacks.");
        require(empty.Rank == -1 && float.IsNaN(empty.DamageTaken) && float.IsPositiveInfinity(empty.DamageDealt) &&
            empty.MostKilledByCount == -4 && empty.MostKilledCount == -5, "ForRichText changes only text, leaving numeric validation to existing formatters.");
        string emptyBuilt = BattleResultText.Build(empty, labels);
        require(emptyBuilt.Contains("Taken: 0\nDealt: 0\n", StringComparison.Ordinal) && emptyBuilt.Contains("By: None (0)", StringComparison.Ordinal),
            "The existing nonfinite damage and negative-count formatting still applies.");
        var bounded = BattleResultText.ForRichText(new PersonalBattleResult(new string('<', 65), 1, winnerList + "EXTRA", 0f, 0f,
            new string('가', 65), 0, new string('a', 63) + "😀", 0));
        require(Count(bounded.PlayerName, opening) == 64 && bounded.MostKilledBy.Length == 64 && bounded.MostKilled.Length == 63,
            "Result names are bounded before escaping, with surrogate-safe truncation.");
        require(bounded.WinnerName == winnerList, "The winner-list bound preserves all eight expected winners and rejects excess trailing display content.");
    }

    private static int Count(string value, string token)
    {
        int count = 0;
        for (int index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length) count++;
        return count;
    }
}
