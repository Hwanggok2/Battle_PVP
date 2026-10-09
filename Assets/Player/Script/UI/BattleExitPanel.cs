using BattlePvp.Logic;
using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class BattleExitPanel : MonoBehaviour
    {
        public static BattleExitPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._overlay != null && Instance._overlay.activeSelf;
        private GameObject _overlay;
        private Button _exit;
        private bool _submitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Battle") return;
            var root = new GameObject("Battle exit UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 160;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            root.AddComponent<BattleExitPanel>();
        }

        private void Awake()
        {
            Instance = this;
            var font = BattleResultTheme.SharedFont;
            bool practice = NetworkManager.singleton is BattleNetworkManager manager && manager.IsPractice;
            _exit = RoomUiElements.Button("Exit battle", transform, font, practice ? "연습 종료" : "나가기",
                new Vector2(160, 44), Vector2.zero);
            var rect = (RectTransform)_exit.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(112, -38);
            _exit.onClick.AddListener(Open);

            var overlay = RoomUiElements.Rect("Exit confirmation", transform, Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.sizeDelta = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, .015f, .025f, .65f);
            overlay.gameObject.AddComponent<Canvas>();
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            var dialog = RoomUiElements.Rect("Dialog", overlay, new Vector2(500, 230), Vector2.zero);
            dialog.gameObject.AddComponent<Image>().color = new Color(.025f, .055f, .075f, .98f);
            var title = RoomUiElements.Text("Question", dialog, font, "나가시겠습니까?", new Vector2(450, 60), new Vector2(0, 45), 30);
            title.alignment = TextAlignmentOptions.Center;
            RoomUiElements.Button("Cancel", dialog, font, "취소", new Vector2(180, 48), new Vector2(-102, -60)).onClick.AddListener(Close);
            RoomUiElements.Button("Confirm", dialog, font, "확인", new Vector2(180, 48), new Vector2(102, -60)).onClick.AddListener(Confirm);
            _overlay = overlay.gameObject;
            _overlay.SetActive(false);
        }

        public void Open()
        {
            if (_submitting) return;
            CharacterSelectionPanel.CloseIfOpen(); WeaponSelectionPanel.CloseIfOpen(); JobGuidePanel.CloseIfOpen();
            CharacterInfoController.CloseOpenPanel();
            if (GameSettingsPanel.IsOpen) GameSettingsPanel.Instance.Cancel();
            _overlay.SetActive(true);
            var canvas = _overlay.GetComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 1100;
            GameInputController.RefreshCursorState();
        }

        public void Close()
        {
            if (_submitting) return;
            _overlay.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            GameInputController.RefreshCursorState();
        }

        public void Confirm()
        {
            if (!IsOpen || _submitting || !(NetworkManager.singleton is BattleNetworkManager manager)) return;
            if (!manager.RequestLeaveBattle()) return;
            _submitting = true;
            foreach (var button in _overlay.GetComponentsInChildren<Button>()) button.interactable = false;
        }

        private void LateUpdate()
        {
            bool visible = !IsOpen && !GameSettingsPanel.IsOpen && !JobGuidePanel.IsOpen &&
                !CharacterSelectionPanel.IsOpen && !WeaponSelectionPanel.IsOpen && !CharacterInfoController.HasOpenPanel;
            _exit.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameInputController.RefreshCursorState();
        }
    }
}
