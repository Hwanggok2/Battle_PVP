using BattlePvp.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.EditorTools
{
    public static class BattleChatUICreator
    {
        [MenuItem("GameObject/Battle PVP/UI/Battle Chat UI", false, 10)]
        private static void CreateBattleChatUI(MenuCommand command)
        {
            Canvas canvas = ResolveCanvas(command.context as GameObject);

            GameObject root = CreateUIObject("BattleChatUI", canvas.transform);
            var panelRect = root.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 18f);
            panelRect.sizeDelta = new Vector2(620f, 220f);
            root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.42f);

            GameObject resizeHandle = CreateUIObject("ResizeHandle", root.transform);
            var resizeRect = resizeHandle.GetComponent<RectTransform>();
            resizeRect.anchorMin = new Vector2(0f, 1f);
            resizeRect.anchorMax = new Vector2(1f, 1f);
            resizeRect.pivot = new Vector2(0.5f, 1f);
            resizeRect.anchoredPosition = Vector2.zero;
            resizeRect.sizeDelta = new Vector2(0f, 8f);
            resizeHandle.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            GameObject logScroll = CreateUIObject("LogScroll", root.transform);
            var logScrollRect = logScroll.GetComponent<RectTransform>();
            logScrollRect.anchorMin = Vector2.zero;
            logScrollRect.anchorMax = Vector2.one;
            logScrollRect.offsetMin = new Vector2(10f, 42f);
            logScrollRect.offsetMax = new Vector2(-10f, -14f);
            logScroll.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            var scrollRect = logScroll.AddComponent<ScrollRect>();

            GameObject viewport = CreateUIObject("Viewport", logScroll.transform);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8f, 6f);
            viewportRect.offsetMax = new Vector2(-8f, -6f);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            GameObject content = CreateUIObject("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var logText = content.AddComponent<TextMeshProUGUI>();
            logText.fontSize = 18f;
            logText.color = new Color(1f, 1f, 1f, 0.94f);
            logText.alignment = TextAlignmentOptions.BottomLeft;
            logText.textWrappingMode = TextWrappingModes.Normal;
            logText.raycastTarget = false;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            GameObject inputObject = CreateUIObject("Input", root.transform);
            var inputRect = inputObject.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.offsetMin = new Vector2(10f, 8f);
            inputRect.offsetMax = new Vector2(-10f, 36f);
            inputObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var input = inputObject.AddComponent<TMP_InputField>();

            GameObject inputTextObject = CreateUIObject("Text", inputObject.transform);
            var inputTextRect = inputTextObject.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(10f, 3f);
            inputTextRect.offsetMax = new Vector2(-10f, -3f);
            var inputText = inputTextObject.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 18f;
            inputText.color = Color.white;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject placeholderObject = CreateUIObject("Placeholder", inputObject.transform);
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 3f);
            placeholderRect.offsetMax = new Vector2(-10f, -3f);
            var placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Enter to chat";
            placeholder.fontSize = 18f;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            input.textViewport = inputRect;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 120;

            var chatUI = root.AddComponent<BattleChatUI>();
            var serialized = new SerializedObject(chatUI);
            serialized.FindProperty("_panelRect").objectReferenceValue = panelRect;
            serialized.FindProperty("_resizeHandle").objectReferenceValue = resizeRect;
            serialized.FindProperty("_viewportRect").objectReferenceValue = viewportRect;
            serialized.FindProperty("_contentRect").objectReferenceValue = contentRect;
            serialized.FindProperty("_scrollRect").objectReferenceValue = scrollRect;
            serialized.FindProperty("_logText").objectReferenceValue = logText;
            serialized.FindProperty("_input").objectReferenceValue = input;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Create Battle Chat UI");
            Selection.activeGameObject = root;
        }

        private static Canvas ResolveCanvas(GameObject context)
        {
            Canvas canvas = null;
            if (context != null)
                canvas = context.GetComponentInParent<Canvas>();

            if (canvas == null)
                canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas != null)
                return canvas;

            GameObject canvasObject = new GameObject("Canvas_BattleChat", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Battle Chat Canvas");
            return canvas;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }
    }
}
