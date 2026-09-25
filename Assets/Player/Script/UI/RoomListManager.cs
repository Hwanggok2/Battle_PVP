using System.Collections;
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

        [SerializeField, Range(1f, 5f)] private float _autoRefreshSeconds = 5f;

        private readonly RoomListState _state = new RoomListState();
        private readonly Dictionary<string, RoomListItem> _itemsById = new Dictionary<string, RoomListItem>();
        private readonly List<string> _removedIds = new List<string>();
        private readonly List<string> _changedIds = new List<string>();
        private readonly List<string> _orderedIds = new List<string>();
        private PlayFabBattleManager _subscribedManager;
        private System.Action<string> _onSelected;
        private Coroutine _autoRefreshRoutine;

        private void OnEnable()
        {
            _state.SetActive(true);
            _onSelected ??= OnRoomSelected;
            if (_refreshButton != null)
                _refreshButton.onClick.AddListener(RefreshList);

            BindRoomSource();
            
            // 초기 1회 로드
            RefreshList();

            if (_autoRefreshRoutine != null) StopCoroutine(_autoRefreshRoutine);
            _autoRefreshRoutine = StartCoroutine(CoAutoRefresh());
        }

        private void OnDisable()
        {
            _state.SetActive(false);
            if (_refreshButton != null)
                _refreshButton.onClick.RemoveListener(RefreshList);

            if (_subscribedManager != null)
                _subscribedManager.OnRoomRegistryChanged -= RefreshList;
            _subscribedManager = null;

            if (_autoRefreshRoutine != null)
            {
                StopCoroutine(_autoRefreshRoutine);
                _autoRefreshRoutine = null;
            }
        }

        private IEnumerator CoAutoRefresh()
        {
            // Bound stale room visibility even with older serialized refresh intervals or paused game time.
            var wait = new WaitForSecondsRealtime(Mathf.Clamp(_autoRefreshSeconds, 1f, 5f));
            while (true)
            {
                yield return wait;
                RefreshList();
            }
        }

        /// <summary>
        /// 서버 또는 데이터 소스로부터 방 목록을 받아와 리스트를 갱신합니다.
        /// </summary>
        public void RefreshList()
        {
            if (!isActiveAndEnabled) return;
            BindRoomSource();
            if (_subscribedManager == null) return;
            if (_contentParent == null || _itemPrefab == null)
            {
                Debug.LogError("[RoomList] Missing content parent or item prefab.");
                return;
            }

            uint request = _state.BeginRequest();
            _subscribedManager.GetActiveRoomInfos(rooms =>
            {
                if (this == null || !isActiveAndEnabled || _contentParent == null || _itemPrefab == null ||
                    !_state.Apply(request, rooms, _removedIds, _changedIds)) return;
                ApplyChangedRows();
            });
        }

        private void BindRoomSource()
        {
            PlayFabBattleManager manager = PlayFabBattleManager.Instance;
            if (_subscribedManager == manager) return;
            if (_subscribedManager != null)
                _subscribedManager.OnRoomRegistryChanged -= RefreshList;
            _subscribedManager = manager;
            if (_subscribedManager != null)
                _subscribedManager.OnRoomRegistryChanged += RefreshList;
        }

        private void ApplyChangedRows()
        {
            if (_removedIds.Count == 0 && _changedIds.Count == 0) return;
            foreach (string roomId in _removedIds)
            {
                if (!_itemsById.TryGetValue(roomId, out RoomListItem item)) continue;
                _itemsById.Remove(roomId);
                if (item == null) continue;
                if (item.IsSelected) OnRoomSelected(string.Empty);
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            foreach (string roomId in _changedIds)
            {
                if (!_itemsById.TryGetValue(roomId, out RoomListItem item) || item == null)
                {
                    item = Instantiate(_itemPrefab, _contentParent);
                    _itemsById[roomId] = item;
                }
                var info = _state.Rooms[roomId];
                item.SetInfo(roomId, info.RoomName, info.MasterName, info.PlayerCount, _onSelected, null, false);
            }
            _state.CopyOrderedIds(_orderedIds);
            for (int i = 0; i < _orderedIds.Count; i++)
            {
                RoomListItem item = _itemsById[_orderedIds[i]];
                if (item.transform.GetSiblingIndex() != i) item.transform.SetSiblingIndex(i);
            }
            LayoutRebuilder.MarkLayoutForRebuild(_contentParent);
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
