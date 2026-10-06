using System.Collections.Generic;
using System.Text;
using BattlePvp.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class RoomStartNotice : MonoBehaviour
    {
        public static RoomStartNotice Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.gameObject.activeInHierarchy;
        private TMP_Text _players;
        private ScrollRect _scroll;

        public static void Show(IReadOnlyList<string> names, Transform owner)
        {
            if (Instance != null) { Instance.SetNames(names); return; }
            var root = new GameObject("Room Start Notice", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(owner, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 230;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var backdrop = RoomUiElements.Rect("Backdrop", root.transform, Vector2.zero, Vector2.zero);
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.sizeDelta = Vector2.zero;
            backdrop.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .6f);
            var panel = RoomUiElements.Rect("Panel", root.transform, new Vector2(720, 560), Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(.018f, .045f, .075f, .99f);
            panel.gameObject.AddComponent<ExpandedPanelLayout>();
            var font = owner.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;
            RoomUiElements.Text("Title", panel, font, "스텟 분배를 기다리고 있습니다", new Vector2(640, 48), new Vector2(0, 225), 29);
            RoomUiElements.Text("Hint", panel, font, "아래 플레이어가 스텟 분배를 마치면 시작할 수 있습니다.", new Vector2(640, 40), new Vector2(0, 177), 21);
            var viewport = RoomUiElements.Rect("Players Viewport", panel, new Vector2(640, 310), new Vector2(0, -10));
            viewport.gameObject.AddComponent<Image>().color = new Color(.04f, .085f, .12f, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            var notice = root.AddComponent<RoomStartNotice>(); Instance = notice;
            notice._scroll = viewport.gameObject.AddComponent<ScrollRect>(); notice._scroll.viewport = viewport;
            notice._scroll.horizontal = false; notice._scroll.movementType = ScrollRect.MovementType.Clamped;
            notice._players = RoomUiElements.Text("Players", viewport, font, "", new Vector2(616, 0), Vector2.zero, 24);
            notice._players.alignment = TextAlignmentOptions.TopLeft; notice._players.textWrappingMode = TextWrappingModes.Normal;
            notice._players.margin = new Vector4(0, 10, 0, 10); notice._players.lineSpacing = 8;
            var content = notice._players.rectTransform; content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            notice._scroll.content = content;
            RoomUiElements.Button("Confirm", panel, font, "확인", new Vector2(240, 58), new Vector2(0, -220)).onClick.AddListener(notice.Close);
            notice.SetNames(names); GameInputController.RefreshCursorState();
        }

        private void SetNames(IReadOnlyList<string> names)
        {
            var text = new StringBuilder();
            foreach (string name in names)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append("• ").Append(UserDisplayText.SingleLine(name, UserDisplayText.NameLimit, "플레이어"));
            }
            if (text.Length == 0) text.Append("참가자 정보를 확인하고 있습니다. 잠시 후 다시 시도해 주세요.");
            UserTextPresentation.SetPlain(_players, text.ToString());
            Canvas.ForceUpdateCanvases(); _scroll.verticalNormalizedPosition = 1;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
            GameInputController.RefreshCursorState();
        }
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            GameInputController.RefreshCursorState();
        }
    }
}
