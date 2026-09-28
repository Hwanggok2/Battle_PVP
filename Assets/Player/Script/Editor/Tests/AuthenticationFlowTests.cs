using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Managers;
using BattlePvp.Networking;
using BattlePvp.UI;
using NUnit.Framework;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class AuthenticationFlowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Dictionary<FieldInfo, object> _statics = new Dictionary<FieldInfo, object>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<LoginWithPlayFabRequest> _logins = new List<LoginWithPlayFabRequest>();
        private readonly List<Action<LoginResult>> _loginSuccess = new List<Action<LoginResult>>();
        private readonly List<Action<PlayFabError>> _loginFailure = new List<Action<PlayFabError>>();
        private readonly List<RegisterPlayFabUserRequest> _registrations = new List<RegisterPlayFabUserRequest>();
        private readonly List<Action<RegisterPlayFabUserResult>> _registerSuccess = new List<Action<RegisterPlayFabUserResult>>();
        private readonly List<Action<bool>> _profileResults = new List<Action<bool>>();
        private PlayFabAuthenticationContext _previousContext;
        private PlayFabAuthManager _auth;
        private GlobalDataManager _profile;
        private double _now;
        private int _successes;
        private int _failures;
        private int _registered;

        [SetUp]
        public void SetUp()
        {
            Isolate(typeof(PlayFabAuthManager), "<Instance>k__BackingField", null);
            Isolate(typeof(PlayFabAuthManager), "InstanceChanged", null);
            Isolate(typeof(PlayFabBattleManager), "<Instance>k__BackingField", null);
            Isolate(typeof(GlobalDataManager), "_instance", null);
            Isolate(typeof(GlobalDataManager), "_applicationIsQuitting", false);
            _previousContext = new PlayFabAuthenticationContext();
            _previousContext.CopyFrom(PlayFabSettings.staticPlayer);
            PlayFabSettings.staticPlayer.ForgetAllCredentials();
            _profile = NewObject("Authentication profile", false).AddComponent<GlobalDataManager>();
            typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _profile);
            _profile.gameObject.SetActive(true);
            _auth = NewObject("Authentication service", false).AddComponent<PlayFabAuthManager>();
            typeof(PlayFabAuthManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _auth);
            Set(_auth, "_clock", (Func<double>)(() => _now));
            Set(_auth, "_sendLogin", (Action<LoginWithPlayFabRequest, Action<LoginResult>, Action<PlayFabError>>)((request, success, failure) =>
            { _logins.Add(request); _loginSuccess.Add(success); _loginFailure.Add(failure); }));
            Set(_auth, "_sendRegister", (Action<RegisterPlayFabUserRequest, Action<RegisterPlayFabUserResult>, Action<PlayFabError>>)((request, success, failure) =>
            { _registrations.Add(request); _registerSuccess.Add(success); }));
            Set(_auth, "_loadProfile", (Action<Action<bool>>)(completed => _profileResults.Add(completed)));
            _auth.OnLoginSuccess += () => _successes++;
            _auth.OnLoginFailure += _ => _failures++;
            _auth.OnRegisterSuccess += () => _registered++;
            EditorTestLifecycle.SetActive(_auth, true);
            _now = 0;
            _successes = _failures = _registered = 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            foreach (var entry in _statics) entry.Key.SetValue(null, entry.Value);
            _statics.Clear();
            PlayFabSettings.staticPlayer.CopyFrom(_previousContext);
            _logins.Clear(); _loginSuccess.Clear(); _loginFailure.Clear();
            _registrations.Clear(); _registerSuccess.Clear(); _profileResults.Clear();
        }

        [Test]
        public void DistinctRequestContextsPreventOutOfOrderLoginFromReplacingTheSelectedAccount()
        {
            _auth.Login("Alice", "fixture-password");
            _auth.Login("Bob", "fixture-password");
            Assert.That(_logins[0].AuthenticationContext, Is.Not.SameAs(PlayFabSettings.staticPlayer));
            Assert.That(_logins[0].AuthenticationContext, Is.Not.SameAs(_logins[1].AuthenticationContext));
            SucceedLogin(1, "B20");
            _profileResults[0](true);
            SucceedLogin(0, "A10");
            Assert.That(PlayFabSettings.staticPlayer.PlayFabId, Is.EqualTo("B20"));
            Assert.That(_profile.PlayerNickname, Is.EqualTo("Bob"));
            Assert.That(_profileResults.Count, Is.EqualTo(1));
            Assert.That(_successes, Is.EqualTo(1));
        }

        [Test]
        public void RegistrationNeverCommitsItsSessionAcrossAnOverlappingLogin()
        {
            _auth.Register("Alice", "fixture@example.invalid", "fixture-password");
            _auth.Login("Bob", "fixture-password");
            SucceedLogin(0, "B20");
            SucceedRegistration(0, "A10");
            _profileResults[0](true);
            Assert.That(PlayFabSettings.staticPlayer.PlayFabId, Is.EqualTo("B20"));
            Assert.That(_registered, Is.Zero);
            _auth.Register("Alice", "fixture@example.invalid", "fixture-password");
            SucceedRegistration(1, "A10");
            Assert.That(_registered, Is.EqualTo(1));
            Assert.That(PlayFabSettings.staticPlayer.PlayFabId, Is.EqualTo("B20"));
            Assert.That(_profile.PlayerNickname, Is.EqualTo("Bob"));
        }

        [Test]
        public void NewRegistrationInvalidatesPendingLoginWithoutAdoptingEitherSession()
        {
            _auth.Login("Alice", "fixture-password");
            _auth.Register("Bob", "fixture@example.invalid", "fixture-password");
            SucceedLogin(0, "A10");
            SucceedRegistration(0, "B20");
            Assert.That(PlayFabSettings.staticPlayer.IsClientLoggedIn(), Is.False);
            Assert.That(_successes, Is.Zero);
            Assert.That(_registered, Is.EqualTo(1));
            Assert.That(_profileResults.Count, Is.Zero);
        }

        [Test]
        public void DuplicateRequestsAndExplicitFailureAllowExactlyOneRetry()
        {
            _auth.Login("Alice", "fixture-password");
            _auth.Login("Alice", "fixture-password");
            Assert.That(_logins.Count, Is.EqualTo(1));
            _loginFailure[0](new PlayFabError());
            _loginFailure[0](new PlayFabError());
            Assert.That(_failures, Is.EqualTo(1));
            _auth.Login("Alice", "fixture-password");
            Assert.That(_logins.Count, Is.EqualTo(2));
            SucceedLogin(1, "A10");
            _profileResults[0](true);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [Test]
        public void TimeoutIsCheckedInsideCallbackBeforeUpdateAndLateSdkSuccessCannotCommit()
        {
            _auth.Login("Alice", "fixture-password");
            _now = 15;
            SucceedLogin(0, "A10");
            Assert.That(_failures, Is.EqualTo(1));
            Assert.That(PlayFabSettings.staticPlayer.IsClientLoggedIn(), Is.False);
            _auth.Login("Bob", "fixture-password");
            SucceedLogin(1, "B20");
            _profileResults[0](true);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [Test]
        public void RetryableProfileFailurePreservesAuthenticationAndCompletesOnce()
        {
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            _profileResults[0](false);
            _profileResults[0](true);
            Assert.That(_successes, Is.EqualTo(1));
            Assert.That(_failures, Is.Zero);
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(PlayFabSettings.staticPlayer.PlayFabId, Is.EqualTo("A10"));
        }

        [Test]
        public void ProfileResetFailureCannotBecomeLoginSuccess()
        {
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            _profile.BeginPlayerSession();
            _profileResults[0](false);
            Assert.That(_successes, Is.Zero);
            Assert.That(_failures, Is.EqualTo(1));
            Assert.That(_auth.ApprovedLoginRequestId, Is.Zero);
        }

        [Test]
        public void ReentrantResetDuringSessionInitializationCannotBeAdoptedByLogin()
        {
            bool resetAgain = false;
            _profile.OnSavedStatsUpdated += _ =>
            {
                if (resetAgain) return;
                resetAgain = true;
                _profile.BeginPlayerSession();
            };
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            Assert.That(_profileResults.Count, Is.Zero);
            _now = 16;
            Invoke(_auth, "Update");
            Assert.That(_successes, Is.Zero);
            Assert.That(_failures, Is.EqualTo(1));
        }

        [Test]
        public void ReplacedOrDisabledProfileInstanceCannotApproveLogin()
        {
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            var replacement = NewObject("Replacement profile", false).AddComponent<GlobalDataManager>();
            typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, replacement);
            _profileResults[0](false);
            Assert.That(_successes, Is.Zero);
            Assert.That(_failures, Is.EqualTo(1));
            typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _profile);
            _auth.Login("Bob", "fixture-password");
            SucceedLogin(1, "B20");
            _profile.gameObject.SetActive(false);
            _profileResults[1](false);
            Assert.That(_successes, Is.Zero);
            Assert.That(_failures, Is.EqualTo(2));
        }

        [Test]
        public void LateOldProfileAndDuplicateSdkCallbacksCannotResetOrApproveNewLogin()
        {
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            _auth.Login("Bob", "fixture-password");
            SucceedLogin(1, "B20");
            int version = _profile.ProfileSessionVersion;
            SucceedLogin(0, "A10");
            SucceedLogin(1, "C30");
            _profileResults[0](false);
            Assert.That(_successes, Is.Zero);
            Assert.That(_profile.ProfileSessionVersion, Is.EqualTo(version));
            Assert.That(PlayFabSettings.staticPlayer.PlayFabId, Is.EqualTo("B20"));
            _profileResults[1](true);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [Test]
        public void MissingProfileResponseRetainsTheSelectedLoginForLobbyRetry()
        {
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(0, "A10");
            _now = 16;
            Invoke(_auth, "Update");
            _profileResults[0](false);
            Assert.That(_successes, Is.EqualTo(1));
            Assert.That(_auth.IsBusy, Is.False);
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
        }

        [Test]
        public void DisablingServiceIgnoresLateAuthenticationAndProfileCallbacks()
        {
            _auth.Login("Alice", "fixture-password");
            EditorTestLifecycle.SetActive(_auth, false);
            SucceedLogin(0, "A10");
            Assert.That(_successes, Is.Zero);
            Assert.That(PlayFabSettings.staticPlayer.IsClientLoggedIn(), Is.False);
            EditorTestLifecycle.SetActive(_auth, true);
            _auth.Login("Alice", "fixture-password");
            SucceedLogin(1, "A10");
            EditorTestLifecycle.SetActive(_auth, false);
            _profileResults[0](true);
            Assert.That(_successes, Is.Zero);
        }

        [Test]
        public void LoginButtonsRejectRepeatedClicksAndRecoverAfterFailure()
        {
            PlayFabLoginUI ui = CreateUi(out Button login, out Button register);
            login.onClick.Invoke();
            login.onClick.Invoke();
            register.onClick.Invoke();
            Assert.That(_logins.Count, Is.EqualTo(1));
            Assert.That(_registrations.Count, Is.Zero);
            Assert.That(login.interactable, Is.False);
            _loginFailure[0](new PlayFabError());
            Assert.That(login.interactable, Is.True);
            login.onClick.Invoke();
            Assert.That(_logins.Count, Is.EqualTo(2));
            EditorTestLifecycle.SetActive(ui, false);
            SucceedLogin(1, "A10");
            Assert.That(_successes, Is.Zero);
            Assert.That(_auth.IsBusy, Is.False);
        }

        [Test]
        public void UiNavigationIsScheduledOnceAndCancelledByScreenExit()
        {
            PlayFabLoginUI ui = CreateUi(out Button login, out _);
            login.onClick.Invoke();
            SucceedLogin(0, "A10");
            _profileResults[0](true);
            _profileResults[0](true);
            var navigation = (LoginNavigationGate)ui.GetType().GetField("_navigation", Private).GetValue(ui);
            Assert.That(navigation.IsPending, Is.True);
            Assert.That(navigation.TryConsume(_auth.ApprovedLoginRequestId, Time.realtimeSinceStartupAsDouble + 2), Is.True);
            Assert.That(navigation.TryConsume(_auth.ApprovedLoginRequestId, Time.realtimeSinceStartupAsDouble + 3), Is.False);
            EditorTestLifecycle.SetActive(ui, false);
            Assert.That(navigation.IsPending, Is.False);
        }

        private void SucceedLogin(int index, string id)
        {
            // Match the installed SDK: it copies result credentials into request context before
            // invoking the application callback. These are local fixture values, never real API calls.
            var context = new PlayFabAuthenticationContext("fixture-session-" + id, "fixture-entity", id, "entity", "title_player_account");
            _logins[index].AuthenticationContext.CopyFrom(context);
            _loginSuccess[index](new LoginResult { PlayFabId = id, SessionTicket = context.ClientSessionTicket });
        }

        private void SucceedRegistration(int index, string id)
        {
            _registrations[index].AuthenticationContext.CopyFrom(
                new PlayFabAuthenticationContext("fixture-registration", "fixture-entity", id, "entity", "title_player_account"));
            _registerSuccess[index](new RegisterPlayFabUserResult { PlayFabId = id });
        }

        private PlayFabLoginUI CreateUi(out Button login, out Button register)
        {
            var ui = NewObject("Login screen", false).AddComponent<PlayFabLoginUI>();
            var username = NewObject("Username", false).AddComponent<TMP_InputField>();
            var password = NewObject("Password", false).AddComponent<TMP_InputField>();
            username.text = "Alice"; password.text = "fixture-password";
            login = NewObject("Login button", false).AddComponent<Button>();
            register = NewObject("Register button", false).AddComponent<Button>();
            Set(ui, "_idInput", username); Set(ui, "_pwInput", password);
            Set(ui, "_loginButton", login); Set(ui, "_registerButton", register);
            EditorTestLifecycle.SetActive(ui, true);
            return ui;
        }

        private GameObject NewObject(string name, bool active)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.SetActive(active); _objects.Add(value); return value;
        }
        private void Isolate(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            _statics[field] = field.GetValue(null); field.SetValue(null, value);
        }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    }
}
