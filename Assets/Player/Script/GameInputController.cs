using UnityEngine;
using BattlePvp.CameraLogic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using BattlePvp.Combat;
using BattlePvp.Networking;
using Mirror;

namespace BattlePvp.Logic
{
    /// <summary>
    /// [Core Fix] 전역 입력 및 마우스 커서/카메라 토글 관리 매니저.
    /// Lobby와 Battle 씬 모두에서 사용 가능하며, ESC를 통해 상태를 전환합니다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameInputController : MonoBehaviour
    {
        // 전역에서 접근 가능한 일시정지(메뉴) 상태
        private static bool _paused;
        private static bool _textInputActive;
        private static int _textInputConsumedFrame = -1;
        public static bool IsPaused { get => _paused || HasModalInput; private set => _paused = value; }
        public static bool IsTextInputActive
        {
            get => _textInputActive || HasFocusedTextInput || _textInputConsumedFrame == Time.frameCount;
            private set => _textInputActive = value;
        }
        private static bool HasModalInput => BattlePvp.UI.JobGuidePanel.IsOpen || BattlePvp.UI.RoomPasswordPrompt.IsOpen || BattlePvp.UI.WaitingRoomTerminal.IsOpen || BattlePvp.UI.GameSettingsPanel.IsOpen ||
            BattlePvp.UI.CharacterInfoController.HasOpenPanel ||
            (BattlePvp.UI.LobbyUIManager.Instance != null && BattlePvp.UI.LobbyUIManager.Instance.HasOpenInputPanel);
        private static bool HasFocusedTextInput
        {
            get
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null || !selected.activeInHierarchy) return false;
                var tmp = selected.GetComponent<TMPro.TMP_InputField>();
                var legacy = selected.GetComponent<InputField>();
                return (tmp != null && tmp.isFocused) || (legacy != null && legacy.isFocused);
            }
        }
        public static event System.Action TextInputCancelled;
        public static GameInputMode CurrentMode { get; private set; }
        private static readonly FrameInputGate InputGate = new FrameInputGate();
        public static bool IsSubmitConsumedThisFrame => InputGate.IsSubmitConsumed(Time.frameCount);
        public static void ConsumeSubmit() => InputGate.ConsumeSubmit(Time.frameCount);

        private FollowCamera _followCamera;
        private bool _isCursorUnlocked = false;
        [SerializeField] private TMPro.TMP_Text _cursorHint;
        private NetworkIdentity _localIdentity;
        private HealthSystem _localHealth;
        private PlayerManager _localMovement;
        private static bool IsWebGlRuntime => Application.platform == RuntimePlatform.WebGLPlayer;

        public static GameInputController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _followCamera = FindFirstObjectByType<FollowCamera>();
            // 씬 진입 시마다 초기화 (Lobby에서 공격이 안 되는 현상 방지)
            IsPaused = false;
            IsTextInputActive = false;
            _isCursorUnlocked = false;
            InputGate.Clear();
            _textInputConsumedFrame = -1;
            CurrentMode = GameInputMode.Gameplay;
        }

        /// <summary>
        /// 게임 플레이 입력을 복구하며, 커서는 현재 씬의 잠금 정책을 따릅니다.
        /// </summary>
        public void ResetToPlayMode()
        {
            if (IsTextInputActive) TextInputCancelled?.Invoke();
            IsTextInputActive = false;
            _isCursorUnlocked = false;
            IsPaused = false;
            ClearSelectedUiIfNotTextInput();
            ApplyCursorState();
        }

        public static void SetTextInputActive(bool isActive)
        {
            if (_textInputActive == isActive) return;
            if (!isActive) _textInputConsumedFrame = Time.frameCount;
            IsTextInputActive = isActive;

            if (!isActive)
                ClearSelectedUiIfNotTextInput();

            if (Instance != null)
                Instance.ApplyCursorState();
            else if (isActive)
            {
                ReleaseCursor();
            }
        }

        private void OnDisable()
        {
            if (Instance != this) return;
            // 오브젝트가 사라지거나 씬이 바뀔 때 상태 초기화
            IsPaused = false;
            IsTextInputActive = false;
            ReleaseCursor();
            CurrentMode = GameInputMode.Gameplay;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void RefreshCursorState()
        {
            if (Instance != null && Instance.isActiveAndEnabled)
                Instance.ApplyCursorState();
            else if (!InputModeRules.CanLockCursor(SceneManager.GetActiveScene().name, GameInputMode.Gameplay))
                ReleaseCursor();
        }

        public static void HandleEscape()
        {
            if (!InputGate.TryConsumeEscape(Time.frameCount)) return;
            if (BattlePvp.UI.JobGuidePanel.IsOpen) { BattlePvp.UI.JobGuidePanel.Instance.Close(); Instance?.ResetToPlayMode(); return; }
            if (BattlePvp.UI.RoomPasswordPrompt.IsOpen) { BattlePvp.UI.RoomPasswordPrompt.Instance.Close(); Instance?.ResetToPlayMode(); return; }
            if (BattlePvp.UI.GameSettingsPanel.IsOpen) { BattlePvp.UI.GameSettingsPanel.Instance.Cancel(); Instance?.ResetToPlayMode(); return; }
            if (BattlePvp.UI.WaitingRoomTerminal.IsOpen) { BattlePvp.UI.WaitingRoomTerminal.Instance.Close(); Instance?.ResetToPlayMode(); return; }
            if (BattlePvp.UI.CharacterInfoController.CloseOpenPanel()) { Instance?.ResetToPlayMode(); return; }
            if (IsTextInputActive)
            {
                TextInputCancelled?.Invoke();
                if (HasFocusedTextInput) EventSystem.current.SetSelectedGameObject(null);
                SetTextInputActive(false);
                return;
            }
            if (Instance == null) return;
            if (BattlePvp.UI.LobbyUIManager.Instance != null && BattlePvp.UI.LobbyUIManager.Instance.CloseInputPanels()) { Instance.ResetToPlayMode(); return; }
            Instance.ApplyCursorState();
            Instance.ToggleCursorMode();
        }

        private void Start()
        {
            CheckSceneDependencies();
            ApplyCursorState(); // 시작 시 커서 상태 적용
        }

        private void Update()
        {
            ApplyCursorState();
            var keyboard = Keyboard.current;
            if (IsTextInputActive && keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                ConsumeSubmit();
            if (!IsTextInputActive && keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                ClearSelectedUiIfNotTextInput();
                BattleStateMachine.Instance?.RequestRestartFromInput();
            }

            bool pointerPressed = !IsTextInputActive && !HasModalInput && Mouse.current != null &&
                                  (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame);
            if (pointerPressed)
            {
                ClearSelectedUiIfNotTextInput();

                if (IsWebGlRuntime && CanLockCursorInCurrentScene() &&
                    Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleEscape();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 포커스를 다시 얻었을 때 현재 설정된 상태 다시 적용 (Task 1)
            if (hasFocus)
            {
                ApplyCursorState();
            }
        }

        private void ToggleCursor()
        {
            _isCursorUnlocked = !_isCursorUnlocked;
            IsPaused = _isCursorUnlocked;

            ApplyCursorState();

            if (_isCursorUnlocked || IsTextInputActive)
                Debug.Log("[GameInput] Pause Mode: Cursor Free, Camera Fixed, Attack Disabled");
            else
                Debug.Log("[GameInput] Play Mode: Camera Active, Attack Enabled, Cursor Follows Scene Policy");
        }

        public void ToggleCursorMode()
        {
            if (IsTextInputActive || BattlePvp.UI.GameSettingsPanel.IsOpen ||
                !InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name) ||
                !InputModeRules.CanToggleMenu(CurrentMode)) return;
            if (_isCursorUnlocked || HasModalInput)
            {
                BattlePvp.UI.JobGuidePanel.CloseIfOpen();
                if (BattlePvp.UI.WaitingRoomTerminal.IsOpen) BattlePvp.UI.WaitingRoomTerminal.Instance.Close();
                BattlePvp.UI.CharacterInfoController.CloseOpenPanel();
                if (BattlePvp.UI.LobbyUIManager.Instance != null) BattlePvp.UI.LobbyUIManager.Instance.CloseInputPanels();
                ResetToPlayMode();
            }
            else ToggleCursor();
        }

        private void ApplyCursorState()
        {
            if (_localIdentity != NetworkClient.localPlayer)
            {
                _localIdentity = NetworkClient.localPlayer;
                _localHealth = _localIdentity != null ? _localIdentity.GetComponent<HealthSystem>() : null;
                _localMovement = _localIdentity != null ? _localIdentity.GetComponent<PlayerManager>() : null;
            }
            CurrentMode = InputModeRules.Resolve(IsTextInputActive, _isCursorUnlocked || HasModalInput,
                _localHealth != null && _localHealth.IsDead,
                _localMovement != null && _localMovement.IsMatchEndLocked,
                BattleStateMachine.Instance != null && BattleStateMachine.Instance.IsResultPanelVisible);
            IsPaused = CurrentMode != GameInputMode.Gameplay && CurrentMode != GameInputMode.TextInput;
            if (!CanLockCursorInCurrentScene())
            {
                ReleaseCursor();
            }
            else if (!IsWebGlRuntime)
            {
                ClearSelectedUiIfNotTextInput();
                if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
                if (Cursor.visible) Cursor.visible = false;
            }
            else
            {
                ClearSelectedUiIfNotTextInput();
                // WebGL 잠금은 전투 씬의 사용자 클릭 경로에서만 요청한다.
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }
            if (_followCamera == null) _followCamera = FindFirstObjectByType<FollowCamera>();
            if (_followCamera != null) _followCamera.IsLocked = CurrentMode != GameInputMode.Gameplay;
            if (_cursorHint != null)
            {
                string hint = BattlePvp.UI.GameSettingsPanel.IsOpen ? "Esc 설정 닫기" :
                    _isCursorUnlocked ? "커서 모드  ·  Esc 조작 복귀" : "Esc 커서  ·  Enter 채팅";
                if (_cursorHint.text != hint) _cursorHint.text = hint;
            }
        }

        private static bool CanLockCursorInCurrentScene() =>
            InputModeRules.CanLockCursor(SceneManager.GetActiveScene().name, CurrentMode);

        private static void ReleaseCursor()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        /// <summary>
        /// Task 4: UI 상호작용 및 씬 필수 요소 체크
        /// </summary>
        private void CheckSceneDependencies()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                Debug.LogWarning("[GameInput] EventSystem을 동적으로 생성했습니다.");
            }

            // Only interactive canvases need a raycaster; HUD overlays are display-only.
            var controls = FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var control in controls)
            {
                Canvas canvas = control.GetComponentInParent<Canvas>(true);
                if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log($"[GameInput] '{canvas.name}' 에 GraphicRaycaster 를 추가했습니다.");
                }
            }
        }

        private static void ClearSelectedUiIfNotTextInput()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected.GetComponent<TMPro.TMP_InputField>() != null ||
                selected.GetComponent<InputField>() != null)
            {
                return;
            }

            eventSystem.SetSelectedGameObject(null);
        }
    }
}
