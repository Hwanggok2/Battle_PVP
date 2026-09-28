using BattlePvp.Logic;
using BattlePvp.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class RoomPasswordPrompt : MonoBehaviour
    {
        public static RoomPasswordPrompt Instance { get; private set; }
        public static bool IsOpen => Instance != null;
        private TMP_InputField _input;
        private string _roomId;
        public static void Open(string roomId, Transform owner)
        {
            if (Instance != null) Instance.Close();
            var root = new GameObject("Room Password", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(owner, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 220;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            var panel = RoomUiElements.Rect("Password Panel", root.transform, new Vector2(600, 340), Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(.018f, .045f, .075f, .99f);
            var font = owner.GetComponentInChildren<TMP_Text>(true)?.font;
            RoomUiElements.Text("Title", panel, font, "비밀방 입장", new Vector2(520, 55), new Vector2(0, 112), 32);
            var prompt = root.AddComponent<RoomPasswordPrompt>(); Instance = prompt; prompt._roomId = roomId;
            prompt._input = RoomUiElements.Input("Password", panel, font, "암호 입력", new Vector2(520, 62), new Vector2(0, 25), true);
            RoomUiElements.Button("Join", panel, font, "입장", new Vector2(250, 60), new Vector2(-135, -102)).onClick.AddListener(prompt.Join);
            RoomUiElements.Button("Cancel", panel, font, "취소", new Vector2(250, 60), new Vector2(135, -102)).onClick.AddListener(prompt.Close);
            GameInputController.RefreshCursorState(); prompt._input.Select(); prompt._input.ActivateInputField();
        }
        private void Join()
        {
            if (string.IsNullOrEmpty(_input.text)) return;
            PlayFabBattleManager.Instance?.JoinRoom(_roomId, _input.text); Close();
        }
        public void Close()
        {
            _input.text = ""; if (Instance == this) Instance = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            gameObject.SetActive(false); Destroy(gameObject); GameInputController.RefreshCursorState();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
