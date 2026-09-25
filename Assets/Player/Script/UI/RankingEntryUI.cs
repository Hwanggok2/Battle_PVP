using UnityEngine;
using TMPro;

namespace BattlePvp.UI
{
    public class RankingEntryUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI rankText;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI deathText;
        private bool _hasData;
        private int _rank, _score, _deaths;
        private string _playerName;

        public void SetData(int rank, string playerName, int score, int deaths)
        {
            if (rankText != null && (!_hasData || _rank != rank)) rankText.text = rank.ToString();
            if (nameText != null && (!_hasData || _playerName != playerName))
                UserTextPresentation.SetPlain(nameText, UserDisplayText.SingleLine(playerName, UserDisplayText.NameLimit));
            if (scoreText != null && (!_hasData || _score != score)) scoreText.text = score.ToString();
            if (deathText != null && (!_hasData || _deaths != deaths)) deathText.text = deaths.ToString();
            _hasData = true;
            _rank = rank;
            _playerName = playerName;
            _score = score;
            _deaths = deaths;
        }
    }
}
