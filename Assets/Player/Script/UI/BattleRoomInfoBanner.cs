using BattlePvp.Networking;
using BattlePvp.Combat;
using Mirror;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class BattleRoomInfoBanner : MonoBehaviour
    {
        [Header("Optional Prefab References")]
        [SerializeField] private RectTransform _bannerRoot;
        [SerializeField] private TextMeshProUGUI _roomNameText;
        [SerializeField] private TextMeshProUGUI _playerCountText;
        [SerializeField] private TextMeshProUGUI _masterNameText;
        [SerializeField] private Button _leaveButton;

        private const string LobbySceneName = "Lobby";
        private readonly HashSet<uint> _seenNetIds = new HashSet<uint>();
        private readonly RoomBannerRequestState _metadataRequests = new RoomBannerRequestState();
        private PlayFabBattleManager _roomManager;

        [ContextMenu("Create Editable Scene UI")]
        private void CreateEditableSceneUI()
        {
            EnsureBanner();
        }

        private void OnEnable()
        {
            _metadataRequests.SetActive(true);
            SceneManager.sceneLoaded += OnSceneLoaded;
            ScoreSystem.OnScoreUpdated += OnRosterUpdated;
            SubscribeRoomEvents();
            RefreshVisibilityAndInfo();
        }

        private void OnDisable()
        {
            _metadataRequests.SetActive(false);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ScoreSystem.OnScoreUpdated -= OnRosterUpdated;
            UnsubscribeRoomEvents();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeRoomEvents();
            RefreshVisibilityAndInfo();
        }

        private bool SubscribeRoomEvents()
        {
            var manager = PlayFabBattleManager.Instance;
            if (ReferenceEquals(_roomManager, manager)) return false;
            UnsubscribeRoomEvents();
            _roomManager = manager;
            if (_roomManager != null) _roomManager.OnRoomRegistryChanged += RefreshVisibilityAndInfo;
            return true;
        }

        private void UnsubscribeRoomEvents()
        {
            if (!ReferenceEquals(_roomManager, null)) _roomManager.OnRoomRegistryChanged -= RefreshVisibilityAndInfo;
            _roomManager = null;
        }

        private void OnRosterUpdated(ScoreSystem _)
        {
            // A manager initialized after this view is bound at the next roster event.
            if (SubscribeRoomEvents()) RefreshVisibilityAndInfo();
            else RefreshPlayerCount();
        }

        private void RefreshVisibilityAndInfo()
        {
            var manager = _roomManager;
            uint request = _metadataRequests.BeginRequest(manager != null ? manager.CurrentRoomId : null);
            RefreshPlayerCount();
            bool shouldShow = IsBattleWaitingScene();
            if (!shouldShow)
            {
                if (_bannerRoot != null)
                    _bannerRoot.gameObject.SetActive(false);
                return;
            }

            EnsureBanner();
            if (_bannerRoot == null) return;
            _bannerRoot.gameObject.SetActive(true);
            
            if (manager == null)
            {
                SetInfo("Unknown Room", "Unknown");
                return;
            }

            SetInfo(manager.CurrentRoomInfo);
            manager.RefreshCurrentRoomInfo(info =>
            {
                if (this == null || !isActiveAndEnabled || manager == null || !ReferenceEquals(_roomManager, manager) ||
                    !_metadataRequests.IsCurrent(request, manager.CurrentRoomId)) return;
                SetInfo(info);
            });
        }

        private bool IsBattleWaitingScene()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == "Battle_wait" || sceneName == "Battle_waiting";
        }

        private void EnsureBanner()
        {
            if (_bannerRoot == null)
            {
                var existing = transform.Find("BattleRoomInfo_Banner");
                if (existing is RectTransform existingRect)
                {
                    _bannerRoot = existingRect;
                    FindExistingTextReferences();
                }
                else
                {
                    CreateBanner();
                }
            }

            EnsureLeaveButton();
        }

        private void CreateBanner()
        {
            var bannerObject = new GameObject("BattleRoomInfo_Banner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
            _bannerRoot = bannerObject.GetComponent<RectTransform>();
            _bannerRoot.SetParent(transform, false);

            _bannerRoot.anchorMin = new Vector2(0f, 1f);
            _bannerRoot.anchorMax = new Vector2(1f, 1f);
            _bannerRoot.pivot = new Vector2(0.5f, 1f);
            _bannerRoot.anchoredPosition = new Vector2(0f, -8f);
            _bannerRoot.sizeDelta = new Vector2(-48f, 38f);

            var image = bannerObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.62f);
            image.raycastTarget = false;

            var layout = bannerObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 0, 0);
            layout.spacing = 36f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            _roomNameText = CreateText("RoomName", 360f, TextAlignmentOptions.Left);
            _playerCountText = CreateText("PlayerCount", 180f, TextAlignmentOptions.Center);
            _masterNameText = CreateText("MasterName", 280f, TextAlignmentOptions.Right);
        }

        private TextMeshProUGUI CreateText(string objectName, float preferredWidth, TextAlignmentOptions alignment)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(_bannerRoot, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.minWidth = Mathf.Min(120f, preferredWidth);
            layout.flexibleWidth = 0f;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private void FindExistingTextReferences()
        {
            if (_roomNameText == null)
                _roomNameText = FindChildText("RoomName");

            if (_playerCountText == null)
                _playerCountText = FindChildText("PlayerCount");

            if (_masterNameText == null)
                _masterNameText = FindChildText("MasterName");

            if (_leaveButton == null)
                _leaveButton = FindChildButton("LeaveButton");
        }

        private TextMeshProUGUI FindChildText(string childName)
        {
            var child = _bannerRoot.Find(childName);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        private Button FindChildButton(string childName)
        {
            var child = _bannerRoot.Find(childName);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private void EnsureLeaveButton()
        {
            if (_bannerRoot == null) return;

            if (_leaveButton == null)
            {
                _leaveButton = FindChildButton("LeaveButton");
            }

            if (_leaveButton == null)
            {
                _leaveButton = CreateLeaveButton();
            }

            _leaveButton.onClick.RemoveListener(OnLeaveButtonClicked);
            _leaveButton.onClick.AddListener(OnLeaveButtonClicked);
        }

        private Button CreateLeaveButton()
        {
            var go = new GameObject("LeaveButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_bannerRoot, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = 110f;
            layout.minWidth = 96f;
            layout.preferredHeight = 30f;
            layout.flexibleWidth = 0f;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.65f, 0.12f, 0.12f, 0.92f);

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.88f, 1f);
            colors.pressedColor = new Color(0.85f, 0.65f, 0.65f, 1f);
            button.colors = colors;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(go.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = "\uB098\uAC00\uAE30";
            text.fontSize = 17f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            return button;
        }

        private void OnLeaveButtonClicked()
        {
            if (_leaveButton != null)
                _leaveButton.interactable = false;

            PlayFabBattleManager.Instance?.LeaveCurrentRoom();
            StartCoroutine(CoLeaveToLobby());
        }

        private IEnumerator CoLeaveToLobby()
        {
            if (NetworkManager.singleton != null)
            {
                if (NetworkServer.active && NetworkClient.active)
                {
                    NetworkManager.singleton.StopHost();
                }
                else if (NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
                else if (NetworkServer.active)
                {
                    NetworkManager.singleton.StopServer();
                }

                yield return null;
            }

            SceneManager.LoadScene(LobbySceneName);
        }

        private void SetInfo(PlayFabBattleManager.RoomInfo info)
        {
            // Room membership can outlive a disconnected player. Metadata never owns live occupancy.
            SetInfo(info.RoomName, info.MasterName);
        }

        private void SetInfo(string roomName, string masterName)
        {
            if (_roomNameText == null || _playerCountText == null || _masterNameText == null)
                return;

            string safeRoomName = UserDisplayText.SingleLine(roomName, UserDisplayText.RoomNameLimit, "Unknown Room");
            string safeMasterName = UserDisplayText.SingleLine(masterName, UserDisplayText.NameLimit, "Unknown");

            UserTextPresentation.SetPlain(_roomNameText, $"Room: {safeRoomName}");
            UserTextPresentation.SetPlain(_masterNameText, $"Master: {safeMasterName}");
            RefreshPlayerCount();
        }

        private void RefreshPlayerCount()
        {
            if (_playerCountText == null) return;
            _seenNetIds.Clear();
            foreach (ScoreSystem score in ScoreSystem.ActiveScores)
                if (score != null && score.netId != 0) _seenNetIds.Add(score.netId);
            _playerCountText.text = $"Players: {_seenNetIds.Count}";
        }
    }
}
