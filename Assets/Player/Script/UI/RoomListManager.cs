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
        private static RoomListManager _activeInstance;

        [Header("UI References")]
        [SerializeField] private RectTransform _contentParent;      // ScrollView의 Content
        [SerializeField] private RoomListItem _itemPrefab;          // 방 항목 프리팹
        [SerializeField] private Button _refreshButton;             // 새로고침 버튼

        [SerializeField] private float _autoRefreshSeconds = 60f;

        private readonly List<RoomListItem> _instantiatedItems = new List<RoomListItem>();
        private Coroutine _autoRefreshRoutine;
        private int _refreshVersion;
        private bool _isAlive;

        private void OnEnable()
        {
            _isAlive = true;
            if (_activeInstance != null && _activeInstance != this)
            {
                _activeInstance._refreshVersion++;
                _activeInstance.ClearList();
            }
            _activeInstance = this;

            if (_refreshButton != null)
                _refreshButton.onClick.AddListener(RefreshList);

            if (PlayFabBattleManager.Instance != null)
                PlayFabBattleManager.Instance.OnRoomRegistryChanged += RefreshList;
            
            // 초기 1회 로드
            RefreshList();

            if (_autoRefreshRoutine != null) StopCoroutine(_autoRefreshRoutine);
            _autoRefreshRoutine = StartCoroutine(CoAutoRefresh());
        }

        private void OnDisable()
        {
            _isAlive = false;
            if (_activeInstance == this)
                _activeInstance = null;

            if (_refreshButton != null)
                _refreshButton.onClick.RemoveListener(RefreshList);

            if (PlayFabBattleManager.Instance != null)
                PlayFabBattleManager.Instance.OnRoomRegistryChanged -= RefreshList;

            if (_autoRefreshRoutine != null)
            {
                StopCoroutine(_autoRefreshRoutine);
                _autoRefreshRoutine = null;
            }

            _refreshVersion++;
            ClearList();
        }

        private void OnDestroy()
        {
            _isAlive = false;
            _refreshVersion++;
            if (_activeInstance == this)
                _activeInstance = null;
        }

        private IEnumerator CoAutoRefresh()
        {
            var wait = new WaitForSeconds(Mathf.Max(1f, _autoRefreshSeconds));
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
            if (!_isAlive || this == null) return;
            if (_activeInstance != this) return;
            if (PlayFabBattleManager.Instance == null) return;
            EnsureContentParent();
            if (_contentParent == null || _itemPrefab == null)
            {
                Debug.LogError("[RoomList] Missing content parent or item prefab.");
                return;
            }

            int requestVersion = ++_refreshVersion;

            // 실제 서버(글로벌 레지스트리)로부터 데이터를 가져옵니다.
            PlayFabBattleManager.Instance.GetActiveRoomInfos(rooms =>
            {
                if (!_isAlive || this == null || requestVersion != _refreshVersion) return;
                if (_activeInstance != this || _contentParent == null || _itemPrefab == null) return;

                ClearList();

                if (rooms.Count == 0)
                {
                    Debug.Log("[RoomList] No rooms returned.");
                }

                int visibleCount = 0;
                var shownRoomTitles = new HashSet<string>();
                foreach (var kvp in rooms)
                {
                    string roomId = kvp.Key;
                    string roomTitle = string.IsNullOrWhiteSpace(kvp.Value.RoomName) ? "Unnamed Room" : kvp.Value.RoomName.Trim();
                    if (!shownRoomTitles.Add(roomTitle)) continue;

                    var item = Instantiate(_itemPrefab, _contentParent);
                    item.SetInfo(roomTitle, kvp.Value.MasterName, kvp.Value.PlayerCount, _ => OnRoomSelected(roomId));
                    _instantiatedItems.Add(item);
                    visibleCount++;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentParent);
                Debug.Log($"[RoomList] Refresh completed. {visibleCount} rooms shown ({rooms.Count} returned).");
            });
        }

        private void EnsureContentParent()
        {
            if (_contentParent != null && _contentParent.name == "Content") return;

            var scrollRect = GetComponent<ScrollRect>();
            if (scrollRect != null && scrollRect.content != null)
            {
                _contentParent = scrollRect.content;
                return;
            }

            var content = transform.Find("Viewport/Content");
            if (content is RectTransform contentRect)
                _contentParent = contentRect;
        }

        private void ClearList()
        {
            if (this == null) return;
            foreach (var item in _instantiatedItems)
            {
                if (item != null) Destroy(item.gameObject);
            }
            _instantiatedItems.Clear();

            if (_contentParent != null)
            {
                var contentItems = _contentParent.GetComponentsInChildren<RoomListItem>(true);
                foreach (var item in contentItems)
                {
                    if (item != null) Destroy(item.gameObject);
                }
            }

            var childItems = GetComponentsInChildren<RoomListItem>(true);
            foreach (var item in childItems)
            {
                if (item != null) Destroy(item.gameObject);
            }
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
