using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>Enlarges the complete dialog, including text and hit targets, within its canvas.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ExpandedPanelLayout : MonoBehaviour
    {
        [SerializeField] private float _preferredScale = 1.3f;
        private RectTransform _rect;
        private bool _applying;
        private void OnEnable() => Refresh();
        private void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Refresh(); }
        public void Refresh()
        {
            if (_applying) return;
            _rect ??= GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || _rect.parent == null) return;
            var canvasRect = canvas.rootCanvas.transform as RectTransform;
            if (canvasRect == null) return;
            Vector3 available = _rect.parent.InverseTransformVector(canvasRect.TransformVector(
                new Vector3(canvasRect.rect.width, canvasRect.rect.height, 0)));
            Vector2 size = _rect.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            float scale = Mathf.Min(_preferredScale, (Mathf.Abs(available.x) - 64) / size.x,
                (Mathf.Abs(available.y) - 64) / size.y);
            _applying = true;
            _rect.localScale = Vector3.one * Mathf.Max(.01f, scale);
            _applying = false;
        }
    }
}
