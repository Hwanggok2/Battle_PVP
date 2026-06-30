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
                _killerNameText.text = string.IsNullOrWhiteSpace(killerName) ? "Unknown" : killerName;

            if (_victimNameText != null)
                _victimNameText.text = string.IsNullOrWhiteSpace(victimName) ? "Unknown" : victimName;

            if (_killIcon != null && icon != null)
                _killIcon.sprite = icon;
        }
    }
}
