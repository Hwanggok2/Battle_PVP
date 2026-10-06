using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using System;
using BattlePvp.Managers;

namespace BattlePvp.Networking
{
    /// <summary>
    /// PlayFab 인증(로그인/회원가입)을 담당하는 싱글톤 매니저입니다.
    /// </summary>
    public class PlayFabAuthManager : MonoBehaviour
    {
        public static PlayFabAuthManager Instance { get; private set; }
        public static event Action<PlayFabAuthManager> InstanceChanged;

        [Header("Settings")]
        public string TitleId = "133DF7"; // 사용자님의 Title ID

        // 인증 결과 이벤트
        public event Action OnLoginSuccess;
        public event Action<string> OnLoginFailure;
        public event Action OnRegisterSuccess;
        public event Action<string> OnRegisterFailure;
        public event Action<bool> OnAuthenticationStateChanged;

        private readonly AuthenticationAttemptState _attempts = new AuthenticationAttemptState();
        private PlayFabAuthenticationContext _selectedContext;
        private GlobalDataManager _selectedProfile;
        private int _selectedProfileVersion;
        private Func<double> _clock = () => Time.realtimeSinceStartupAsDouble;
        // SDK login follow-ups (including device info) must use this request's session
        // before our success callback decides whether to adopt it as the selected account.
        private Action<LoginWithPlayFabRequest, Action<LoginResult>, Action<PlayFabError>> _sendLogin =
            (request, success, failure) => new PlayFabClientInstanceAPI(request.AuthenticationContext)
                .LoginWithPlayFab(request, success, failure);
        private Action<RegisterPlayFabUserRequest, Action<RegisterPlayFabUserResult>, Action<PlayFabError>> _sendRegister =
            (request, success, failure) => new PlayFabClientInstanceAPI(request.AuthenticationContext)
                .RegisterPlayFabUser(request, success, failure);
        private Action<Action<bool>> _loadProfile = completed => GlobalDataManager.Instance.LoadProfileForCurrentAccount(completed);

        public bool IsBusy => _attempts.IsBusy;
        public long CurrentRequestId => _attempts.Current?.Id ?? 0;
        public long ApprovedLoginRequestId => _attempts.Current != null &&
            _attempts.Current.Operation == AuthenticationOperation.Login &&
            _attempts.Current.Stage == AuthenticationStage.Succeeded && IsSelectedProfileCurrent()
                ? _attempts.Current.Id : 0;

        private void Awake()
        {
            if (Instance == null || Instance == this)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                // SDK 설정을 코드에서도 확실히 보정합니다.
                if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
                {
                    PlayFabSettings.staticSettings.TitleId = TitleId;
                }
                NotifyInstanceChanged(this);
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
            AuthenticationAttempt attempt = BeginAttempt(AuthenticationOperation.Register, username);
            if (attempt == null) return;
            var request = new RegisterPlayFabUserRequest
            {
                Username = attempt.Username,
                Email = email,
                Password = password,
                DisplayName = attempt.Username,
                AuthenticationContext = new PlayFabAuthenticationContext()
            };
            try
            {
                _sendRegister(request, result =>
                {
                    ExpireAttempt();
                    // Registration's SDK-created session is deliberately never adopted.
                    if (_attempts.ResolveResponse(attempt, result != null, _clock())) PublishOutcome(attempt);
                }, _ => FailResponse(attempt));
            }
            catch (Exception) { FailResponse(attempt); }
        }

        /// <summary>
        /// 기존 계정으로 로그인합니다. (사용자명, 비밀번호)
        /// </summary>
        public void Login(string username, string password)
        {
            AuthenticationAttempt attempt = BeginAttempt(AuthenticationOperation.Login, username);
            if (attempt == null) return;
            var request = new LoginWithPlayFabRequest
            {
                Username = attempt.Username,
                Password = password,
                AuthenticationContext = new PlayFabAuthenticationContext()
            };
            try
            {
                _sendLogin(request, result => AcceptLoginResponse(attempt, request.AuthenticationContext, result),
                    _ => FailResponse(attempt));
            }
            catch (Exception) { FailResponse(attempt); }
        }

        private AuthenticationAttempt BeginAttempt(AuthenticationOperation operation, string username)
        {
            if (!isActiveAndEnabled) return null;
            ExpireAttempt();
            AuthenticationAttempt attempt = _attempts.Begin(operation, username, _clock());
            if (attempt == null) return null;
            Notify(OnAuthenticationStateChanged, subscriber => ((Action<bool>)subscriber)(true), attempt);
            return _attempts.IsCurrent(attempt) && _attempts.IsBusy ? attempt : null;
        }

        private void AcceptLoginResponse(AuthenticationAttempt attempt, PlayFabAuthenticationContext context, LoginResult result)
        {
            ExpireAttempt();
            bool valid = result != null && context != null && context.IsClientLoggedIn() &&
                !string.IsNullOrWhiteSpace(context.PlayFabId) && context.PlayFabId == result.PlayFabId &&
                context.ClientSessionTicket == result.SessionTicket;
            if (!_attempts.ResolveResponse(attempt, valid, _clock())) return;
            if (!valid) { PublishOutcome(attempt); return; }

            // SDK callbacks mutate only their request context. Commit a copy once, after the
            // attempt check, so late or duplicate SDK responses cannot replace the selected user.
            _selectedContext = new PlayFabAuthenticationContext();
            _selectedContext.CopyFrom(context);
            PlayFabSettings.staticPlayer.CopyFrom(_selectedContext);
            _selectedProfile = null;
            _selectedProfileVersion = 0;
            try
            {
                _selectedProfile = GlobalDataManager.Instance;
                _selectedProfile.PlayerNickname = attempt.Username;
                // Freeze the version this login owns before reset notifications can reenter.
                _selectedProfileVersion = unchecked(_selectedProfile.ProfileSessionVersion + 1);
                _selectedProfile.BeginPlayerSession();
                if (!IsCurrentProfileAttempt(attempt)) { FinishProfileAttempt(attempt); return; }
                PlayFabBattleManager.Instance?.BeginProfileSession();
                if (!IsCurrentProfileAttempt(attempt)) { FinishProfileAttempt(attempt); return; }
                _loadProfile(_ => FinishProfileAttempt(attempt));
            }
            catch (Exception)
            {
                // A profile consumer may fail after authentication. Preserve a valid selected
                // session for the lobby's existing retry path, but never approve a reset session.
                FinishProfileAttempt(attempt);
            }
        }

        private void FinishProfileAttempt(AuthenticationAttempt attempt)
        {
            ExpireAttempt();
            if (_attempts.ResolveProfile(attempt, IsSelectedProfileCurrent(), _clock())) PublishOutcome(attempt);
        }

        private bool IsCurrentProfileAttempt(AuthenticationAttempt attempt) =>
            _attempts.IsCurrent(attempt) && _attempts.Current.Stage == AuthenticationStage.Profile &&
            IsSelectedProfileCurrent();

        private bool IsSelectedContextCurrent() => _selectedContext != null &&
            PlayFabSettings.staticPlayer.PlayFabId == _selectedContext.PlayFabId &&
            PlayFabSettings.staticPlayer.ClientSessionTicket == _selectedContext.ClientSessionTicket;

        private bool IsSelectedProfileCurrent() => IsSelectedContextCurrent() && _selectedProfile != null &&
            _selectedProfile.IsCurrentActiveInstance && _selectedProfile.ProfileSessionVersion == _selectedProfileVersion;

        private void FailResponse(AuthenticationAttempt attempt)
        {
            ExpireAttempt();
            if (_attempts.ResolveResponse(attempt, false, _clock())) PublishOutcome(attempt);
        }

        private void Update() => ExpireAttempt();

        private void OnEnable()
        {
            if (Instance == null) { Instance = this; NotifyInstanceChanged(this); }
            Notify(OnAuthenticationStateChanged, subscriber => ((Action<bool>)subscriber)(IsBusy), _attempts.Current);
        }

        private void ExpireAttempt()
        {
            AuthenticationAttempt expired = _attempts.Expire(_clock(), IsSelectedProfileCurrent());
            if (expired != null) PublishOutcome(expired);
        }

        public void CancelPendingAuthentication()
        {
            AuthenticationAttempt attempt = _attempts.Current;
            _attempts.Cancel();
            Notify(OnAuthenticationStateChanged, subscriber => ((Action<bool>)subscriber)(false), attempt);
        }

        private void OnDisable() => CancelPendingAuthentication();

        private void OnDestroy()
        {
            _attempts.Cancel();
            if (Instance != this) return;
            Instance = null;
            NotifyInstanceChanged(null);
        }

        private void PublishOutcome(AuthenticationAttempt attempt)
        {
            bool success = attempt.Stage == AuthenticationStage.Succeeded;
            if (attempt.Operation == AuthenticationOperation.Login)
            {
                if (success) Notify(OnLoginSuccess, subscriber => ((Action)subscriber)(), attempt);
                else Notify(OnLoginFailure, subscriber => ((Action<string>)subscriber)("로그인 요청을 완료하지 못했습니다. 다시 시도해 주세요."), attempt);
            }
            else
            {
                if (success) Notify(OnRegisterSuccess, subscriber => ((Action)subscriber)(), attempt);
                else Notify(OnRegisterFailure, subscriber => ((Action<string>)subscriber)("회원가입 요청을 완료하지 못했습니다. 다시 시도해 주세요."), attempt);
            }
            Notify(OnAuthenticationStateChanged, subscriber => ((Action<bool>)subscriber)(false), attempt);
        }

        private void Notify(Delegate subscribers, Action<Delegate> invoke, AuthenticationAttempt attempt)
        {
            if (subscribers == null) return;
            AuthenticationStage? stage = attempt?.Stage;
            foreach (Delegate subscriber in subscribers.GetInvocationList())
            {
                if (attempt != null && (!_attempts.IsCurrent(attempt) || attempt.Stage != stage)) return;
                try { invoke(subscriber); }
                catch (Exception) { Debug.LogWarning("[PlayFabAuth] An authentication listener failed."); }
            }
        }

        private static void NotifyInstanceChanged(PlayFabAuthManager instance)
        {
            if (InstanceChanged == null) return;
            foreach (Action<PlayFabAuthManager> subscriber in InstanceChanged.GetInvocationList())
            {
                try { subscriber(instance); }
                catch (Exception) { Debug.LogWarning("[PlayFabAuth] An authentication service listener failed."); }
            }
        }

        /// <summary>
        /// 현재 로그인 여부를 확인합니다.
        /// </summary>
        public bool IsLoggedIn() => PlayFabClientAPI.IsClientLoggedIn();
    }
}
