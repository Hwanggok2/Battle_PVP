using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>Shares the lobby room browser, with room management limited to leaving while connected.</summary>
    public sealed class WaitingRoomPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _currentRoom;
        [SerializeField] private TMP_Text _browseHint;
        [SerializeField] private TMP_Text _listHeading;
        [SerializeField] private GameObject _createButton;
        [SerializeField] private GameObject _joinButton;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private RoomListManager _roomList;
        private PlayFabBattleManager _manager;
        private BattleRoomInfoBanner _banner;

        private void OnEnable()
        {
            var canvas = GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 180;
            bool waiting = SceneManager.GetActiveScene().name == "Battle_waiting";
            _heading.text = waiting ? "방 정보" : "대기실 목록";
            _currentRoom.gameObject.SetActive(waiting);
            _browseHint.gameObject.SetActive(waiting);
            _listHeading.gameObject.SetActive(waiting);
            var listRect = (RectTransform)_roomList.transform;
            listRect.sizeDelta = new Vector2(820, waiting ? 320 : 425);
            listRect.anchoredPosition = new Vector2(0, waiting ? -35 : 15);
            _createButton.SetActive(!waiting);
            _joinButton.SetActive(!waiting);
            _leaveButton.gameObject.SetActive(waiting);
            _leaveButton.interactable = true;
            _roomList.SetBrowseOnly(waiting);
            _banner = waiting ? FindFirstObjectByType<BattleRoomInfoBanner>() : null;
            _leaveButton.onClick.AddListener(Leave);
            _manager = PlayFabBattleManager.Instance;
            if (_manager != null) _manager.OnRoomRegistryChanged += RefreshSummary;
            ScoreSystem.OnScoreUpdated += OnRosterChanged;
            RefreshSummary();
            GameInputController.RefreshCursorState();
        }

        private void OnDisable()
        {
            _leaveButton.onClick.RemoveListener(Leave);
            if (_manager != null) _manager.OnRoomRegistryChanged -= RefreshSummary;
            ScoreSystem.OnScoreUpdated -= OnRosterChanged;
            _manager = null;
            GameInputController.RefreshCursorState();
        }

        private void OnRosterChanged(ScoreSystem _) => RefreshSummary();

        private void RefreshSummary()
        {
            var info = _manager != null ? _manager.CurrentRoomInfo : default;
            string room = UserDisplayText.SingleLine(info.RoomName, UserDisplayText.RoomNameLimit, "대기실");
            string host = UserDisplayText.SingleLine(info.MasterName, UserDisplayText.NameLimit, "연결 확인 중");
            int count = 0;
            foreach (var player in ScoreSystem.ActiveScores)
                if (player != null && player.netId != 0) count++;
            UserTextPresentation.SetPlain(_currentRoom, $"현재 방 · {room}\n방장 · {host}    참가자 · {count}/8");
            _browseHint.text = NetworkServer.active
                ? "방장이 나가면 대기실이 종료됩니다. 다른 방 참가는 로비에서 가능합니다."
                : "다른 방 참가는 현재 대기실을 나간 뒤 로비에서 가능합니다.";
        }

        private void Leave()
        {
            if (_banner == null || InputModeRules.CanLockCursor(SceneManager.GetActiveScene().name,
                GameInputController.CurrentMode)) return;
            _leaveButton.interactable = false;
            _banner.LeaveRoom();
        }
    }
}
