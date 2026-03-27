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

        private void Start()
        {
            // 버튼 클릭 시 실행될 함수 등록
            if (_loginButton != null) _loginButton.onClick.AddListener(OnLoginClicked);
            if (_registerButton != null) _registerButton.onClick.AddListener(OnRegisterClicked);

            // PlayFabAuthManager 이벤트 구독
            if (PlayFabAuthManager.Instance != null)
            {
                PlayFabAuthManager.Instance.OnLoginSuccess += HandleLoginSuccess;
                PlayFabAuthManager.Instance.OnLoginFailure += HandleFailure;
                PlayFabAuthManager.Instance.OnRegisterSuccess += HandleRegisterSuccess;
                PlayFabAuthManager.Instance.OnRegisterFailure += HandleFailure;
            }
        }

        private void OnDestroy()
        {
            // 이벤트 구독 해제 (메모리 누수 방지)
            if (PlayFabAuthManager.Instance != null)
            {
                PlayFabAuthManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
                PlayFabAuthManager.Instance.OnLoginFailure -= HandleFailure;
                PlayFabAuthManager.Instance.OnRegisterSuccess -= HandleRegisterSuccess;
                PlayFabAuthManager.Instance.OnRegisterFailure -= HandleFailure;
            }
        }

        private void OnLoginClicked()
        {
            if (string.IsNullOrEmpty(_idInput.text) || string.IsNullOrEmpty(_pwInput.text))
            {
                SetStatus("<color=yellow>아이디와 비밀번호를 모두 입력하세요.</color>");
                return;
            }

            SetStatus("로그인 중...");
            PlayFabAuthManager.Instance.Login(_idInput.text, _pwInput.text);
        }

        private void OnRegisterClicked()
        {
            if (string.IsNullOrEmpty(_idInput.text) || string.IsNullOrEmpty(_pwInput.text))
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
            PlayFabAuthManager.Instance.Register(_idInput.text, email, _pwInput.text);
        }

        private void HandleLoginSuccess()
        {
            SetStatus("<color=green>로그인 성공! 잠시 후 이동합니다.</color>");
            
            // 로그인 성공 시 로비(Main) 씬으로 이동합니다.
            // 씬 매니저 설정(Build Settings)에서 'Main' 씬이 추가되어 있어야 합니다.
            Invoke("LoadMainScene", 1.2f);
        }

        private void LoadMainScene()
        {
            // 실제 로비 씬 이름이 만약 다르면 여기서 이름을 수정해 주세요.
            SceneManager.LoadScene("Main");
        }

        private void HandleRegisterSuccess()
        {
            SetStatus("<color=blue>회원가입 성공! 이제 로그인해 주세요.</color>");
        }

        private void HandleFailure(string errorMessage)
        {
            SetStatus($"<color=red>오류: {errorMessage}</color>");
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
            Debug.Log($"[LoginUI] {message}");
        }
    }
}
