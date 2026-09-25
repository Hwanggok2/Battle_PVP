using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BattlePvp.Networking;
using NUnit.Framework;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class AuthenticationSdkIntegrationTests
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Static | BindingFlags.Instance;
        private readonly List<Action> _restore = new List<Action>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private PlayFabSharedSettings _settings;
        private PlayFabAuthManager _auth;
        private CaptureTransport _transport;
        private readonly PlayFabUnityHttp _responseParser = new PlayFabUnityHttp();

        [SetUp]
        public void SetUp()
        {
            try
            {
                // Replace the reference, not properties on the project's settings asset.
                _settings = ScriptableObject.CreateInstance<PlayFabSharedSettings>();
                _settings.TitleId = "fixture-title";
                _settings.DisableDeviceInfo = false;
                _settings.DisableFocusTimeCollection = true;
                ReplaceField(typeof(PlayFabSettings), "_playFabShared", _settings);
                // DeviceUtil assigns this virtual property after handling a login response.
                PreserveField(typeof(PlayFabApiSettings), "<DisableFocusTimeCollection>k__BackingField",
                    PlayFabSettings.staticSettings);

                var previousContext = new PlayFabAuthenticationContext();
                previousContext.CopyFrom(PlayFabSettings.staticPlayer);
                _restore.Add(() => PlayFabSettings.staticPlayer.CopyFrom(previousContext));
                PlayFabSettings.staticPlayer.ForgetAllCredentials();

                object plugins = typeof(PluginManager).GetField("Instance", Fields).GetValue(null);
                FieldInfo pluginMap = typeof(PluginManager).GetField("plugins", Fields);
                ReplaceField(typeof(PluginManager), "plugins", Activator.CreateInstance(pluginMap.FieldType), plugins);
                _transport = new CaptureTransport();
                PluginManager.SetPlugin(_transport, PluginContract.PlayFab_Transport);
                // Leave the actual SDK serializer in the response path.
                PluginManager.SetPlugin(new PlayFab.Json.SimpleJsonInstance(), PluginContract.PlayFab_Serializer);

                ReplaceField(typeof(PlayFabHttp), "_logger", null);
                ReplaceField(typeof(PlayFabHttp), "ApiProcessingEventHandler", null);
                ReplaceField(typeof(PlayFabHttp), "ApiProcessingErrorEventHandler", null);
                foreach (string name in new[] { "_needsAttribution", "_gatherDeviceInfo", "_gatherScreenTime" })
                    ReplaceField(typeof(PlayFabDeviceUtil), name, false);
                ReplaceField(typeof(SingletonMonoBehaviour<PlayFabHttp>), "_instance", null);
                PlayFabHttp http = NewInactiveObject("SDK response fixture").AddComponent<PlayFabHttp>();
                typeof(SingletonMonoBehaviour<PlayFabHttp>).GetField("_instance", Fields).SetValue(null, http);

                ReplaceField(typeof(PlayFabAuthManager), "<Instance>k__BackingField", null);
                ReplaceField(typeof(PlayFabAuthManager), "InstanceChanged", null);
                _auth = NewInactiveObject("Authentication SDK fixture").AddComponent<PlayFabAuthManager>();
            }
            catch
            {
                CleanUp();
                throw;
            }
        }

        [TearDown]
        public void TearDown() => CleanUp();

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ProductionSendDelegatesKeepDeviceReportsOnEachRequestSession(
            bool register, bool anotherAccountIsSelected)
        {
            SelectGlobalAccount(anotherAccountIsSelected);
            var contexts = new[] { new PlayFabAuthenticationContext(), new PlayFabAuthenticationContext() };
            var completed = new List<int>();
            var deviceCountsAtCompletion = new List<int>();
            var resultContexts = new PlayFabAuthenticationContext[2];
            int failures = 0;
            for (int i = 0; i < contexts.Length; i++)
            {
                int index = i;
                Send(register, false, contexts[i], result =>
                {
                    resultContexts[index] = result;
                    completed.Add(index);
                    deviceCountsAtCompletion.Add(_transport.DeviceRequestCount);
                }, _ => failures++);
            }

            Assert.That(_transport.Requests.Count, Is.EqualTo(2));
            CallRequestContainer first = _transport.Requests[0];
            CallRequestContainer second = _transport.Requests[1];
            AssertLoginRequest(first, contexts[0], register);
            AssertLoginRequest(second, contexts[1], register);
            Assert.That(first.instanceApi, Is.Not.SameAs(second.instanceApi));

            // SDK response processing precedes the application's decision to select an account.
            // Complete B before A and deliberately do not adopt either session in these callbacks.
            _responseParser.OnResponse(LoginResponse("B"), second);
            Assert.That(_transport.Requests.Count, Is.EqualTo(3));
            AssertDeviceRequest(_transport.Requests[2], second, contexts[1], "B");
            AssertGlobalAccount(anotherAccountIsSelected);
            _responseParser.OnResponse(LoginResponse("A"), first);
            Assert.That(_transport.Requests.Count, Is.EqualTo(4));
            AssertDeviceRequest(_transport.Requests[3], first, contexts[0], "A");
            AssertGlobalAccount(anotherAccountIsSelected);

            CollectionAssert.AreEqual(new[] { 1, 0 }, completed);
            CollectionAssert.AreEqual(new[] { 1, 2 }, deviceCountsAtCompletion,
                "The SDK's device follow-up must succeed before the application success callback.");
            Assert.That(failures, Is.Zero);
            AssertContext(resultContexts[0], "A");
            AssertContext(resultContexts[1], "B");
            AssertContext(contexts[0], "A");
            AssertContext(contexts[1], "B");
            _responseParser.OnResponse("{\"code\":200,\"status\":\"OK\",\"data\":{}}", _transport.Requests[2]);
            _responseParser.OnResponse("{\"code\":200,\"status\":\"OK\",\"data\":{}}", _transport.Requests[3]);
            Assert.That(_transport.Requests.Count, Is.EqualTo(4));
            AssertGlobalAccount(anotherAccountIsSelected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaticSdkRouteReproducesTheSignedOutDeviceFailure(bool register)
        {
            var context = new PlayFabAuthenticationContext();
            int completed = 0;
            int failures = 0;
            Send(register, true, context, _ => completed++, _ => failures++);
            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
            CallRequestContainer request = _transport.Requests[0];
            Assert.That(request.context, Is.SameAs(context));
            Assert.That(request.instanceApi, Is.Null);
            LogAssert.Expect(LogType.Exception,
                new Regex("^PlayFabException: Must be logged in to call this method(?:\\r?\\n|$)"));

            _responseParser.OnResponse(LoginResponse("A"), request);

            AssertContext(context, "A");
            Assert.That(completed, Is.Zero, "The old SDK route aborts before application success.");
            Assert.That(failures, Is.Zero, "OnResponse logs this SDK exception instead of invoking an API error callback.");
            Assert.That(_transport.DeviceRequestCount, Is.Zero);
            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
            AssertGlobalAccount(false);
        }

        private void Send(bool register, bool staticRoute, PlayFabAuthenticationContext context,
            Action<PlayFabAuthenticationContext> completed, Action<PlayFabError> failed)
        {
            if (register)
            {
                var request = new RegisterPlayFabUserRequest
                {
                    Username = "fixture-user", Email = "fixture@example.invalid", Password = "fixture-password",
                    AuthenticationContext = context
                };
                if (staticRoute) PlayFabClientAPI.RegisterPlayFabUser(request, result => completed(result.AuthenticationContext), failed);
                else ((Action<RegisterPlayFabUserRequest, Action<RegisterPlayFabUserResult>, Action<PlayFabError>>)
                    typeof(PlayFabAuthManager).GetField("_sendRegister", Fields).GetValue(_auth))
                    (request, result => completed(result.AuthenticationContext), failed);
            }
            else
            {
                var request = new LoginWithPlayFabRequest
                {
                    Username = "fixture-user", Password = "fixture-password", AuthenticationContext = context
                };
                if (staticRoute) PlayFabClientAPI.LoginWithPlayFab(request, result => completed(result.AuthenticationContext), failed);
                else ((Action<LoginWithPlayFabRequest, Action<LoginResult>, Action<PlayFabError>>)
                    typeof(PlayFabAuthManager).GetField("_sendLogin", Fields).GetValue(_auth))
                    (request, result => completed(result.AuthenticationContext), failed);
            }
        }

        private static void AssertLoginRequest(CallRequestContainer request, PlayFabAuthenticationContext context, bool register)
        {
            Assert.That(request.ApiEndpoint, Is.EqualTo(register ? "/Client/RegisterPlayFabUser" : "/Client/LoginWithPlayFab"));
            Assert.That(request.context, Is.SameAs(context));
            Assert.That(request.ApiRequest.AuthenticationContext, Is.SameAs(context));
            Assert.That(request.instanceApi, Is.TypeOf<PlayFabClientInstanceAPI>());
            Assert.That(((PlayFabClientInstanceAPI)request.instanceApi).authenticationContext, Is.SameAs(context));
        }

        private static void AssertDeviceRequest(CallRequestContainer device, CallRequestContainer login,
            PlayFabAuthenticationContext context, string suffix)
        {
            Assert.That(device.ApiEndpoint, Is.EqualTo("/Client/ReportDeviceInfo"));
            Assert.That(device.instanceApi, Is.SameAs(login.instanceApi));
            Assert.That(device.context, Is.SameAs(context));
            Assert.That(device.context, Is.Not.SameAs(PlayFabSettings.staticPlayer));
            Assert.That(device.RequestHeaders["X-Authorization"], Is.EqualTo("fixture-session-" + suffix));
            Assert.That(device.ApiRequest, Is.TypeOf<DeviceInfoRequest>());
            Assert.That(((DeviceInfoRequest)device.ApiRequest).Info, Is.Not.Null);
            // Do not print, serialize to disk, or inspect the SDK's collected device identifiers.
            AssertContext(context, suffix);
        }

        private static void AssertContext(PlayFabAuthenticationContext context, string suffix)
        {
            Assert.That(context, Is.Not.Null);
            Assert.That(context.PlayFabId, Is.EqualTo("fixture-player-" + suffix));
            Assert.That(context.ClientSessionTicket, Is.EqualTo("fixture-session-" + suffix));
            Assert.That(context.EntityToken, Is.EqualTo("fixture-token-" + suffix));
            Assert.That(context.EntityId, Is.EqualTo("fixture-entity-" + suffix));
            Assert.That(context.EntityType, Is.EqualTo("title_player_account"));
        }

        private static void SelectGlobalAccount(bool selected)
        {
            PlayFabSettings.staticPlayer.ForgetAllCredentials();
            if (selected) PlayFabSettings.staticPlayer.CopyFrom(new PlayFabAuthenticationContext(
                "fixture-global-session", "fixture-global-token", "fixture-global-player",
                "fixture-global-entity", "title_player_account", "fixture-global-telemetry"));
        }

        private static void AssertGlobalAccount(bool selected)
        {
            PlayFabAuthenticationContext context = PlayFabSettings.staticPlayer;
            Assert.That(context.PlayFabId, Is.EqualTo(selected ? "fixture-global-player" : null));
            Assert.That(context.ClientSessionTicket, Is.EqualTo(selected ? "fixture-global-session" : null));
            Assert.That(context.EntityToken, Is.EqualTo(selected ? "fixture-global-token" : null));
            Assert.That(context.EntityId, Is.EqualTo(selected ? "fixture-global-entity" : null));
            Assert.That(context.EntityType, Is.EqualTo(selected ? "title_player_account" : null));
            Assert.That(context.TelemetryKey, Is.EqualTo(selected ? "fixture-global-telemetry" : null));
        }

        private static string LoginResponse(string suffix) =>
            "{\"code\":200,\"status\":\"OK\",\"data\":{\"PlayFabId\":\"fixture-player-" + suffix +
            "\",\"SessionTicket\":\"fixture-session-" + suffix + "\",\"EntityToken\":{\"EntityToken\":\"fixture-token-" + suffix +
            "\",\"Entity\":{\"Id\":\"fixture-entity-" + suffix + "\",\"Type\":\"title_player_account\"}}," +
            "\"SettingsForUser\":{\"GatherDeviceInfo\":true,\"GatherFocusInfo\":false,\"NeedsAttribution\":false}}}";

        private GameObject NewInactiveObject(string name)
        {
            var owner = new GameObject(name);
            owner.SetActive(false);
            _objects.Add(owner);
            return owner;
        }

        private void PreserveField(Type type, string name, object target = null)
        {
            FieldInfo field = type.GetField(name, Fields);
            Assert.That(field, Is.Not.Null, "Missing SDK fixture field: " + name);
            object previous = field.GetValue(target);
            _restore.Add(() => field.SetValue(target, previous));
        }

        private void ReplaceField(Type type, string name, object value, object target = null)
        {
            PreserveField(type, name, target);
            type.GetField(name, Fields).SetValue(target, value);
        }

        private void CleanUp()
        {
            // Destroy only fixture objects while fake transport/settings and the null logger remain installed.
            try
            {
                for (int i = _objects.Count - 1; i >= 0; i--)
                    if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            }
            finally
            {
                _objects.Clear();
                for (int i = _restore.Count - 1; i >= 0; i--) _restore[i]();
                _restore.Clear();
                if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
                _auth = null;
                _transport = null;
            }
        }

        private sealed class CaptureTransport : ITransportPlugin
        {
            public readonly List<CallRequestContainer> Requests = new List<CallRequestContainer>();
            public int DeviceRequestCount { get; private set; }
            public bool IsInitialized => true;
            public void Initialize() => throw new InvalidOperationException("The fixture transport is already initialized.");
            public void Update() { }
            public void OnDestroy() { }
            public int GetPendingMessages() => 0;
            public void MakeApiCall(object request)
            {
                var call = (CallRequestContainer)request;
                if (Requests.Count >= 4 || (call.ApiEndpoint != "/Client/LoginWithPlayFab" &&
                    call.ApiEndpoint != "/Client/RegisterPlayFabUser" && call.ApiEndpoint != "/Client/ReportDeviceInfo"))
                    throw new InvalidOperationException("Unexpected SDK request in the authentication fixture.");
                Requests.Add(call);
                if (call.ApiEndpoint == "/Client/ReportDeviceInfo") DeviceRequestCount++;
            }
            public void SimpleGetCall(string url, Action<byte[]> success, Action<string> failure) =>
                throw new InvalidOperationException("Real HTTP is not available in this fixture.");
            public void SimplePutCall(string url, byte[] payload, Action<byte[]> success, Action<string> failure) =>
                throw new InvalidOperationException("Real HTTP is not available in this fixture.");
            public void SimplePostCall(string url, byte[] payload, Action<byte[]> success, Action<string> failure) =>
                throw new InvalidOperationException("Real HTTP is not available in this fixture.");
        }
    }
}
