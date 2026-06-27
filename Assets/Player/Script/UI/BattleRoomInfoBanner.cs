using BattlePvp.Networking;
using TMPro;
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

        [ContextMenu("Create Editable Scene UI")]
        private void CreateEditableSceneUI()
        {
            EnsureBanner();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SubscribeRoomEvents();
            RefreshVisibilityAndInfo();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeRoomEvents();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshVisibilityAndInfo();
        }

        private void SubscribeRoomEvents()
        {
            if (PlayFabBattleManager.Instance == null) return;
            PlayFabBattleManager.Instance.OnRoomRegistryChanged -= RefreshVisibilityAndInfo;
            PlayFabBattleManager.Instance.OnRoomRegistryChanged += RefreshVisibilityAndInfo;
        }

        private void UnsubscribeRoomEvents()
        {
            if (PlayFabBattleManager.Instance == null) return;
            PlayFabBattleManager.Instance.OnRoomRegistryChanged -= RefreshVisibilityAndInfo;
        }

        private void RefreshVisibilityAndInfo()
        {
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
            
            var manager = PlayFabBattleManager.Instance;
            if (manager == null)
            {
                SetInfo("Unknown Room", 0, "Unknown");
                return;
            }

            SetInfo(manager.CurrentRoomInfo);
            manager.RefreshCurrentRoomInfo(SetInfo);
        }

        private bool IsBattleWaitingScene()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == "Battle_wait" || sceneName == "Battle_waiting";
        }

        private void EnsureBanner()
        {
            if (_bannerRoot != null) return;

            var existing = transform.Find("BattleRoomInfo_Banner");
            if (existing is RectTransform existingRect)
            {
                _bannerRoot = existingRect;
                FindExistingTextReferences();
                return;
            }

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
            text.enableWordWrapping = false;
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
        }

        private TextMeshProUGUI FindChildText(string childName)
        {
            var child = _bannerRoot.Find(childName);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        private void SetInfo(PlayFabBattleManager.RoomInfo info)
        {
            SetInfo(info.RoomName, info.PlayerCount, info.MasterName);
        }

        private void SetInfo(string roomName, int playerCount, string masterName)
        {
            if (_roomNameText == null || _playerCountText == null || _masterNameText == null)
                return;

            string safeRoomName = string.IsNullOrWhiteSpace(roomName) ? "Unknown Room" : roomName.Trim();
            string safeMasterName = string.IsNullOrWhiteSpace(masterName) ? "Unknown" : masterName.Trim();

            _roomNameText.text = $"Room: {safeRoomName}";
            _playerCountText.text = $"Players: {Mathf.Max(0, playerCount)}";
            _masterNameText.text = $"Master: {safeMasterName}";
        }
    }
}
