using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;

namespace BattlePvp.UI
{
    /// <summary>
    /// 방 목록(ScrollView)의 데이터를 채우고 항목을 관리하는 클래스입니다.
    /// </summary>
    public sealed class RoomListManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private RectTransform _contentParent;      // ScrollView의 Content
        [SerializeField] private RoomListItem _itemPrefab;          // 방 항목 프리팹
        [SerializeField] private Button _refreshButton;             // 새로고침 버튼

        private readonly List<RoomListItem> _instantiatedItems = new List<RoomListItem>();

        private void OnEnable()
        {
            if (_refreshButton != null)
                _refreshButton.onClick.AddListener(RefreshList);
            
            // 초기 1회 로드
            RefreshList();
        }

        private void OnDisable()
        {
            if (_refreshButton != null)
                _refreshButton.onClick.RemoveListener(RefreshList);
        }

        /// <summary>
        /// 서버 또는 데이터 소스로부터 방 목록을 받아와 리스트를 갱신합니다.
        /// </summary>
        public void RefreshList()
        {
            if (PlayFabBattleManager.Instance == null) return;

            ClearList();

            // 실제 서버(글로벌 레지스트리)로부터 데이터를 가져옵니다.
            PlayFabBattleManager.Instance.GetActiveRooms(rooms => 
            {
                foreach (var kvp in rooms)
                {
                    string roomId = kvp.Key;
                    string roomTitle = kvp.Value;

                    var item = Instantiate(_itemPrefab, _contentParent);
                    // 현재는 인원수 데이터를 따로 관리하지 않으므로 1명으로 임시 표시 (추후 확장 가능)
                    item.SetInfo(roomTitle, 1, _ => OnRoomSelected(roomId));
                    _instantiatedItems.Add(item);
                }
                Debug.Log($"[RoomList] Refresh completed. {rooms.Count} rooms found.");
            });
        }

        private void ClearList()
        {
            foreach (var item in _instantiatedItems)
            {
                if (item != null) Destroy(item.gameObject);
            }
            _instantiatedItems.Clear();
        }

        private void OnRoomSelected(string roomName)
        {
            // LobbyUIManager에게 선택된 방 이름을 전달합니다.
            if (LobbyUIManager.Instance != null)
            {
                LobbyUIManager.Instance.SetSelectedRoom(roomName);
            }
        }
    }
}
