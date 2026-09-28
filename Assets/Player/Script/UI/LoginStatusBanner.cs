using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public enum LoginStatusTone { Notice, Pending, Success, Error }

    /// <summary>Authentication feedback only; navigation remains owned by PlayFabLoginUI.</summary>
    public sealed class LoginStatusBanner : MonoBehaviour
    {
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Image _accent;
        [SerializeField] private Image _activity;
        private bool _pending;
        private Color _color;

        public void Show(string heading, string message, LoginStatusTone tone)
        {
            bool visible = !string.IsNullOrEmpty(message);
            gameObject.SetActive(visible);
            if (!visible) return;
            _pending = tone == LoginStatusTone.Pending;
            _color = tone == LoginStatusTone.Error ? new Color(1f, .34f, .48f) :
                tone == LoginStatusTone.Notice ? new Color(1f, .72f, .32f) : new Color(.25f, .91f, .94f);
            if (_heading != null) { _heading.text = heading; _heading.color = _color; }
            if (_message != null) { _message.richText = false; _message.text = message; _message.gameObject.SetActive(true); }
            if (_accent != null) _accent.color = _color;
            if (_activity != null) { _activity.color = _color; _activity.fillAmount = 1; }
        }

        private void Update()
        {
            if (!_pending || _activity == null) return;
            var color = _color;
            color.a = .3f + .7f * Mathf.PingPong(Time.unscaledTime * 1.5f, 1f);
            _activity.color = color;
        }
    }
}
