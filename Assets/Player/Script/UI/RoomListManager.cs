using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;
using TMPro;

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

        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private Scrollbar _verticalScrollbar;
        [SerializeField] private GameObject _developerPanelRoot;
        [SerializeField] private TMP_InputField _adminKeyInput;
        [SerializeField] private Button _unlockDeleteButton;
        [SerializeField] private Button _clearAllButton;
        [SerializeField] private TextMeshProUGUI _developerStatusText;

        [SerializeField] private float _autoRefreshSeconds = 60f;
        [SerializeField] private float _scrollSensitivity = 24f;

        private readonly List<RoomListItem> _instantiatedItems = new List<RoomListItem>();
        private Coroutine _autoRefreshRoutine;
        private Coroutine _developerToastRoutine;
        private TextMeshProUGUI _developerToastText;
        private int _refreshVersion;
        private bool _isAlive;
        private bool _isRoomAdminUnlocked;

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

            EnsureScrollBarVisible();
            EnsureDeveloperPanel();
            SetDeveloperPanelVisible(IsDeveloperRoomAdminEnabled());
            
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

            if (_clearAllButton != null)
                _clearAllButton.onClick.RemoveListener(OnClearAllClicked);

            if (_unlockDeleteButton != null)
                _unlockDeleteButton.onClick.RemoveListener(OnUnlockDeleteClicked);

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
                    item.SetInfo(
                        roomId,
                        roomTitle,
                        kvp.Value.MasterName,
                        kvp.Value.PlayerCount,
                        _ => OnRoomSelected(roomId),
                        OnDeleteRoomClicked,
                        CanShowRoomDeleteControls());
                    _instantiatedItems.Add(item);
                    visibleCount++;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentParent);
                Canvas.ForceUpdateCanvases();
                ResizeContentForItems();
                EnsureScrollBarVisible();
                Debug.Log($"[RoomList] Refresh completed. {visibleCount} rooms shown ({rooms.Count} returned).");
            });
        }

        private void EnsureContentParent()
        {
            if (_contentParent != null && _contentParent.name == "Content") return;

            if (_scrollRect == null)
                _scrollRect = GetComponent<ScrollRect>();

            if (_scrollRect != null && _scrollRect.content != null)
            {
                _contentParent = _scrollRect.content;
                return;
            }

            var content = transform.Find("Viewport/Content");
            if (content is RectTransform contentRect)
                _contentParent = contentRect;
        }

        private void EnsureScrollBarVisible()
        {
            if (_scrollRect == null)
                _scrollRect = GetComponent<ScrollRect>();

            if (_scrollRect == null)
                return;

            if (_verticalScrollbar == null)
                _verticalScrollbar = _scrollRect.verticalScrollbar;

            _scrollRect.vertical = true;
            _scrollRect.horizontal = false;
            _scrollRect.scrollSensitivity = Mathf.Max(1f, _scrollSensitivity);
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            if (_scrollRect.horizontalScrollbar != null)
                _scrollRect.horizontalScrollbar.gameObject.SetActive(false);

            if (_verticalScrollbar != null)
            {
                _scrollRect.verticalScrollbar = _verticalScrollbar;
                _verticalScrollbar.gameObject.SetActive(true);
                _verticalScrollbar.interactable = true;
            }
        }

        private void ResizeContentForItems()
        {
            if (_contentParent == null)
                return;

            var layout = _contentParent.GetComponent<VerticalLayoutGroup>();
            float totalHeight = 0f;

            if (layout != null)
            {
                totalHeight += layout.padding.top + layout.padding.bottom;
                int activeChildCount = 0;

                for (int i = 0; i < _contentParent.childCount; i++)
                {
                    var child = _contentParent.GetChild(i) as RectTransform;
                    if (child == null || !child.gameObject.activeSelf)
                        continue;

                    totalHeight += GetChildPreferredHeight(child);
                    activeChildCount++;
                }

                if (activeChildCount > 1)
                    totalHeight += layout.spacing * (activeChildCount - 1);
            }
            else
            {
                for (int i = 0; i < _contentParent.childCount; i++)
                {
                    var child = _contentParent.GetChild(i) as RectTransform;
                    if (child == null || !child.gameObject.activeSelf)
                        continue;

                    totalHeight += GetChildPreferredHeight(child);
                }
            }

            float viewportHeight = 0f;
            if (_scrollRect == null)
                _scrollRect = GetComponent<ScrollRect>();

            if (_scrollRect != null && _scrollRect.viewport != null)
                viewportHeight = _scrollRect.viewport.rect.height;

            _contentParent.anchorMin = new Vector2(0f, 1f);
            _contentParent.anchorMax = new Vector2(1f, 1f);
            _contentParent.pivot = new Vector2(0f, 1f);
            _contentParent.anchoredPosition = Vector2.zero;
            _contentParent.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, totalHeight));

            if (_scrollRect != null)
            {
                _scrollRect.content = _contentParent;
                _scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private static float GetChildPreferredHeight(RectTransform child)
        {
            float preferredHeight = LayoutUtility.GetPreferredHeight(child);
            if (preferredHeight > 0f)
                return preferredHeight;

            if (child.sizeDelta.y > 0f)
                return child.sizeDelta.y;

            return child.rect.height > 0f ? child.rect.height : 32f;
        }

        private void EnsureDeveloperPanel()
        {
            if (!IsDeveloperRoomAdminEnabled())
            {
                SetDeveloperPanelVisible(false);
                return;
            }

            if (_developerPanelRoot == null)
                _developerPanelRoot = CreateDeveloperPanel();

            if (_clearAllButton != null)
            {
                _clearAllButton.onClick.RemoveListener(OnClearAllClicked);
                _clearAllButton.onClick.AddListener(OnClearAllClicked);
            }

            if (_unlockDeleteButton != null)
            {
                _unlockDeleteButton.onClick.RemoveListener(OnUnlockDeleteClicked);
                _unlockDeleteButton.onClick.AddListener(OnUnlockDeleteClicked);
            }

            RefreshDeveloperControlState();
        }

        private GameObject CreateDeveloperPanel()
        {
            Transform panelParent = transform;
            var panel = new GameObject("Developer_Room_Admin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(panelParent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(250f, 192f);

            var image = panel.GetComponent<Image>();
            image.color = new Color(0.05f, 0.06f, 0.07f, 0.78f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreatePanelLabel(panel.transform, "Developer Rooms", 18f);
            _adminKeyInput = CreateInput(panel.transform, "RoomAdminKey");
            _unlockDeleteButton = CreatePanelButton(panel.transform, "Unlock Delete");
            _clearAllButton = CreatePanelButton(panel.transform, "Clear All Rooms");
            _developerStatusText = CreatePanelLabel(panel.transform, "", 14f);
            _developerStatusText.textWrappingMode = TextWrappingModes.Normal;

            return panel;
        }

        private static TextMeshProUGUI CreatePanelLabel(Transform parent, string text, float fontSize)
        {
            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 28f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Left;

            return label;
        }

        private static TMP_InputField CreateInput(Transform parent, string placeholder)
        {
            var inputObject = new GameObject("AdminKey_Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            inputObject.transform.SetParent(parent, false);

            var rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 34f);

            var image = inputObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.92f);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(inputObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 16f;
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            placeholderObject.transform.SetParent(inputObject.transform, false);
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8f, 3f);
            placeholderRect.offsetMax = new Vector2(-8f, -3f);

            var placeholderText = placeholderObject.GetComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 16f;
            placeholderText.color = new Color(0f, 0f, 0f, 0.45f);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            var input = inputObject.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.contentType = TMP_InputField.ContentType.Password;
            input.lineType = TMP_InputField.LineType.SingleLine;

            return input;
        }

        private static Button CreatePanelButton(Transform parent, string label)
        {
            var buttonObject = new GameObject(label.Replace(" ", "_"), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 34f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.48f, 0.1f, 0.1f, 0.95f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            return button;
        }

        private static bool IsDeveloperRoomAdminEnabled()
        {
#if UNITY_EDITOR
            return true;
#else
            return Debug.isDebugBuild;
#endif
        }

        private bool CanShowRoomDeleteControls()
        {
            return IsDeveloperRoomAdminEnabled() && _isRoomAdminUnlocked;
        }

        private void RefreshDeveloperControlState()
        {
            bool canDelete = CanShowRoomDeleteControls();

            if (_clearAllButton != null)
            {
                _clearAllButton.gameObject.SetActive(canDelete);
                _clearAllButton.interactable = canDelete;
            }

            if (_unlockDeleteButton != null)
            {
                _unlockDeleteButton.gameObject.SetActive(!canDelete);
                _unlockDeleteButton.interactable = IsDeveloperRoomAdminEnabled();
            }

            if (_adminKeyInput != null)
                _adminKeyInput.interactable = !canDelete;
        }

        private void SetDeveloperPanelVisible(bool visible)
        {
            if (_developerPanelRoot != null)
                _developerPanelRoot.SetActive(visible);
        }

        private string GetAdminKey()
        {
            return _adminKeyInput != null ? _adminKeyInput.text : string.Empty;
        }

        private void SetDeveloperStatus(string message)
        {
            if (_developerStatusText != null)
                _developerStatusText.text = message;

            if (!string.IsNullOrWhiteSpace(message))
                ShowDeveloperToast(message);
        }

        private void OnDeleteRoomClicked(string roomId)
        {
            if (!IsDeveloperRoomAdminEnabled() || PlayFabBattleManager.Instance == null)
                return;

            if (!_isRoomAdminUnlocked)
            {
                SetDeveloperStatus("Unlock delete first.");
                return;
            }

            SetDeveloperStatus($"Deleting {roomId}...");
            PlayFabBattleManager.Instance.AdminDeleteRoom(GetAdminKey(), roomId, (ok, message) =>
            {
                SetDeveloperStatus(ok ? "Room deleted." : message);
                if (ok) RefreshList();
            });
        }

        private void OnClearAllClicked()
        {
            if (!IsDeveloperRoomAdminEnabled() || PlayFabBattleManager.Instance == null)
                return;

            if (!_isRoomAdminUnlocked)
            {
                SetDeveloperStatus("Unlock delete first.");
                return;
            }

            SetDeveloperStatus("Clearing rooms...");
            PlayFabBattleManager.Instance.AdminClearRoomRegistry(GetAdminKey(), (ok, message) =>
            {
                SetDeveloperStatus(ok ? "All rooms cleared." : message);
                if (ok) RefreshList();
            });
        }

        private void OnUnlockDeleteClicked()
        {
            if (!IsDeveloperRoomAdminEnabled() || PlayFabBattleManager.Instance == null)
                return;

            if (string.IsNullOrWhiteSpace(GetAdminKey()))
            {
                SetDeveloperStatus("RoomAdminKey is empty.");
                return;
            }

            SetDeveloperStatus("Checking RoomAdminKey...");
            PlayFabBattleManager.Instance.AdminValidateRoomKey(GetAdminKey(), (ok, message) =>
            {
                _isRoomAdminUnlocked = ok;
                RefreshDeveloperControlState();
                SetDeveloperStatus(ok ? "Delete unlocked." : message);
                if (ok) RefreshList();
            });
        }

        private void ShowDeveloperToast(string message)
        {
            if (_developerToastText == null)
                _developerToastText = CreateDeveloperToast();

            if (_developerToastText == null)
                return;

            _developerToastText.transform.parent.gameObject.SetActive(true);
            _developerToastText.text = message;

            if (_developerToastRoutine != null)
                StopCoroutine(_developerToastRoutine);

            _developerToastRoutine = StartCoroutine(CoHideDeveloperToast());
        }

        private IEnumerator CoHideDeveloperToast()
        {
            yield return new WaitForSecondsRealtime(4f);

            if (_developerToastText != null)
                _developerToastText.transform.parent.gameObject.SetActive(false);

            _developerToastRoutine = null;
        }

        private TextMeshProUGUI CreateDeveloperToast()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;

            var toastObject = new GameObject("Developer_RoomAdmin_Toast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            toastObject.transform.SetParent(canvas.transform, false);

            var rect = toastObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(520f, 48f);

            var image = toastObject.GetComponent<Image>();
            image.color = new Color(0.02f, 0.02f, 0.02f, 0.88f);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(toastObject.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            toastObject.SetActive(false);
            return text;
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
