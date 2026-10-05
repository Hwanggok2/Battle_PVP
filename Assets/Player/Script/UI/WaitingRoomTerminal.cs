using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Networking;
using BattlePvp.Stats;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DefaultExecutionOrder(-60)]
    public sealed class WaitingRoomTerminal : MonoBehaviour
    {
        public static WaitingRoomTerminal Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeInHierarchy;
        [SerializeField] private GameObject _panel;
        [SerializeField] private GameObject _prompt;
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private Image _progress;
        [SerializeField] private TMP_Text _roomSummary;
        [SerializeField] private TMP_Text _durationLabel;
        [SerializeField] private Button _durationButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Transform _focusPoint;
        private Camera _camera;
        private StatManager _local;
        private HealthSystem _health;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly TerminalHold _hold = new TerminalHold();
        private bool _focused, _touchHeld;

        private void Awake()
        {
            Instance = this;
            _panel.SetActive(false); _prompt.SetActive(false);
            _durationButton.onClick.AddListener(ChangeDuration);
            _closeButton.onClick.AddListener(Close);
        }
        private void OnEnable() { StatManager.LocalChanged += Bind; Bind(StatManager.Local); }
        private void OnDisable()
        {
            StatManager.LocalChanged -= Bind; _touchHeld = _focused = false;
            _hold.Step(false, false, 0);
            if (_panel != null) _panel.SetActive(false);
            if (_prompt != null) _prompt.SetActive(false);
            GameInputController.RefreshCursorState();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void Bind(StatManager stats) { _local = stats; _health = stats != null ? stats.GetComponent<HealthSystem>() : null; }

        public static bool ConsumesSkillInput(int slot)
        {
            if (Instance == null || !Instance.isActiveAndEnabled || !Instance.HasFocus()) return false;
            string key = slot == 0 ? LocalGameSettings.Current.skill1 : LocalGameSettings.Current.skill2;
            return key == "e";
        }

        private bool HasFocus()
        {
            if (_local == null || (_health != null && _health.IsDead) || GameInputController.IsPaused ||
                GameInputController.IsTextInputActive || GameInputController.CurrentMode != GameInputMode.Gameplay) return false;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || Vector3.Distance(_camera.transform.position, _focusPoint.position) > 3.4f) return false;
            var ray = new Ray(_camera.transform.position, _camera.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, _hits, 3.4f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; Collider candidate = null;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(_local.transform) || _hits[i].distance >= nearest) continue;
                candidate = _hits[i].collider; nearest = _hits[i].distance;
            }
            return candidate != null && candidate.GetComponentInParent<WaitingRoomTerminal>() == this;
        }
        private void Update()
        {
            if (IsOpen)
            {
                if (_local == null || (_health != null && _health.IsDead)) { Close(); return; }
                RefreshSettings(); return;
            }
            _focused = HasFocus();
            _prompt.SetActive(_focused);
            bool held = _touchHeld || (Keyboard.current != null && Keyboard.current.eKey.isPressed);
            bool completed = _hold.Step(_focused, held, Time.unscaledDeltaTime);
            _progress.fillAmount = _hold.Progress;
            if (_focused) _promptText.text = _hold.Progress > 0 ? "단말 연결 중" : "E 길게 누르기 · 전술 단말";
            if (completed) Open();
        }
        public void SetTouchHeld(bool held) => _touchHeld = held;
        private void Open()
        {
            JobGuidePanel.CloseIfOpen();
            _touchHeld = false; _prompt.SetActive(false);
            _panel.SetActive(true); RefreshSettings();
            GameInputController.RefreshCursorState();
            LocalGameSettings.Click();
        }
        public void Close()
        {
            _touchHeld = false; _panel.SetActive(false);
            // Closing during the local host countdown cancels it through BattleStartController.OnDisable.
            GameInputController.Instance?.ResetToPlayMode();
        }
        private void ChangeDuration()
        {
            var settings = BattleMapSelection.Instance;
            if (!NetworkServer.active || settings == null || BattleStartController.IsStarting) return;
            settings.SetMatchDuration(settings.MatchSeconds == 180 ? 300 : settings.MatchSeconds == 300 ? 600 : 180);
            RefreshSettings();
        }
        private void RefreshSettings()
        {
            var settings = BattleMapSelection.Instance;
            _durationButton.interactable = NetworkServer.active && settings != null && !BattleStartController.IsStarting;
            _durationLabel.text = $"경기 시간 · {(settings != null ? settings.MatchSeconds : 180) / 60}분";
            UserTextPresentation.SetPlain(_roomSummary, NetworkServer.active
                ? "설정 저장 후 출격하세요. 닫으면 시작 준비가 취소됩니다."
                : "방장이 전장과 입장 조건을 설정합니다.");
        }
    }

    public sealed class TerminalHold
    {
        private float _elapsed;
        private bool _completed;
        public float Progress => Mathf.Clamp01(_elapsed / .75f);
        public bool Step(bool eligible, bool held, float delta)
        {
            if (!held) { _elapsed = 0; _completed = false; return false; }
            if (!eligible) { _elapsed = 0; return false; }
            if (_completed) return false;
            _elapsed += Mathf.Max(0, delta);
            if (_elapsed < .75f) return false;
            _completed = true; return true;
        }
    }
}
