using UnityEngine;
using TMPro;

namespace BattlePvp.UI
{
    public class RankingEntryUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI rankText;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI scoreText;

        public void SetData(int rank, string playerName, int score)
        {
            if (rankText != null) rankText.text = rank.ToString();
            if (nameText != null) nameText.text = playerName;
            if (scoreText != null) scoreText.text = score.ToString();
        }
    }
}
