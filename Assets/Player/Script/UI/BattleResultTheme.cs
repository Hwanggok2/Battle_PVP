using TMPro;
using UnityEngine;

namespace BattlePvp.UI
{
    public sealed class BattleResultTheme : ScriptableObject
    {
        public TMP_FontAsset Font;
        private static BattleResultTheme _theme;
        public static TMP_FontAsset SharedFont
        {
            get
            {
                if (_theme == null) _theme = Resources.Load<BattleResultTheme>("BattleResultTheme");
                return _theme != null && _theme.Font != null ? _theme.Font : TMP_Settings.defaultFontAsset;
            }
        }
    }
}
