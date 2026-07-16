using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatReticleView : MonoBehaviour
    {
        private const string RingResourcePath = "Images/Combat_Ring";
        private const string SpikeResourcePath = "Images/Combat_Spike";
        private static readonly Vector2[] SpikeDirections =
        {
            new Vector2(1f, 1f).normalized,
            new Vector2(-1f, 1f).normalized,
            new Vector2(1f, -1f).normalized,
            new Vector2(-1f, -1f).normalized
        };

        [Header("Sprites")]
        [Tooltip("Base reticle sprite. Uses the default Resources sprite when empty.")]
        [SerializeField] private Sprite _baseReticleSprite;
        [Tooltip("Charge reticle sprite. Uses the generated ring and thickness setting when empty.")]
        [SerializeField] private Sprite _chargeReticleSprite;
        [Tooltip("Hit feedback spike sprite. Uses the default Resources sprite when empty.")]
        [SerializeField] private Sprite _hitSpikeSprite;

        [Header("Base Reticle")]
        [SerializeField] private Color _baseColor = new Color(1f, 0f, 0f, 1f);
        [Range(4f, 80f)] [SerializeField] private float _baseSize = 15f;

        private Image _baseImage;
        private Image _chargeImage;
        private readonly Image[] _hitSpikes = new Image[4];
        private Coroutine _hitRoutine;
        private bool _initialized;
        private Texture2D _chargeRingTexture;
        private Sprite _chargeRingSprite;
        private float _chargeRingSpriteThickness = -1f;

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

        private void OnDestroy()
        {
            if (_chargeRingSprite != null)
                Destroy(_chargeRingSprite);
            if (_chargeRingTexture != null)
                Destroy(_chargeRingTexture);
        }

        public void SetCharge(float damageProgress, float maximumScale, Color color, float thickness)
        {
            if (_chargeImage == null)
                return;

            if (_chargeReticleSprite != null)
                _chargeImage.sprite = _chargeReticleSprite;
            else
                EnsureChargeRingSprite(thickness);

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
            float centerGap,
            float maximumLength,
            float spikeWidth)
        {
            if (_hitSpikes[0] == null)
                return;

            if (_hitRoutine != null)
                StopCoroutine(_hitRoutine);
            _hitRoutine = StartCoroutine(AnimateHit(
                color,
                Mathf.Clamp01(startOpacity),
                Mathf.Clamp01(endOpacity),
                Mathf.Max(0.01f, duration),
                Mathf.Clamp(growPortion, 0.05f, 0.95f),
                Mathf.Max(0f, centerGap),
                Mathf.Max(1f, maximumLength),
                Mathf.Max(0.5f, spikeWidth)));
        }

        private void CreateImages(Transform parent)
        {
            Sprite ringSprite = _baseReticleSprite != null
                ? _baseReticleSprite
                : Resources.Load<Sprite>(RingResourcePath);
            Sprite spikeSprite = _hitSpikeSprite != null
                ? _hitSpikeSprite
                : Resources.Load<Sprite>(SpikeResourcePath);
            _baseImage = CreateImage(parent, "BaseReticle", ringSprite, _baseSize, _baseSize, _baseColor, true);
            _chargeImage = CreateImage(parent, "ChargeReticle", _chargeReticleSprite, _baseSize, _baseSize, Color.white, true);

            for (int i = 0; i < _hitSpikes.Length; i++)
            {
                Image spike = CreateImage(parent, $"HitSpike_{i + 1}", spikeSprite, 1f, 1f, Color.clear, false);
                float angle = Mathf.Atan2(SpikeDirections[i].y, SpikeDirections[i].x) * Mathf.Rad2Deg - 90f;
                spike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                _hitSpikes[i] = spike;
            }
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

        private static Image CreateImage(Transform parent, string objectName, Sprite sprite, float width, float height, Color color, bool preserveAspect)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            return image;
        }

        private void EnsureChargeRingSprite(float thickness)
        {
            thickness = Mathf.Clamp(thickness, 0.5f, 8f);
            if (_chargeRingSprite != null && Mathf.Approximately(_chargeRingSpriteThickness, thickness))
                return;

            if (_chargeRingSprite != null)
                Destroy(_chargeRingSprite);
            if (_chargeRingTexture != null)
                Destroy(_chargeRingTexture);

            const int resolution = 64;
            const float radius = 27f;
            float center = (resolution - 1) * 0.5f;
            Color32[] pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float edgeDistance = Mathf.Abs(distance - radius);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((thickness * 0.5f + 0.75f) - edgeDistance) * 255f);
                    pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
                }
            }

            _chargeRingTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "RuntimeChargeRing",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _chargeRingTexture.SetPixels32(pixels);
            _chargeRingTexture.Apply(false, true);
            _chargeRingSprite = Sprite.Create(
                _chargeRingTexture,
                new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            _chargeRingSprite.name = "RuntimeChargeRingSprite";
            _chargeRingSpriteThickness = thickness;
            _chargeImage.sprite = _chargeRingSprite;
        }

        private IEnumerator AnimateHit(
            Color color,
            float startOpacity,
            float endOpacity,
            float duration,
            float growPortion,
            float centerGap,
            float maximumLength,
            float spikeWidth)
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

                float visibleProgress = Mathf.Max(0f, sizeProgress);
                SetSpikeLayout(centerGap, maximumLength * visibleProgress, spikeWidth * visibleProgress);
                Color frameColor = color;
                frameColor.a *= Mathf.Lerp(startOpacity, endOpacity, normalized);
                SetSpikeColor(frameColor);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetHitVisible(false);
            _hitRoutine = null;
        }

        private void SetHitVisible(bool visible)
        {
            foreach (Image spike in _hitSpikes)
            {
                if (spike != null && spike.gameObject.activeSelf != visible)
                    spike.gameObject.SetActive(visible);
            }
        }

        private void SetSpikeLayout(float centerGap, float length, float width)
        {
            for (int i = 0; i < _hitSpikes.Length; i++)
            {
                RectTransform rect = _hitSpikes[i].rectTransform;
                rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, length));
                rect.anchoredPosition = SpikeDirections[i] * (centerGap + length * 0.5f);
            }
        }

        private void SetSpikeColor(Color color)
        {
            foreach (Image spike in _hitSpikes)
            {
                if (spike != null)
                    spike.color = color;
            }
        }
    }
}
