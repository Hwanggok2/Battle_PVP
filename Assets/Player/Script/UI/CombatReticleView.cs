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
        [Tooltip("Charge reticle sprite. Empty or the same as the base sprite uses a constant-thickness ring.")]
        [SerializeField] private Sprite _chargeReticleSprite;
        [Tooltip("Hit feedback spike sprite. Uses the default Resources sprite when empty.")]
        [SerializeField] private Sprite _hitSpikeSprite;

        [Header("Base Reticle")]
        [SerializeField] private Color _baseColor = new Color(1f, 0f, 0f, 1f);
        [Range(4f, 80f)] [SerializeField] private float _baseSize = 15f;

        private Image _baseImage;
        private Graphic _chargeGraphic;
        private ReticleRingGraphic _chargeRingGraphic;
        private readonly Image[] _hitSpikes = new Image[4];
        private readonly Image[] _statusDamageSpikes = new Image[4];
        private Coroutine _hitRoutine;
        private Coroutine _statusDamageRoutine;
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

        public void SetCharge(float damageProgress, float maximumScale, Color color, float thickness)
        {
            if (_chargeGraphic == null)
                return;

            if (_chargeRingGraphic != null)
                _chargeRingGraphic.Thickness = thickness;

            float scale = Mathf.Lerp(Mathf.Max(1f, maximumScale), 1f, Mathf.Clamp01(damageProgress));
            _chargeGraphic.rectTransform.sizeDelta = Vector2.one * (_baseSize * scale);
            _chargeGraphic.color = color;
            SetChargeVisible(true);
        }

        public void SetChargeVisible(bool visible)
        {
            if (_chargeGraphic != null && _chargeGraphic.gameObject.activeSelf != visible)
                _chargeGraphic.gameObject.SetActive(visible);
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

        public void PlayStatusDamage(
            Color color,
            float startOpacity,
            float endOpacity,
            float duration,
            float growPortion,
            float centerGap,
            float maximumLength,
            float spikeWidth)
        {
            if (_statusDamageSpikes[0] == null)
                return;

            if (_statusDamageRoutine != null)
                StopCoroutine(_statusDamageRoutine);
            _statusDamageRoutine = StartCoroutine(AnimateSpikes(
                _statusDamageSpikes,
                color,
                Mathf.Clamp01(startOpacity),
                Mathf.Clamp01(endOpacity),
                Mathf.Max(0.01f, duration),
                Mathf.Clamp(growPortion, 0.05f, 0.95f),
                Mathf.Max(0f, centerGap),
                Mathf.Max(1f, maximumLength),
                Mathf.Max(0.5f, spikeWidth),
                () => _statusDamageRoutine = null));
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

            if (_chargeReticleSprite == null || _chargeReticleSprite == ringSprite)
            {
                _chargeRingGraphic = CreateRingGraphic(parent, "ChargeReticle", _baseSize, Color.white);
                _chargeGraphic = _chargeRingGraphic;
            }
            else
            {
                _chargeGraphic = CreateImage(
                    parent,
                    "ChargeReticle",
                    _chargeReticleSprite,
                    _baseSize,
                    _baseSize,
                    Color.white,
                    true);
            }

            for (int i = 0; i < _hitSpikes.Length; i++)
            {
                Image spike = CreateImage(parent, $"HitSpike_{i + 1}", spikeSprite, 1f, 1f, Color.clear, false);
                float angle = Mathf.Atan2(SpikeDirections[i].y, SpikeDirections[i].x) * Mathf.Rad2Deg - 90f;
                spike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                _hitSpikes[i] = spike;
            }

            for (int i = 0; i < _statusDamageSpikes.Length; i++)
            {
                Image spike = CreateImage(parent, $"StatusDamageSpike_{i + 1}", spikeSprite, 1f, 1f, Color.clear, false);
                float angle = Mathf.Atan2(SpikeDirections[i].y, SpikeDirections[i].x) * Mathf.Rad2Deg - 90f;
                spike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                _statusDamageSpikes[i] = spike;
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

        private static ReticleRingGraphic CreateRingGraphic(Transform parent, string objectName, float size, Color color)
        {
            GameObject ringObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(ReticleRingGraphic));
            ringObject.transform.SetParent(parent, false);

            RectTransform rect = ringObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * size;

            ReticleRingGraphic ring = ringObject.GetComponent<ReticleRingGraphic>();
            ring.color = color;
            ring.raycastTarget = false;
            return ring;
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
            yield return AnimateSpikes(
                _hitSpikes,
                color,
                startOpacity,
                endOpacity,
                duration,
                growPortion,
                centerGap,
                maximumLength,
                spikeWidth,
                () => _hitRoutine = null);
        }

        private IEnumerator AnimateSpikes(
            Image[] spikes,
            Color color,
            float startOpacity,
            float endOpacity,
            float duration,
            float growPortion,
            float centerGap,
            float maximumLength,
            float spikeWidth,
            System.Action onComplete)
        {
            SetSpikeVisible(spikes, true);
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
                SetSpikeLayout(spikes, centerGap, maximumLength * visibleProgress, spikeWidth * visibleProgress);
                Color frameColor = color;
                frameColor.a *= Mathf.Lerp(startOpacity, endOpacity, normalized);
                SetSpikeColor(spikes, frameColor);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetSpikeVisible(spikes, false);
            onComplete?.Invoke();
        }

        private void SetHitVisible(bool visible)
        {
            SetSpikeVisible(_hitSpikes, visible);
            SetSpikeVisible(_statusDamageSpikes, visible);
        }

        private static void SetSpikeVisible(Image[] spikes, bool visible)
        {
            foreach (Image spike in spikes)
            {
                if (spike != null && spike.gameObject.activeSelf != visible)
                    spike.gameObject.SetActive(visible);
            }
        }

        private static void SetSpikeLayout(Image[] spikes, float centerGap, float length, float width)
        {
            for (int i = 0; i < spikes.Length; i++)
            {
                RectTransform rect = spikes[i].rectTransform;
                rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, length));
                rect.anchoredPosition = SpikeDirections[i] * (centerGap + length * 0.5f);
            }
        }

        private static void SetSpikeColor(Image[] spikes, Color color)
        {
            foreach (Image spike in spikes)
            {
                if (spike != null)
                    spike.color = color;
            }
        }
    }

    internal sealed class ReticleRingGraphic : MaskableGraphic
    {
        private const int SegmentCount = 64;
        private float _thickness = 1f;

        public float Thickness
        {
            get => _thickness;
            set
            {
                float clamped = Mathf.Max(0.5f, value);
                if (Mathf.Approximately(_thickness, clamped))
                    return;

                _thickness = clamped;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Rect rect = rectTransform.rect;
            float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (outerRadius <= 0f)
                return;

            float innerRadius = Mathf.Max(0f, outerRadius - Mathf.Min(_thickness, outerRadius));
            Vector2 center = rect.center;
            Color32 vertexColor = color;

            for (int i = 0; i < SegmentCount; i++)
            {
                float radians = i * Mathf.PI * 2f / SegmentCount;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                vertexHelper.AddVert(center + direction * outerRadius, vertexColor, Vector2.zero);
                vertexHelper.AddVert(center + direction * innerRadius, vertexColor, Vector2.zero);
            }

            for (int i = 0; i < SegmentCount; i++)
            {
                int currentOuter = i * 2;
                int currentInner = currentOuter + 1;
                int nextOuter = ((i + 1) % SegmentCount) * 2;
                int nextInner = nextOuter + 1;
                vertexHelper.AddTriangle(currentOuter, nextOuter, nextInner);
                vertexHelper.AddTriangle(currentOuter, nextInner, currentInner);
            }
        }
    }
}
