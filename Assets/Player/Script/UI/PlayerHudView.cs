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

            if (_hpText != null)
                _hpText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
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

            ResolveSkillUI();
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
            if (_skillUI != null)
                return;

            _skillUI = FindFirstObjectByType<SkillUI>(FindObjectsInactive.Include);
        }
    }
}
