using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class WaitingMapChoice : MonoBehaviour
    {
        private Button _button;
        private TMP_Text _text;
        private float _nextUpdate;
        private void Awake() { _button = GetComponent<Button>(); _text = GetComponentInChildren<TMP_Text>(); _button.onClick.AddListener(SelectNext); }
        private void OnDestroy() { if (_button != null) _button.onClick.RemoveListener(SelectNext); }
        private void SelectNext()
        {
            if (NetworkServer.active && BattleMapSelection.Instance != null)
                BattleMapSelection.Instance.Select((byte)((BattleMapSelection.Instance.Selected + 1) % BattleMapSelection.MapCount));
        }
        private void Update()
        {
            if (Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + .2f;
            var selection = BattleMapSelection.Instance;
            _button.interactable = NetworkServer.active && selection != null && !BattleStartController.IsStarting;
            _text.text = "전장 · " + BattleMapSelection.MapName(selection != null ? selection.Selected : (byte)0);
        }
    }
}
