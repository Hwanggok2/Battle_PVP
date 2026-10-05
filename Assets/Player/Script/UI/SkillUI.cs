using BattlePvp.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class SkillUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private Image _baseImage;
        [SerializeField] private Image _overlayImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _indexText;
        [SerializeField] private bool _useDirectKeyLabel;

        [Header("Preview")]
        [SerializeField] private JobSkillData _previewSkillData;

        [Header("Fallback Display")]
        [SerializeField] private Sprite _fallbackIconSprite;
        [SerializeField] private Color _activeOverlayColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _cooldownOverlayColor = new Color(0.25f, 0.25f, 0.25f, 0.82f);

        [Header("Runtime Debug")]
        [SerializeField] private SkillHudPhase _lastPhase = SkillHudPhase.Hidden;
        [SerializeField] private float _lastFill;
        [SerializeField] private float _lastRemainingSeconds;
        private bool _initialized;
        private bool _hasState;
        private bool _hasReceivedState;
        private int _displayedSeconds = -1;
        private int _displayedIndex = -1;
        private int _displayedCount = -1;
        private SkillTooltip _tooltip;

        public void SetDescription(string description)
        {
            if (_tooltip == null) _tooltip = GetComponent<SkillTooltip>();
            if (_tooltip != null) _tooltip.SetDescription(description);
        }

        private void Awake()
        {
            if (!_initialized) InitializeDisplay();
            if (!_hasReceivedState)
                SetState(new SkillHudState(false, string.Empty, 0, 0, SkillHudPhase.Hidden, 0f, 0f));
            ApplyPreview();
        }

#if UNITY_EDITOR
        private int _pendingEditorValidation;

        private void OnValidate()
        {
            if (System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 1) == 0)
                EditorValidationQueue.Enqueue(ApplyPendingEditorValidation);
        }

        private void ApplyPendingEditorValidation()
        {
            if (System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 0) == 0 || this == null)
                return;
            // The next authoritative HUD snapshot restores the live display. An Inspector
            // edit must never replace a running cooldown or visibility with preview data.
            _initialized = false;
            if (Application.IsPlaying(gameObject)) return;
            InitializeDisplay();
            ApplyPreview();
        }
#endif

        public void SetState(SkillHudState state)
        {
            // SetActive below can invoke our first Awake. That Awake must preserve this request,
            // including an explicit Hidden state, instead of restoring an editor preview.
            _hasReceivedState = true;
            if (!_initialized) InitializeDisplay();
            bool phaseChanged = !_hasState || _lastPhase != state.Phase;
            _lastPhase = state.Phase;
            _lastFill = state.NormalizedFill;
            _lastRemainingSeconds = state.RemainingSeconds;

            RectTransform root = ResolveRoot();
            if (root == null)
                return;

            if (root.gameObject.activeSelf != state.Visible) root.gameObject.SetActive(state.Visible);
            if (!state.Visible)
                return;

            Sprite iconSprite = state.IconSprite != null
                ? state.IconSprite
                : (_fallbackIconSprite != null ? _fallbackIconSprite : GetRuntimeWhiteSprite());

            if (_baseImage != null && _baseImage.sprite != iconSprite)
                _baseImage.sprite = iconSprite;

            if (_nameText != null && _nameText.text != state.Name)
                _nameText.text = state.Name;

            if (_indexText != null && _useDirectKeyLabel)
                _indexText.text = (state.SelectedIndex == 0 ? LocalGameSettings.Current.skill1 : LocalGameSettings.Current.skill2).ToUpperInvariant();
            else if (_indexText != null && (!_hasState || _displayedIndex != state.SelectedIndex || _displayedCount != state.SkillCount))
                _indexText.text = state.SkillCount > 1 ? $"{state.SelectedIndex + 1}/{state.SkillCount}" : string.Empty;
            _displayedIndex = state.SelectedIndex;
            _displayedCount = state.SkillCount;

            bool showTimer = state.Phase == SkillHudPhase.Casting ||
                             state.Phase == SkillHudPhase.Active ||
                             state.Phase == SkillHudPhase.Cooldown || state.RemainingSeconds > 0;

            if (_timerText != null)
            {
                if (_timerText.gameObject.activeSelf != showTimer) _timerText.gameObject.SetActive(showTimer);
                int seconds = showTimer ? Mathf.CeilToInt(state.RemainingSeconds) : -1;
                if (!_hasState || _displayedSeconds != seconds)
                    _timerText.text = showTimer ? seconds.ToString() : string.Empty;
                _displayedSeconds = seconds;
            }
            _hasState = true;
            if (_overlayImage == null)
                return;

            if (phaseChanged)
            {
                ConfigureOverlayPanel(OverlayColorForPhase(state.Phase));
                ConfigureOverlayImage(state.Phase);
            }
            _overlayImage.fillAmount = state.NormalizedFill;

            if (_overlayImage.gameObject.activeSelf != showTimer) _overlayImage.gameObject.SetActive(showTimer);
        }

        private void InitializeDisplay()
        {
            ResolveReferences();
            if (_baseImage != null)
                ConfigureImage(_baseImage, _baseImage.sprite, Image.Type.Simple, Color.white);
            ConfigureOverlayImage(SkillHudPhase.Hidden);
            ArrangeLayers();
            _hasState = false;
            _initialized = true;
        }

        private void OnTransformChildrenChanged() => _initialized = false;

        private void ApplyPreview()
        {
            if (_previewSkillData == null || Application.isPlaying)
                return;

            if (_baseImage != null && _previewSkillData.IconSprite != null)
                ConfigureImage(_baseImage, _previewSkillData.IconSprite, Image.Type.Simple, Color.white);

            if (_overlayImage != null)
            {
                ConfigureOverlayPanel(_cooldownOverlayColor);
                _overlayImage.fillAmount = 0f;
                _overlayImage.gameObject.SetActive(true);
            }

            if (_nameText != null)
                _nameText.text = _previewSkillData.DisplayName;

            if (_timerText != null)
            {
                _timerText.gameObject.SetActive(_previewSkillData.CooldownSeconds > 0f);
                _timerText.text = _previewSkillData.CooldownSeconds > 0f
                    ? Mathf.CeilToInt(_previewSkillData.CooldownSeconds).ToString()
                    : string.Empty;
            }

            if (_indexText != null)
                _indexText.text = string.Empty;
        }

        private void ResolveReferences()
        {
            if (_root == null)
                _root = GetComponent<RectTransform>();

            if (_baseImage == null || _overlayImage == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                foreach (Image image in images)
                {
                    if (_baseImage == null)
                    {
                        _baseImage = image;
                        continue;
                    }

                    if (_overlayImage == null && image != _baseImage)
                        _overlayImage = image;
                }
            }

            if (_nameText == null || _timerText == null || _indexText == null)
            {
                TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI text in texts)
                {
                    string lowerName = text.name.ToLowerInvariant();

                    if (_nameText == null && lowerName.Contains("name"))
                    {
                        _nameText = text;
                        continue;
                    }

                    if (_timerText == null && (lowerName.Contains("timer") || lowerName.Contains("cooldown")))
                    {
                        _timerText = text;
                        continue;
                    }

                    if (_indexText == null && lowerName.Contains("index"))
                    {
                        _indexText = text;
                    }
                }
            }
        }

        private RectTransform ResolveRoot()
        {
            return _root != null ? _root : GetComponent<RectTransform>();
        }

        private void ConfigureOverlayImage(SkillHudPhase phase)
        {
            if (_overlayImage == null)
                return;

            _overlayImage.type = Image.Type.Filled;
            if (_overlayImage.sprite == null)
                _overlayImage.sprite = GetRuntimeWhiteSprite();

            _overlayImage.fillMethod = Image.FillMethod.Radial360;
            _overlayImage.fillOrigin = (int)Image.Origin360.Top;
            _overlayImage.fillClockwise = phase == SkillHudPhase.Casting;
            _overlayImage.raycastTarget = false;
        }

        private Color OverlayColorForPhase(SkillHudPhase phase)
        {
            return phase == SkillHudPhase.Active || phase == SkillHudPhase.Casting
                ? _activeOverlayColor
                : _cooldownOverlayColor;
        }

        private void ArrangeLayers()
        {
            if (_baseImage != null && _overlayImage != null &&
                _baseImage.transform.parent == _overlayImage.transform.parent)
            {
                _baseImage.transform.SetAsFirstSibling();
                _overlayImage.transform.SetSiblingIndex(_baseImage.transform.GetSiblingIndex() + 1);
            }
            else if (_overlayImage != null)
            {
                Transform iconLayer = _baseImage != null ? _baseImage.transform : null;
                while (iconLayer != null && iconLayer.parent != _overlayImage.transform.parent) iconLayer = iconLayer.parent;
                if (iconLayer != null)
                { iconLayer.SetAsFirstSibling(); _overlayImage.transform.SetSiblingIndex(iconLayer.GetSiblingIndex() + 1); }
                else _overlayImage.transform.SetAsFirstSibling();
            }

            MoveTextAboveOverlay(_nameText);
            MoveTextAboveOverlay(_timerText);
            MoveTextAboveOverlay(_indexText);
        }

        private static void MoveTextAboveOverlay(TextMeshProUGUI text)
        {
            if (text != null)
                text.transform.SetAsLastSibling();
        }

        private static void ConfigureImage(Image image, Sprite sprite, Image.Type type, Color color)
        {
            if (image == null)
                return;

            image.sprite = sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
        }

        private void ConfigureOverlayPanel(Color color)
        {
            if (_overlayImage == null)
                return;

            if (_overlayImage.sprite == null)
                _overlayImage.sprite = GetRuntimeWhiteSprite();

            _overlayImage.type = Image.Type.Filled;
            _overlayImage.color = color;
            _overlayImage.raycastTarget = false;
        }

        private static Sprite _runtimeWhiteSprite;

        private static Sprite GetRuntimeWhiteSprite()
        {
            if (_runtimeWhiteSprite != null)
                return _runtimeWhiteSprite;

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            _runtimeWhiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _runtimeWhiteSprite.hideFlags = HideFlags.HideAndDontSave;
            return _runtimeWhiteSprite;
        }
    }
}
