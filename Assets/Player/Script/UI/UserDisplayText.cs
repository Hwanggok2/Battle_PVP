using System;
using System.Text;

namespace BattlePvp.UI
{
    /// <summary>Display-only normalization. Stored names, account keys and network payloads remain unchanged.</summary>
    public static class UserDisplayText
    {
        public const int NameLimit = 64;
        public const int MessageLimit = 120;
        public const int RoomNameLimit = 80;
        public const int WinnerListLimit = NameLimit * 8 + 2 * 7;

        public static string SingleLine(string value, int limit, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(value) || limit <= 0) return fallback;
            int length = Math.Min(value.Length, limit);
            if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
                length--;
            var text = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = value[i];
                text.Append(char.IsControl(c) || c == '\u2028' || c == '\u2029' ? ' ' : c);
            }
            string result = text.ToString().Trim();
            return result.Length == 0 ? fallback : result;
        }

        // TMP decodes unicode escapes even with parseCtrlCharacters=false. With that flag true,
        // an escaped backslash is consumed once and cannot introduce another unicode/control escape.
        public static string EscapePlain(string value) => (value ?? string.Empty).Replace("\\", "\\\\");

        public static string EscapeRich(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                // Isolate each opening delimiter, including those in </noparse>, <style> and <br>.
                // No complete user-controlled tag is ever passed to either TMP parsing stage.
                if (c == '<') text.Append("<noparse><</noparse>");
                else if (c == '\\') text.Append("\\\\");
                else text.Append(c);
            }
            return text.ToString();
        }
    }
}
