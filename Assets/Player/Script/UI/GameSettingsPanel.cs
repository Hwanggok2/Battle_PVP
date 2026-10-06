using System;
using System.Collections.Generic;
using BattlePvp.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DefaultExecutionOrder(-80)]
    public sealed class GameSettingsPanel : MonoBehaviour
    {
        public static GameSettingsPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeSelf;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _openButton;
        private LocalGameSettingsData _saved, _draft;
        private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private readonly Dictionary<string, TMP_Text> _values = new Dictionary<string, TMP_Text>();
        private TMP_Text _notice;
        private bool _refreshing;
        private int _listening;
        private bool _quitting;

        private void Awake()
        {
            Instance = this; _panel.SetActive(false);
            _openButton.onClick.AddListener(Toggle);
            foreach (var slider in _panel.GetComponentsInChildren<Slider>(true)) _sliders[slider.name] = slider;
            foreach (var button in _panel.GetComponentsInChildren<Button>(true)) _buttons[button.name] = button;
            foreach (var text in _panel.GetComponentsInChildren<TMP_Text>(true)) if (text.name.EndsWith("Value")) _values[text.name] = text;
            _notice = _panel.transform.Find("Notice").GetComponent<TMP_Text>();
            BindSlider("Brightness", v => _draft.brightness = v);
            BindSlider("HudScale", v => _draft.hudScale = v);
            BindSlider("HudOpacity", v => _draft.hudOpacity = v);
            BindSlider("Master", v => _draft.master = v);
            BindSlider("Music", v => _draft.music = v);
            BindSlider("Effects", v => _draft.effects = v);
            BindSlider("Ui", v => _draft.ui = v);
            BindSlider("Sensitivity", v => _draft.sensitivity = v);
            Bind("Quality", () => _draft.quality = (_draft.quality + 1) % 3);
            Bind("Fps", () => _draft.fps = _draft.fps == 60 ? 30 : 60);
            Bind("Mute", () => _draft.muted = !_draft.muted);
            Bind("BackgroundMute", () => _draft.muteInBackground = !_draft.muteInBackground);
            Bind("InvertY", () => _draft.invertY = !_draft.invertY);
            _buttons["Key1"].onClick.AddListener(() => Listen(1));
            _buttons["Key2"].onClick.AddListener(() => Listen(2));
            _buttons["Fullscreen"].onClick.AddListener(() => Screen.fullScreen = !Screen.fullScreen);
            _buttons["SoundTest"].onClick.AddListener(LocalGameSettings.Click);
            _buttons["Cancel"].onClick.AddListener(Cancel);
            _buttons["Close"].onClick.AddListener(Cancel);
            _buttons["Apply"].onClick.AddListener(Save);
            _buttons["Defaults"].onClick.AddListener(() => { _draft = new LocalGameSettingsData(); Preview(); });
            if (_buttons.TryGetValue("Quit", out var quit)) quit.onClick.AddListener(QuitGame);
            foreach (string tab in new[] { "System", "Sound", "Controls" })
            {
                string page = tab; _buttons[tab + "Tab"].onClick.AddListener(() => ShowTab(page));
            }
        }
        private void OnDestroy() { if (Instance == this) { if (IsOpen && _saved != null) LocalGameSettings.Apply(_saved, false); Instance = null; } }
        private void BindSlider(string name, Action<float> write) => _sliders[name].onValueChanged.AddListener(v => { if (_refreshing || _draft == null) return; write(v); Preview(); });
        private void Bind(string name, Action write) => _buttons[name].onClick.AddListener(() => { write(); Preview(); });
        public void Toggle() { if (IsOpen) Cancel(); else Open(); }
        public void Open()
        {
            if (IsOpen || _quitting) return;
            CharacterSelectionPanel.CloseIfOpen();
            JobGuidePanel.CloseIfOpen();
            CharacterInfoController.CloseOpenPanel();
            if (LobbyUIManager.Instance != null) LobbyUIManager.Instance.CloseInputPanels();
            _saved = LocalGameSettings.Current.Copy(); _draft = _saved.Copy(); _listening = 0;
            _panel.SetActive(true); _notice.text = string.Empty; ShowTab("System"); Refresh();
            GameInputController.RefreshCursorState();
        }
        public void Cancel() { if (_quitting) return; if (_saved != null) LocalGameSettings.Apply(_saved, false); Close(); }
        private void Save() { LocalGameSettings.Apply(_draft, true); Close(); }
        private void Close()
        {
            _listening = 0; _panel.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            GameInputController.RefreshCursorState();
        }
        private void Preview() { LocalGameSettings.Apply(_draft, false); Refresh(); }
        private void ShowTab(string name)
        {
            _listening = 0;
            foreach (string tab in new[] { "System", "Sound", "Controls" }) _panel.transform.Find(tab).gameObject.SetActive(tab == name);
        }
        private void SetValue(string name, float value) { _sliders[name].SetValueWithoutNotify(value); _values[name + "Value"].text = Mathf.RoundToInt(value * 100) + "%"; }
        private void Label(string name, string text) => _buttons[name].GetComponentInChildren<TMP_Text>().text = text;
        private void Refresh()
        {
            _refreshing = true;
            SetValue("Brightness", _draft.brightness); SetValue("HudScale", _draft.hudScale); SetValue("HudOpacity", _draft.hudOpacity);
            SetValue("Master", _draft.master); SetValue("Music", _draft.music); SetValue("Effects", _draft.effects); SetValue("Ui", _draft.ui); SetValue("Sensitivity", _draft.sensitivity);
            Label("Quality", new[] { "낮음", "보통", "높음" }[_draft.quality]); Label("Fps", _draft.fps + " FPS");
            Label("Mute", _draft.muted ? "켜짐" : "꺼짐"); Label("BackgroundMute", _draft.muteInBackground ? "켜짐" : "꺼짐"); Label("InvertY", _draft.invertY ? "켜짐" : "꺼짐");
            Label("Key1", _draft.skill1.ToUpperInvariant()); Label("Key2", _draft.skill2.ToUpperInvariant());
            _refreshing = false;
        }
        private void Listen(int slot) { _listening = slot; _notice.text = "새 키를 누르세요 · Q E R F Z X V / 1~5"; }
        public async void QuitGame()
        {
            if (_quitting) return;
            _quitting = true;
            _notice.text = "게임을 종료하는 중…";
            foreach (var button in _buttons.Values) button.interactable = false;
            if (_saved != null) LocalGameSettings.Apply(_saved, false);
            try
            {
                var rooms = BattlePvp.Networking.PlayFabBattleManager.Instance;
                if (rooms != null) await rooms.LeaveBeforeApplicationQuitAsync();
            }
            catch (Exception) { Debug.LogWarning("[Settings] Room cleanup was not confirmed before exit."); }
            finally
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
        private void Update()
        {
            if (!IsOpen || _listening == 0 || Keyboard.current == null) return;
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame || key.keyCode == Key.Escape) continue;
                string value = key.displayName.ToLowerInvariant();
                string other = _listening == 1 ? _draft.skill2 : _draft.skill1;
                if (!LocalGameSettingsData.IsSkillKey(value) || value == other) { _notice.text = "사용할 수 없는 키입니다. 다른 키를 누르세요."; return; }
                if (_listening == 1) _draft.skill1 = value; else _draft.skill2 = value;
                _listening = 0; _notice.text = "키가 변경되었습니다. 적용을 눌러 저장하세요."; Preview(); return;
            }
        }
    }
}
