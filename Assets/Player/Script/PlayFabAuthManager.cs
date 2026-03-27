using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using System;

namespace BattlePvp.Networking
{
    /// <summary>
    /// PlayFab 인증(로그인/회원가입)을 담당하는 싱글톤 매니저입니다.
    /// </summary>
    public class PlayFabAuthManager : MonoBehaviour
    {
        public static PlayFabAuthManager Instance { get; private set; }

        [Header("Settings")]
        public string TitleId = "133DF7"; // 사용자님의 Title ID

        // 인증 결과 이벤트
        public event Action OnLoginSuccess;
        public event Action<string> OnLoginFailure;
        public event Action OnRegisterSuccess;
        public event Action<string> OnRegisterFailure;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                // SDK 설정을 코드에서도 확실히 보정합니다.
                if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
                {
                    PlayFabSettings.staticSettings.TitleId = TitleId;
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 새로운 계정을 생성합니다. (사용자명, 이메일, 비밀번호)
        /// </summary>
        public void Register(string username, string email, string password)
        {
            var request = new RegisterPlayFabUserRequest
            {
                Username = username,
                Email = email,
                Password = password,
                DisplayName = username // 처음 가입 시 닉네임을 유저명과 동일하게 설정
            };

            PlayFabClientAPI.RegisterPlayFabUser(request, 
                result => {
                    Debug.Log("회원가입 성공!");
                    OnRegisterSuccess?.Invoke();
                }, 
                error => {
                    Debug.LogError($"회원가입 실패: {error.GenerateErrorReport()}");
                    OnRegisterFailure?.Invoke(error.ErrorMessage);
                }
            );
        }

        /// <summary>
        /// 기존 계정으로 로그인합니다. (사용자명, 비밀번호)
        /// </summary>
        public void Login(string username, string password)
        {
            var request = new LoginWithPlayFabRequest
            {
                Username = username,
                Password = password
            };

            PlayFabClientAPI.LoginWithPlayFab(request, 
                result => {
                    Debug.Log("로그인 성공!");
                    OnLoginSuccess?.Invoke();
                    
                    // 로그인 성공 시 추가 유저 데이터 로드 로직을 여기서 실행할 수 있습니다.
                }, 
                error => {
                    Debug.LogError($"로그인 실패: {error.GenerateErrorReport()}");
                    OnLoginFailure?.Invoke(error.ErrorMessage);
                }
            );
        }

        /// <summary>
        /// 현재 로그인 여부를 확인합니다.
        /// </summary>
        public bool IsLoggedIn() => PlayFabClientAPI.IsClientLoggedIn();
    }
}
