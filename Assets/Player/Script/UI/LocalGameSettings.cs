using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattlePvp.UI
{
    [Serializable]
    public sealed class LocalGameSettingsData
    {
        public int quality = 1, fps = 60;
        public float brightness = 1f, hudScale = 1f, hudOpacity = 1f, sensitivity = 1f;
        public float master = .65f, music = .45f, effects = .7f, ui = .45f;
        public bool muted, muteInBackground = true, invertY;
        public string skill1 = "q", skill2 = "e";
        public LocalGameSettingsData Copy() => (LocalGameSettingsData)MemberwiseClone();
        public void Sanitize()
        {
            quality = Mathf.Clamp(quality, 0, 2); fps = fps == 30 ? 30 : 60;
            brightness = Safe(brightness, .6f, 1.4f, 1f); hudScale = Safe(hudScale, .8f, 1.25f, 1f);
            hudOpacity = Safe(hudOpacity, .35f, 1f, 1f); sensitivity = Safe(sensitivity, .25f, 2f, 1f);
            master = Safe(master, 0, 1, .65f); music = Safe(music, 0, 1, .45f);
            effects = Safe(effects, 0, 1, .7f); ui = Safe(ui, 0, 1, .45f);
            if (!IsSkillKey(skill1)) skill1 = "q";
            if (!IsSkillKey(skill2) || skill2 == skill1) skill2 = skill1 == "e" ? "q" : "e";
        }
        public static readonly string[] SkillKeys = { "q", "e", "r", "f", "z", "x", "v", "1", "2", "3", "4", "5" };
        public static bool IsSkillKey(string value) => Array.IndexOf(SkillKeys, value) >= 0;
        private static float Safe(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    /// <summary>Device preferences only; these never alter authoritative player profiles.</summary>
    public sealed class LocalGameSettings : MonoBehaviour
    {
        private const string Key = "BattlePvp.DeviceSettings.v1";
        private static LocalGameSettingsData _current = new LocalGameSettingsData();
        public static LocalGameSettingsData Current => _current;
        public static event Action Changed;
        private static LocalGameSettings _instance;
        private AudioSource _uiSource;
        private AudioClip _click;
        private bool _focused = true;
        private int _soundFrame = -1;
        private UniversalRenderPipelineAsset _pipeline;
        private RenderPipelineAsset _originalQualityPipeline;
        private readonly WebFrameBudget _webBudget = new WebFrameBudget();
        public static bool IsWebPlayer => Application.platform == RuntimePlatform.WebGLPlayer;
        public static int EffectiveQuality => IsWebPlayer && _instance != null ? _instance._webBudget.Quality : _current.quality;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int _renderWidth, _renderHeight;
        private void Update()
        {
            if (_webBudget.Observe(Time.unscaledDeltaTime, _focused))
            {
                ApplyGraphics();
                Changed?.Invoke();
            }
            else if (_renderWidth != Screen.width || _renderHeight != Screen.height) ApplyRenderScale();
        }
#endif

        // Limit 3D pixel cost on high-DPI/fullscreen browsers. The UI keeps native resolution.
        private void ApplyRenderScale()
        {
            if (_pipeline == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            _renderWidth = Screen.width; _renderHeight = Screen.height;
            _pipeline.renderScale = _webBudget.RenderScale(_renderWidth, _renderHeight);
#else
            _pipeline.renderScale = _current.quality == 0 ? .75f : _current.quality == 1 ? .9f : 1f;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _current = new LocalGameSettingsData(); Changed = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("Device Settings");
            DontDestroyOnLoad(go); go.AddComponent<LocalGameSettings>();
        }
        private void Awake()
        {
            _instance = this;
            try { if (PlayerPrefs.HasKey(Key)) _current = JsonUtility.FromJson<LocalGameSettingsData>(PlayerPrefs.GetString(Key)) ?? new LocalGameSettingsData(); }
            catch (ArgumentException) { _current = new LocalGameSettingsData(); }
            _current.Sanitize();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                _originalQualityPipeline = QualitySettings.renderPipeline;
                _pipeline = Instantiate(pipeline);
                _pipeline.name = "Device Render Settings";
                QualitySettings.renderPipeline = _pipeline;
            }
            _uiSource = gameObject.AddComponent<AudioSource>();
            _uiSource.playOnAwake = false; _uiSource.spatialBlend = 0;
            _click = Resources.Load<AudioClip>("Remodel/ui-terminal-click");
            SceneManager.sceneLoaded += SceneLoaded;
            Apply(_current, false);
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (_pipeline != null)
            {
                if (QualitySettings.renderPipeline == _pipeline) QualitySettings.renderPipeline = _originalQualityPipeline;
                Destroy(_pipeline);
            }
            if (_instance == this) _instance = null;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _webBudget.ResetObservation();
            Apply(_current, false);
        }
        public static void Apply(LocalGameSettingsData settings, bool save)
        {
            _current = settings.Copy(); _current.Sanitize();
            BattlePvp.Networking.RoomNetworkTiming.ApplyFrameRate(_current.fps, BattlePvp.Networking.RoomNetworkTiming.LowLatencyActive);
            QualitySettings.vSyncCount = 0;
            _instance?._webBudget.Configure(_current.quality, _current.fps);
            ApplyGraphics();
            if (_instance != null) _instance.ApplyVolume();
            Changed?.Invoke();
            if (save) { PlayerPrefs.SetString(Key, JsonUtility.ToJson(_current)); PlayerPrefs.Save(); }
        }
        private static void ApplyGraphics()
        {
            int quality = EffectiveQuality;
            QualitySettings.shadows = quality == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = quality == 2 ? 55 : 35;
            QualitySettings.antiAliasing = quality == 2 ? 4 : quality == 1 ? 2 : 0;
            if (_instance == null || _instance._pipeline == null) return;
            var pipeline = _instance._pipeline;
            _instance.ApplyRenderScale();
            pipeline.msaaSampleCount = quality == 2 ? 4 : quality == 1 ? 2 : 1;
            pipeline.shadowDistance = quality == 0 ? 0 : quality == 1 ? 35 : 55;
            if (IsWebPlayer) ApplyWebPipeline(pipeline, quality);
        }

        internal static void ApplyWebPipeline(UniversalRenderPipelineAsset pipeline, int quality)
        {
            pipeline.msaaSampleCount = quality == 2 ? 2 : 1;
            pipeline.shadowDistance = quality == 0 ? 0 : quality == 1 ? 20 : 35;
            pipeline.shadowCascadeCount = 1;
            pipeline.mainLightShadowmapResolution = quality == 2 ? 1024 : 512;
            pipeline.maxAdditionalLightsCount = quality == 0 ? 0 : 2;
            pipeline.supportsHDR = quality > 0;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
        }
        private void OnApplicationFocus(bool focused) { _focused = focused; ApplyVolume(); }
        private void ApplyVolume() => AudioListener.volume = _current.muted || (!_focused && _current.muteInBackground) ? 0 : _current.master;
        public static void Click()
        {
            if (_instance == null || _instance._click == null || _instance._soundFrame == Time.frameCount) return;
            _instance._soundFrame = Time.frameCount;
            _instance._uiSource.Stop();
            _instance._uiSource.PlayOneShot(_instance._click, _current.ui);
        }
    }
}
