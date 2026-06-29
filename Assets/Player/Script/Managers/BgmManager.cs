using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Audio
{
    public sealed class BgmManager : MonoBehaviour
    {
        private const string AutoObjectName = "[BGM Manager]";
        private const string ResourceBgmPath = "BGM/";
        private const string SettingsResourcePath = "BGM/BgmSettings";

        private static BgmManager _instance;

        [Header("Playback")]
        [SerializeField] [Range(0f, 1f)] private float _volume = 0.6f;
        [SerializeField] private float _fadeSeconds = 0.6f;
        [SerializeField] private bool _loopByDefault = true;

        [Header("Scene Clips")]
        [SerializeField] private BgmSettings _settings;
        [SerializeField] private AudioClip _defaultClip;

        private AudioSource _audioSource;
        private readonly Dictionary<string, BgmSettings.SceneBgm> _bgmsByScene = new Dictionary<string, BgmSettings.SceneBgm>(System.StringComparer.OrdinalIgnoreCase);
        private Coroutine _fadeRoutine;
        private string _currentSceneName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var go = new GameObject(AutoObjectName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BgmManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();

            LoadSettings();
            _audioSource.loop = _loopByDefault;
            _audioSource.playOnAwake = false;
            _audioSource.volume = 0f;

            RebuildSceneClipMap();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            PlayForScene(SceneManager.GetActiveScene().name);
        }

        private void OnValidate()
        {
            _fadeSeconds = Mathf.Max(0f, _fadeSeconds);
            ApplySettings();

            if (_audioSource != null && _fadeRoutine == null)
                _audioSource.volume = _volume;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PlayForScene(scene.name);
        }

        private void RebuildSceneClipMap()
        {
            _bgmsByScene.Clear();
            if (_settings == null || _settings.SceneBgms == null)
                return;

            for (int i = 0; i < _settings.SceneBgms.Length; i++)
            {
                BgmSettings.SceneBgm sceneBgm = _settings.SceneBgms[i];
                if (string.IsNullOrWhiteSpace(sceneBgm.SceneName) || sceneBgm.Clip == null)
                    continue;

                _bgmsByScene[sceneBgm.SceneName.Trim()] = sceneBgm;
            }
        }

        private void PlayForScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || _currentSceneName == sceneName)
                return;

            _currentSceneName = sceneName;
            AudioClip nextClip = ResolveClip(sceneName, out bool loop);
            if (nextClip == _audioSource.clip && _audioSource.loop == loop)
                return;

            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            _fadeRoutine = StartCoroutine(CoSwitchClip(nextClip, loop));
        }

        private void LoadSettings()
        {
            if (_settings == null)
                _settings = Resources.Load<BgmSettings>(SettingsResourcePath);

            ApplySettings();
        }

        private void ApplySettings()
        {
            if (_settings == null)
                return;

            _volume = Mathf.Clamp01(_settings.MasterVolume);
            _fadeSeconds = Mathf.Max(0f, _settings.FadeSeconds);
            _loopByDefault = _settings.LoopByDefault;
            _defaultClip = _settings.DefaultClip;
            RebuildSceneClipMap();
        }

        private AudioClip ResolveClip(string sceneName, out bool loop)
        {
            loop = _loopByDefault;

            if (_bgmsByScene.TryGetValue(sceneName, out BgmSettings.SceneBgm configuredBgm) && configuredBgm.Clip != null)
            {
                loop = configuredBgm.OverrideLoop ? configuredBgm.Loop : _loopByDefault;
                return configuredBgm.Clip;
            }

            AudioClip resourceClip = Resources.Load<AudioClip>(ResourceBgmPath + sceneName);
            if (resourceClip != null)
                return resourceClip;

            return _defaultClip != null ? _defaultClip : Resources.Load<AudioClip>(ResourceBgmPath + "Default");
        }

        private System.Collections.IEnumerator CoSwitchClip(AudioClip nextClip, bool loop)
        {
            float fadeOutStart = _audioSource.volume;
            float fadeDuration = Mathf.Max(0.01f, _fadeSeconds);

            for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
            {
                _audioSource.volume = Mathf.Lerp(fadeOutStart, 0f, t / fadeDuration);
                yield return null;
            }

            _audioSource.Stop();
            _audioSource.clip = nextClip;
            _audioSource.loop = loop;

            if (nextClip != null)
                _audioSource.Play();

            for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
            {
                _audioSource.volume = Mathf.Lerp(0f, _volume, t / fadeDuration);
                yield return null;
            }

            _audioSource.volume = nextClip == null ? 0f : _volume;
            _fadeRoutine = null;
        }
    }
}
