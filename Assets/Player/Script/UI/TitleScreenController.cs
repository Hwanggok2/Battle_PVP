using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class TitleScreenController : MonoBehaviour
    {
        [SerializeField] private GameObject _loginPanel;
        [SerializeField] private Button _title;
        private bool _opened;
        private void Awake() { _loginPanel.SetActive(false); _title.gameObject.SetActive(true); _title.onClick.AddListener(Open); }
        private void OnDestroy() { if (_title != null) _title.onClick.RemoveListener(Open); }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!_opened && keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) Open();
        }
        public void Open() { if (_opened) return; _opened = true; _title.gameObject.SetActive(false); _loginPanel.SetActive(true); LocalGameSettings.Click(); }
    }
}
