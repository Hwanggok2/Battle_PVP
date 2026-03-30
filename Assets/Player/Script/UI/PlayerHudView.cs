using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattlePvp.Stats;

namespace BattlePvp.UI
{
    /// <summary>
    /// IPlayerHudView의 실제 구현체.
    /// 유니티 인스펙터에서 텍스트와 슬라이더를 연결하여 화면에 표시합니다.
    /// </summary>
    public class PlayerHudView : MonoBehaviour, IPlayerHudView
    {
        [Header("HP")]
        [SerializeField] private Slider _hpSlider;
        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private Image _overflowEffect;

        [Header("Identity")]
        [SerializeField] private TextMeshProUGUI _identityText;

        [Header("Match Info")]
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private TextMeshProUGUI _scoreText;

        public void SetHp(float current, float max)
        {
            if (_hpSlider != null) _hpSlider.value = current / max;
            if (_hpText != null) _hpText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        public void SetIdentity(Identity identity)
        {
            if (_identityText != null)
                _identityText.text = $"{identity.Type} ({identity.PrimaryStat})";
        }

        public void SetOverflow(bool isOverflow, float overlapPercent)
        {
            if (_overflowEffect != null)
                _overflowEffect.gameObject.SetActive(isOverflow);
        }

        public void SetMatchTimer(float seconds)
        {
            if (_timerText == null) return;
            
            int min = Mathf.FloorToInt(seconds / 60);
            int sec = Mathf.FloorToInt(seconds % 60);
            _timerText.text = $"{min:00}:{sec:00}";
        }

        public void SetCountdown(string text, bool active)
        {
            if (_countdownText == null) return;
            _countdownText.gameObject.SetActive(active);
            _countdownText.text = text;
        }

        public void SetScore(int points)
        {
            if (_scoreText != null)
                _scoreText.text = $"{points}";
        }
    }
}
