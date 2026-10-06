using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public static class RoomUiElements
    {
        // Left to right: character, job, information, stats, settings.
        public static void TopMenuButton(Button button, int column)
        {
            if (button == null) return;
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(116f, 44f);
            rect.anchoredPosition = new Vector2(-82f - (4 - column) * 128f, -42f);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            label.font = BattleResultTheme.SharedFont;
            label.fontSize = 24f;
            label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8f, 2f);
            label.rectTransform.offsetMax = new Vector2(-8f, -2f);
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        public static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, string text, Vector2 size, Vector2 pos, float fontSize = 22)
        {
            var label = Rect(name, parent, size, pos).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = fontSize; label.text = text; label.color = new Color(.8f, .94f, 1);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false; label.richText = false;
            return label;
        }
        public static Button Button(string name, Transform parent, TMP_FontAsset font, string text, Vector2 size, Vector2 pos)
        {
            var rect = Rect(name, parent, size, pos);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.055f, .16f, .21f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Text("Label", rect, font, text, size - new Vector2(16, 4), Vector2.zero).alignment = TextAlignmentOptions.Center;
            rect.gameObject.AddComponent<NeonButtonSound>(); return button;
        }
        public static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font, string hint, Vector2 size, Vector2 pos, bool secret = false)
        {
            var rect = Rect(name, parent, size, pos);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.04f, .085f, .12f, 1);
            var input = rect.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image;
            var viewport = Rect("Viewport", rect, size - new Vector2(24, 8), Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)Text("Text", viewport, font, "", viewport.sizeDelta, Vector2.zero);
            var placeholder = Text("Placeholder", viewport, font, hint, viewport.sizeDelta, Vector2.zero);
            placeholder.color = new Color(.45f, .6f, .68f); input.placeholder = placeholder;
            input.contentType = secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine; input.characterLimit = secret ? 32 : 40;
            return input;
        }
    }
}
