using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace BattlePvp.UI
{
    /// <summary>
    /// 방 목록의 각 항목(Prefab)을 관리하는 스크립트입니다.
    /// </summary>
    public sealed class RoomListItem : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _roomNameText;
        [SerializeField] private TextMeshProUGUI _playerCountText;
        [SerializeField] private Button _selectButton;

        private string _roomName;
        private Action<string> _onSelected;

        /// <summary>
        /// 방 정보를 외부(Manager)로부터 주입받아 UI를 갱신합니다.
        /// </summary>
        /// <param name="roomName">방 고유 ID/이름</param>
        /// <param name="playerCount">현재 접속 인원</param>
        /// <param name="onSelected">클릭 시 실행할 콜백</param>
        public void SetInfo(string roomName, int playerCount, Action<string> onSelected)
        {
            _roomName = roomName;
            _onSelected = onSelected;

            if (_roomNameText != null) _roomNameText.text = roomName;
            if (_playerCountText != null) _playerCountText.text = $"{playerCount}";

            if (_selectButton != null)
            {
                _selectButton.onClick.RemoveAllListeners();
                _selectButton.onClick.AddListener(OnItemClicked);
            }
        }

        private void OnItemClicked()
        {
            _onSelected?.Invoke(_roomName);
            Debug.Log($"[RoomListItem] Item Selected: {_roomName}");
        }
    }
}
