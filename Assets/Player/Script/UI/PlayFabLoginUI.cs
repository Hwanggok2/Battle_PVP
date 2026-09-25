using UnityEngine;
using TMPro;
using UnityEngine.UI;
using BattlePvp.Networking;
using UnityEngine.SceneManagement;

namespace BattlePvp.UI
{
    /// <summary>
    /// PlayFab 인증 화면을 제어하는 UI 매니저입니다.
    /// 인스펙터에서 InputField와 Button을 각각 연결해 주세요.
    /// </summary>
    public class PlayFabLoginUI : MonoBehaviour
    {
        [Header("Input Fields")]
        [SerializeField] private TMP_InputField _idInput;      // 사용자 아이디 (Username)
        [SerializeField] private TMP_InputField _emailInput;   // 이메일 (회원가입용 필수)
        [SerializeField] private TMP_InputField _pwInput;      // 비밀번호

        [Header("Buttons")]
        [SerializeField] private Button _loginButton;
        [SerializeField] private Button _registerButton;

        [Header("Status Feedback")]
        [SerializeField] private TextMeshProUGUI _statusText;  // 결과 메시지 표시용 텍스트
        private PlayFabAuthManager _authService;
        private readonly LoginNavigationGate _navigation = new LoginNavigationGate();
        private bool _waitingForResult;

        private void Awake()
        {
            // Scene editing visibility must not expose an old status at runtime.
            SetStatus(string.Empty);
            if (_emailInput != null) _emailInput.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            _navigation.Activate();
            _waitingForResult = false;
            if (IsLoginScene())
                EnsureLoginCursorVisible();
            if (_loginButton != null) _loginButton.onClick.AddListener(OnLoginClicked);
            if (_registerButton != null) _registerButton.onClick.AddListener(OnRegisterClicked);
            PlayFabAuthManager.InstanceChanged += BindAuthService;
            BindAuthService(PlayFabAuthManager.Instance);
        }

        private void Update()
        {
            if (IsLoginScene() && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
                EnsureLoginCursorVisible();

            bool pending = _navigation.IsPending;
            if (_navigation.TryConsume(_authService != null ? _authService.ApprovedLoginRequestId : 0,
                Time.realtimeSinceStartupAsDouble))
            {
                LoadMainScene();
                return;
            }
            if (pending && !_navigation.IsPending) UpdateButtons();
        }

        private void OnDisable()
        {
            PlayFabAuthManager.InstanceChanged -= BindAuthService;
            if (_loginButton != null) _loginButton.onClick.RemoveListener(OnLoginClicked);
            if (_registerButton != null) _registerButton.onClick.RemoveListener(OnRegisterClicked);
            _navigation.Deactivate();
            UnbindAuthService();
            if (_waitingForResult && _authService != null) _authService.CancelPendingAuthentication();
            _waitingForResult = false;
            _authService = null;
        }

        private void BindAuthService(PlayFabAuthManager service)
        {
            if (_authService == service) { UpdateButtons(); return; }
            UnbindAuthService();
            if (_waitingForResult && _authService != null) _authService.CancelPendingAuthentication();
            _authService = service;
            _waitingForResult = service != null && service.IsBusy;
            _navigation.Activate();
            if (service != null)
            {
                service.OnLoginSuccess += HandleLoginSuccess;
                service.OnLoginFailure += HandleFailure;
                service.OnRegisterSuccess += HandleRegisterSuccess;
                service.OnRegisterFailure += HandleFailure;
                service.OnAuthenticationStateChanged += HandleAuthenticationState;
            }
            UpdateButtons();
        }

        private void UnbindAuthService()
        {
            if (_authService == null) return;
            _authService.OnLoginSuccess -= HandleLoginSuccess;
            _authService.OnLoginFailure -= HandleFailure;
            _authService.OnRegisterSuccess -= HandleRegisterSuccess;
            _authService.OnRegisterFailure -= HandleFailure;
            _authService.OnAuthenticationStateChanged -= HandleAuthenticationState;
        }

        private void HandleAuthenticationState(bool busy)
        {
            if (!isActiveAndEnabled) return;
            _waitingForResult = busy;
            if (busy) _navigation.Activate();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool canSubmit = _authService != null && _authService.isActiveAndEnabled && !_authService.IsBusy && !_waitingForResult && !_navigation.IsPending;
            if (_loginButton != null) _loginButton.interactable = canSubmit;
            if (_registerButton != null) _registerButton.interactable = canSubmit;
        }

        private bool CanSubmit() => isActiveAndEnabled && _authService != null && _authService.isActiveAndEnabled &&
            !_authService.IsBusy && !_waitingForResult && !_navigation.IsPending;

        private static void EnsureLoginCursorVisible()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool IsLoginScene()
        {
            return SceneManager.GetActiveScene().name == "Login";
        }

        private void OnLoginClicked()
        {
            if (!CanSubmit()) return;
            if (_idInput == null || _pwInput == null || string.IsNullOrWhiteSpace(_idInput.text) || string.IsNullOrEmpty(_pwInput.text))
            {
                SetStatus("<color=yellow>아이디와 비밀번호를 모두 입력하세요.</color>");
                return;
            }

            SetStatus("로그인 중...");
            _waitingForResult = true;
            UpdateButtons();
            _authService.Login(_idInput.text, _pwInput.text);
        }

        private void OnRegisterClicked()
        {
            if (!CanSubmit()) return;
            if (_idInput == null || _pwInput == null || string.IsNullOrWhiteSpace(_idInput.text) || string.IsNullOrEmpty(_pwInput.text))
            {
                SetStatus("<color=yellow>아이디와 비밀번호를 모두 입력하세요.</color>");
                return;
            }

            // 비밀번호 길이 체크 (서버 가기 전 한 번 더!)
            if (_pwInput.text.Length < 6)
            {
                SetStatus("<color=yellow>비밀번호는 최소 6자 이상이어야 합니다.</color>");
                return;
            }

            // 이메일이 없다면 자동으로 아이디 기반 이메일 생성
            string email = _emailInput != null && !string.IsNullOrEmpty(_emailInput.text) 
                ? _emailInput.text 
                : $"{_idInput.text}@test.com";

            SetStatus("회원가입 시도 중...");
            _waitingForResult = true;
            UpdateButtons();
            _authService.Register(_idInput.text, email, _pwInput.text);
        }

        private void HandleLoginSuccess()
        {
            if (!isActiveAndEnabled || !_waitingForResult || _authService == null ||
                !_navigation.Schedule(_authService.ApprovedLoginRequestId, Time.realtimeSinceStartupAsDouble)) return;
            _waitingForResult = false;
            SetStatus("<color=green>로그인 성공! 잠시 후 이동합니다.</color>");
            UpdateButtons();
        }

        private void LoadMainScene()
        {
            // 실제 로비 씬 이름이 만약 다르면 여기서 이름을 수정해 주세요.
            SceneManager.LoadScene("Lobby");
        }

        private void HandleRegisterSuccess()
        {
            if (!isActiveAndEnabled || !_waitingForResult) return;
            _waitingForResult = false;
            SetStatus("<color=blue>회원가입 성공! 이제 로그인해 주세요.</color>");
            UpdateButtons();
        }

        private void HandleFailure(string errorMessage)
        {
            if (!isActiveAndEnabled || !_waitingForResult) return;
            _waitingForResult = false;
            SetStatus($"<color=red>오류: {errorMessage}</color>");
            UpdateButtons();
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }
        }
    }
}
