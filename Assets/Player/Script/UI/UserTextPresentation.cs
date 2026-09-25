using TMPro;

namespace BattlePvp.UI
{
    public static class UserTextPresentation
    {
        public static void SetPlain(TMP_Text target, string value)
        {
            if (target == null) return;
            target.richText = false;
            target.parseCtrlCharacters = true;
            target.text = UserDisplayText.EscapePlain(value);
        }

        // Only developer-authored markup and EscapeRich user values may enter this path.
        public static void SetRich(TMP_Text target, string value)
        {
            if (target == null) return;
            target.richText = true;
            target.parseCtrlCharacters = true;
            target.text = value;
        }
    }
}
