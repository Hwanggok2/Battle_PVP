using BattlePvp.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>A non-raycasting tooltip above the skill arc, outside the icon's mask.</summary>
    public sealed class SkillTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private string _description;
        private RectTransform _panel;
        private TMP_Text _text;
        private bool _hovered;
        private static bool HasPointer => GameInputController.CurrentMode != GameInputMode.Gameplay ||
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";

        public void SetDescription(string description)
        {
            if (_description == description) return;
            _description = description;
            if (_text != null) { _text.text = description; FitText(); }
            if (string.IsNullOrEmpty(description)) Hide();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_description) || !HasPointer) return;
            _hovered = true;
            if (_panel == null) CreatePanel();
            _text.text = _description;
            FitText();
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
        }
        public void OnPointerExit(PointerEventData eventData) => Hide();
        private void OnDisable() => Hide();
        private void Update()
        {
            if (_hovered && (!HasPointer ||
                !SkillArcHud.IsVisibleScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))) Hide();
        }
        private void Hide() { _hovered = false; if (_panel != null) _panel.gameObject.SetActive(false); }
        private void OnDestroy() { if (_panel != null) Destroy(_panel.gameObject); }
        private void FitText() => _panel.sizeDelta = new Vector2(420, Mathf.Max(160, _text.preferredHeight + 40));

        private void CreatePanel()
        {
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var go = new GameObject("Skill Description", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            _panel = (RectTransform)go.transform;
            _panel.SetParent(canvas.transform, false);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1, 0);
            _panel.anchoredPosition = new Vector2(-34, 375);
            _panel.sizeDelta = new Vector2(420, 260);
            go.GetComponent<Image>().color = new Color(.018f, .037f, .068f, .98f);
            go.GetComponent<Image>().raycastTarget = false;
            var group = go.GetComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
            var label = new GameObject("Description", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(_panel, false);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24, 18); rect.offsetMax = new Vector2(-24, -18);
            _text = label.GetComponent<TextMeshProUGUI>();
            var source = GetComponentInChildren<TMP_Text>(true);
            _text.font = source != null ? source.font : TMP_Settings.defaultFontAsset;
            _text.fontSize = 20; _text.color = new Color(.82f, .95f, 1f);
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.richText = false; _text.raycastTarget = false;
        }
    }
}
