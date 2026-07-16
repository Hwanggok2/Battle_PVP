using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatReticleView : MonoBehaviour
    {
        private const string RingResourcePath = "Images/Combat_Ring";
        private const string HitSpikesResourcePath = "Images/Combat_HitSpikes";

        [Header("Base Reticle")]
        [SerializeField] private Color _baseColor = new Color(1f, 0f, 0f, 1f);
        [Min(1f)] [SerializeField] private float _baseSize = 15f;

        private Image _baseImage;
        private Image _chargeImage;
        private Image _hitImage;
        private Coroutine _hitRoutine;
        private bool _initialized;

        public void InitializeForLocalPlayer(Transform playerRoot)
        {
            if (_initialized || playerRoot == null)
                return;

            Canvas hudCanvas = ResolveHudCanvas(playerRoot);
            if (hudCanvas == null)
                return;

            CreateImages(hudCanvas.transform);
            SetBaseVisible(true);
            SetChargeVisible(false);
            SetHitVisible(false);
            _initialized = true;
        }

        public void SetBaseVisible(bool visible)
        {
            if (_baseImage != null && _baseImage.gameObject.activeSelf != visible)
                _baseImage.gameObject.SetActive(visible);
        }

        public void SetCharge(float damageProgress, float maximumScale, Color color)
        {
            if (_chargeImage == null)
                return;

            float scale = Mathf.Lerp(Mathf.Max(1f, maximumScale), 1f, Mathf.Clamp01(damageProgress));
            _chargeImage.rectTransform.sizeDelta = Vector2.one * (_baseSize * scale);
            _chargeImage.color = color;
            SetChargeVisible(true);
        }

        public void SetChargeVisible(bool visible)
        {
            if (_chargeImage != null && _chargeImage.gameObject.activeSelf != visible)
                _chargeImage.gameObject.SetActive(visible);
        }

        public void PlayHit(
            Color color,
            float startOpacity,
            float endOpacity,
            float duration,
            float growPortion,
            float maximumSize)
        {
            if (_hitImage == null)
                return;

            if (_hitRoutine != null)
                StopCoroutine(_hitRoutine);
            _hitRoutine = StartCoroutine(AnimateHit(
                color,
                Mathf.Clamp01(startOpacity),
                Mathf.Clamp01(endOpacity),
                Mathf.Max(0.01f, duration),
                Mathf.Clamp(growPortion, 0.05f, 0.95f),
                Mathf.Max(1f, maximumSize)));
        }

        private void CreateImages(Transform parent)
        {
            Sprite ringSprite = Resources.Load<Sprite>(RingResourcePath);
            Sprite hitSprite = Resources.Load<Sprite>(HitSpikesResourcePath);
            _baseImage = CreateImage(parent, "BaseReticle", ringSprite, _baseSize, _baseColor);
            _chargeImage = CreateImage(parent, "ChargeReticle", ringSprite, _baseSize, Color.white);
            _hitImage = CreateImage(parent, "HitSpikes", hitSprite, 1f, Color.clear);
        }

        private static Canvas ResolveHudCanvas(Transform playerRoot)
        {
            Canvas[] canvases = playerRoot.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas canvas in canvases)
            {
                if (canvas != null && canvas.name == "Canvas_HUD")
                    return canvas;
            }

            return null;
        }

        private static Image CreateImage(Transform parent, string objectName, Sprite sprite, float size, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * size;

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private IEnumerator AnimateHit(Color color, float startOpacity, float endOpacity, float duration, float growPortion, float maximumSize)
        {
            SetHitVisible(true);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float normalized = Mathf.Clamp01(elapsed / duration);
                float sizeProgress;
                if (normalized < growPortion)
                {
                    float grow = normalized / growPortion;
                    sizeProgress = 1f - Mathf.Pow(1f - grow, 3f);
                }
                else
                {
                    float shrink = (normalized - growPortion) / (1f - growPortion);
                    sizeProgress = 1f - shrink * shrink;
                }

                _hitImage.rectTransform.sizeDelta = Vector2.one * (maximumSize * Mathf.Max(0f, sizeProgress));
                Color frameColor = color;
                frameColor.a *= Mathf.Lerp(startOpacity, endOpacity, normalized);
                _hitImage.color = frameColor;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetHitVisible(false);
            _hitRoutine = null;
        }

        private void SetHitVisible(bool visible)
        {
            if (_hitImage != null && _hitImage.gameObject.activeSelf != visible)
                _hitImage.gameObject.SetActive(visible);
        }
    }
}
