using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class KillAnnouncementItemUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _killerNameText;
        [SerializeField] private Image _killIcon;
        [SerializeField] private TMP_Text _victimNameText;

        public void SetData(string killerName, string victimName, Sprite icon = null)
        {
            if (_killerNameText != null)
                UserTextPresentation.SetPlain(_killerNameText, UserDisplayText.SingleLine(killerName, UserDisplayText.NameLimit, "Unknown"));

            if (_victimNameText != null)
                UserTextPresentation.SetPlain(_victimNameText, UserDisplayText.SingleLine(victimName, UserDisplayText.NameLimit, "Unknown"));

            if (_killIcon != null && icon != null)
                _killIcon.sprite = icon;
        }
    }
}
