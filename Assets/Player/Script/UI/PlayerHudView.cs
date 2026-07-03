using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>
    /// Default implementation of IPlayerHudView.
    /// Connects Unity UI references to the player HUD data pushed by PlayerHUD.
    /// </summary>
    public class PlayerHudView : MonoBehaviour, IPlayerHudView
    {
        [Header("HP")]
        [SerializeField] private Slider _hpSlider;
        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private Slider _shieldSlider;
        [SerializeField] private RectTransform _shieldRoot;
        [SerializeField] private Image _shieldFillImage;
        [SerializeField] private Vector2 _shieldStartOffset;
        [SerializeField] private Image _overflowEffect;

        [Header("Identity")]
        [SerializeField] private TextMeshProUGUI _identityText;

        [Header("Skill")]
        [SerializeField] private SkillUI _skillUI;

        [Header("Match Info")]
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private TextMeshProUGUI _scoreText;

        [Header("Death Overlay")]
        [SerializeField] private GameObject _deathDimObject;
        [SerializeField] private TextMeshProUGUI _deathCountdownText;

        [Header("Loading Overlay")]
        [SerializeField] private GameObject _loadingDimObject;

        private Color _defaultDeathTextColor = Color.white;
        private float _lastHpCurrent;
        private float _lastHpMax;
        private float _currentShield;
        private bool _shieldSliderAutoCreated;
        private bool _usingCustomShieldImage;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            if (_deathCountdownText != null)
                _defaultDeathTextColor = _deathCountdownText.color;
        }

        public void SetHp(float current, float max)
        {
            ResolveReferences();

            if (_hpSlider != null)
                _hpSlider.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            _lastHpCurrent = current;
            _lastHpMax = max;
            UpdateShieldDisplay();
            UpdateHpText();
        }

        public void SetShield(float shield)
        {
            ResolveReferences();
            _currentShield = Mathf.Max(0f, shield);
            UpdateShieldDisplay();
            UpdateHpText();
        }

        private void UpdateHpText()
        {
            if (_hpText != null)
            {
                string shield = _currentShield > 0.5f ? $" ({Mathf.CeilToInt(_currentShield)})" : string.Empty;
                _hpText.text = $"{Mathf.CeilToInt(_lastHpCurrent)} / {Mathf.CeilToInt(_lastHpMax)}{shield}";
            }
        }

        private void UpdateShieldDisplay()
        {
            if (_shieldSlider == null && _shieldFillImage == null)
                return;

            bool visible = _currentShield > 0.5f;
            SetShieldObjectActive(visible);
            if (!visible)
                return;

            if (_hpSlider != null && (_shieldSliderAutoCreated || _usingCustomShieldImage))
                PositionShieldSegment();
            else if (_shieldSlider != null)
                _shieldSlider.value = Mathf.Clamp01(_currentShield / Mathf.Max(1f, _lastHpMax));

            if (_shieldFillImage != null && _shieldFillImage.type == Image.Type.Filled)
                _shieldFillImage.fillAmount = 1f;
        }

        public void SetIdentity(Identity identity)
        {
            if (_identityText != null)
                _identityText.text = $"{identity.Type} ({identity.PrimaryStat})";
        }

        public void SetSkill(SkillHudState state)
        {
            ResolveSkillUI();

            if (_skillUI != null)
                _skillUI.SetState(state);
        }

        public void SetOverflow(bool isOverflow, float overlapPercent)
        {
            if (_overflowEffect != null)
                _overflowEffect.gameObject.SetActive(isOverflow);
        }

        public void SetMatchTimer(float seconds)
        {
            if (_timerText == null)
                return;

            int min = Mathf.FloorToInt(seconds / 60);
            int sec = Mathf.FloorToInt(seconds % 60);
            _timerText.text = $"{min:00}:{sec:00}";
        }

        public void SetCountdown(string text, bool active)
        {
            if (_countdownText == null)
                return;

            _countdownText.gameObject.SetActive(active);
            _countdownText.text = text;
        }

        public void SetDeathOverlay(bool active, string text = "", Color? textColor = null)
        {
            if (_deathDimObject != null)
                _deathDimObject.SetActive(active);

            if (_deathCountdownText != null && active)
            {
                _deathCountdownText.text = text;
                _deathCountdownText.color = textColor ?? _defaultDeathTextColor;
            }
        }

        public void SetScore(int points)
        {
            if (_scoreText != null)
                _scoreText.text = $"{points}";
        }

        public void SetLoadingOverlay(bool active)
        {
            if (_loadingDimObject != null)
            {
                _loadingDimObject.SetActive(active);
            }
            else if (_deathDimObject != null)
            {
                _deathDimObject.SetActive(active);
                if (_deathCountdownText != null && active)
                    _deathCountdownText.text = "Loading...";
            }
        }

        private void ResolveReferences()
        {
            if (_hpSlider == null)
            {
                Slider[] sliders = GetComponentsInChildren<Slider>(true);
                _hpSlider = FindNamed(sliders, "hp", "health");
                if (_hpSlider == null && sliders.Length == 1)
                    _hpSlider = sliders[0];
            }

            if (_hpText == null)
            {
                TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
                _hpText = FindNamed(texts, "hp", "health");
                if (_hpText == null && texts.Length == 1)
                    _hpText = texts[0];
            }

            if (_overflowEffect == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                _overflowEffect = FindNamed(images, "overflow");
            }

            ResolveShieldReferences();

            if (_shieldSlider == null && _shieldFillImage == null)
            {
                if (_hpSlider != null)
                {
                    _shieldSlider = CreateShieldSlider(_hpSlider);
                    _shieldRoot = _shieldSlider.transform as RectTransform;
                    _shieldFillImage = _shieldSlider.fillRect != null ? _shieldSlider.fillRect.GetComponent<Image>() : null;
                    _shieldSliderAutoCreated = true;
                }
            }

            ResolveSkillUI();
        }

        private void ResolveShieldReferences()
        {
            if (_shieldFillImage != null)
            {
                _usingCustomShieldImage = !_shieldSliderAutoCreated;
                if (_shieldRoot == null)
                    _shieldRoot = _shieldFillImage.transform as RectTransform;
                return;
            }

            if (_shieldSlider == null)
            {
                Slider[] sliders = GetComponentsInChildren<Slider>(true);
                _shieldSlider = FindNamed(sliders, "shield");
            }

            if (_shieldSlider != null)
            {
                _shieldRoot = _shieldSlider.transform as RectTransform;
                _shieldFillImage = _shieldSlider.fillRect != null ? _shieldSlider.fillRect.GetComponent<Image>() : _shieldSlider.GetComponentInChildren<Image>(true);
                _usingCustomShieldImage = !_shieldSliderAutoCreated;
                return;
            }

            Image[] images = GetComponentsInChildren<Image>(true);
            _shieldFillImage = FindNamed(images, "shield");
            if (_shieldFillImage != null)
            {
                _shieldRoot = _shieldFillImage.transform as RectTransform;
                _usingCustomShieldImage = true;
            }
        }

        private void SetShieldObjectActive(bool active)
        {
            if (_shieldRoot != null)
                _shieldRoot.gameObject.SetActive(active);
            else if (_shieldSlider != null)
                _shieldSlider.gameObject.SetActive(active);
            else if (_shieldFillImage != null)
                _shieldFillImage.gameObject.SetActive(active);
        }

        private static Slider CreateShieldSlider(Slider hpSlider)
        {
            RectTransform hpRect = hpSlider.transform as RectTransform;
            Transform parent = hpSlider.transform.parent != null ? hpSlider.transform.parent : hpSlider.transform;

            GameObject root = new GameObject("Shield Slider", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            if (hpRect != null)
            {
                rootRect.anchorMin = hpRect.anchorMin;
                rootRect.anchorMax = hpRect.anchorMax;
                rootRect.pivot = hpRect.pivot;
                rootRect.sizeDelta = new Vector2(0f, hpRect.sizeDelta.y);
                rootRect.anchoredPosition = hpRect.anchoredPosition;
            }

            Image sourceFill = hpSlider.fillRect != null ? hpSlider.fillRect.GetComponent<Image>() : null;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(root.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Image fill = fillObject.AddComponent<Image>();
            fill.color = new Color(1f, 0.82f, 0.08f, 0.9f);
            if (sourceFill != null)
            {
                fill.sprite = sourceFill.sprite;
                fill.material = sourceFill.material;
                fill.type = sourceFill.type;
                fill.pixelsPerUnitMultiplier = sourceFill.pixelsPerUnitMultiplier;
                fill.preserveAspect = sourceFill.preserveAspect;
                fill.fillCenter = sourceFill.fillCenter;
                fill.fillMethod = sourceFill.fillMethod;
                fill.fillOrigin = sourceFill.fillOrigin;
                fill.fillClockwise = sourceFill.fillClockwise;
            }

            Slider slider = root.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.fillRect = fillRect;
            slider.targetGraphic = fill;
            root.SetActive(false);
            return slider;
        }

        private void PositionShieldSegment()
        {
            RectTransform hpRect = _hpSlider.transform as RectTransform;
            RectTransform shieldRect = _shieldRoot != null ? _shieldRoot : (_shieldSlider != null ? _shieldSlider.transform as RectTransform : _shieldFillImage.transform as RectTransform);
            if (hpRect == null || shieldRect == null)
                return;

            float hpWidth = hpRect.rect.width;
            if (hpWidth <= 0.01f)
                hpWidth = Mathf.Abs(hpRect.sizeDelta.x);
            if (hpWidth <= 0.01f)
                hpWidth = 1f;

            float hpHeight = hpRect.rect.height;
            if (hpHeight <= 0.01f)
                hpHeight = Mathf.Abs(hpRect.sizeDelta.y);

            float hpRatio = Mathf.Clamp01(_lastHpCurrent / Mathf.Max(1f, _lastHpMax));
            float shieldRatio = Mathf.Clamp(_currentShield / Mathf.Max(1f, _lastHpMax), 0f, 1.5f);
            float shieldWidth = hpWidth * shieldRatio;
            float hpLeft = hpRect.anchoredPosition.x - (hpRect.pivot.x * hpWidth);
            float shieldLeft = hpLeft + (hpWidth * hpRatio) + _shieldStartOffset.x;

            shieldRect.anchorMin = hpRect.anchorMin;
            shieldRect.anchorMax = hpRect.anchorMax;
            shieldRect.pivot = hpRect.pivot;
            shieldRect.sizeDelta = new Vector2(shieldWidth, hpHeight);
            shieldRect.anchoredPosition = new Vector2(shieldLeft + (shieldRect.pivot.x * shieldWidth), hpRect.anchoredPosition.y + _shieldStartOffset.y);
            if (_shieldSlider != null)
                _shieldSlider.value = 1f;
        }

        private static T FindNamed<T>(T[] components, params string[] keywords) where T : Component
        {
            foreach (T component in components)
            {
                if (component == null)
                    continue;

                string lowerName = component.name.ToLowerInvariant();
                foreach (string keyword in keywords)
                {
                    if (lowerName.Contains(keyword))
                        return component;
                }
            }

            return null;
        }

        private void ResolveSkillUI()
        {
            if (_skillUI != null)
                return;

            _skillUI = GetComponentInChildren<SkillUI>(true);
            if (_skillUI != null)
                return;

            Transform parent = transform.parent;
            if (parent != null)
            {
                _skillUI = parent.GetComponentInChildren<SkillUI>(true);
                if (_skillUI != null)
                    return;
            }

            _skillUI = GetComponentInParent<SkillUI>(true);
        }
    }
}
