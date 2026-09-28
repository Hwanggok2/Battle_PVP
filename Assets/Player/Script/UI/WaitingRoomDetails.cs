using BattlePvp.Combat;
using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class WaitingRoomDetails : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _title, _password;
        [SerializeField] private Toggle _private;
        [SerializeField] private Button _capacityButton, _save;
        [SerializeField] private TMP_Text _capacityText, _status;
        [SerializeField] private RawImage _mapPreview;
        [SerializeField] private Texture[] _mapImages;
        [SerializeField] private RoomPlayerCard[] _cards;
        private int _capacity = 8;
        private bool _loaded;
        private float _nextRefresh;
        private bool CanEdit => NetworkServer.active && !BattleStartController.IsStarting &&
            PlayFabBattleManager.Instance != null && !PlayFabBattleManager.Instance.RoomSettingsBusy;

        private void Awake()
        {
            _capacityButton.onClick.AddListener(() => { _capacity = _capacity >= 8 ? Mathf.Max(2, ScoreSystem.ActiveScores.Count) : _capacity + 1; Refresh(); });
            _save.onClick.AddListener(Save);
            _private.onValueChanged.AddListener(_ => Refresh());
        }
        private void OnEnable() { _loaded = false; _password.text = ""; _status.text = ""; Refresh(); }
        private void OnDisable() { _password.text = ""; }
        private void Update() { if (Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + .25f; Refresh(); } }
        private void Refresh()
        {
            var settings = BattleMapSelection.Instance;
            if (settings != null && (!_loaded || !NetworkServer.active))
            {
                _title.SetTextWithoutNotify(settings.RoomTitle ?? "대기실");
                _capacity = settings.RoomCapacity; _private.SetIsOnWithoutNotify(settings.PrivateRoom); _loaded = true;
            }
            bool edit = CanEdit;
            _title.interactable = _capacityButton.interactable = _private.interactable = _save.interactable = edit;
            _password.gameObject.SetActive(_private.isOn && NetworkServer.active);
            _password.interactable = edit;
            _capacityText.text = $"정원 · {_capacity}명";
            int selected = settings != null ? settings.Selected : 0;
            if (_mapImages != null && selected < _mapImages.Length) _mapPreview.texture = _mapImages[selected];
            int count = 0;
            foreach (var score in ScoreSystem.ActiveScores)
            {
                if (score == null || !score.IsConnected || count >= _cards.Length) continue;
                _cards[count].gameObject.SetActive(true); _cards[count++].Bind(score, edit);
            }
            for (int i = count; i < _cards.Length; i++) _cards[i].gameObject.SetActive(false);
        }
        private async void Save()
        {
            if (!CanEdit) return;
            _status.text = "설정 저장 중";
            string error = await PlayFabBattleManager.Instance.SaveRoomSettings(_title.text, _capacity, _private.isOn, _password.text);
            if (this == null) return;
            _status.text = error ?? "설정을 저장했습니다.";
            if (error == null) { _password.text = ""; _loaded = false; }
            Refresh();
        }
        public void ShowStatus(string message) { if (_status != null) _status.text = message; }
    }
}
