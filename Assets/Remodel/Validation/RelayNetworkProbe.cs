#if UNITY_EDITOR || BATTLE_PVP_RELAY_PROBE
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Diagnostics;
using BattlePvp.Networking;
using Mirror;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace BattlePvp.Remodel.Validation
{
    // Explicit test scene/build only. Does not bypass authentication in normal game scenes/builds.
    public sealed class RelayNetworkProbe : MonoBehaviour
    {
        [Serializable] private sealed class Result
        {
            public string role, outcome, protocol, region;
            public string route;
            public int targetFps, tickRate, renderInterval;
            public bool connected;
            public bool publicCandidateAvailable;
            public double connectSeconds;
            public double renderedFps;
            public long directSocketSendFailures;
            public double wallRttP50Ms, wallRttP95Ms, wallRttMaxMs;
            public double relayRttP50Ms, relayRttP95Ms;
            public int reliableEchoes, reliableErrors, directSamples, relaySamples;
        }
        [Serializable] private sealed class BrowserConfig { public string joinCode; public bool disableRtc; public int seconds = 50; }
        private UnityRelayTransport _relay;
        private RelayLatencyCapture _capture;
        private CancellationTokenSource _cancel;
        private bool _host, _direct, _automatic, _fallbackProbe, _started, _finished, _connected;
        private string _connectedRoute;
        private bool _publicCandidateAvailable;
        private const string ProbeRoom = "battle_abc123_11111111111111111111111111111111";
        private double _began, _connectedAt;
        private string _folder, _exchange;
        private double _seconds;
        private int _renderedFrames, _rendersAtConnect;
        public struct WallEcho : NetworkMessage { public double Sent; }
        private readonly System.Collections.Generic.List<double> _wallRtts = new System.Collections.Generic.List<double>(1000);
        private double _nextEcho;
        private bool _rtc, _rtcStarted;
        private int _sequence, _receivedSequence, _sequenceErrors;
        public struct OrderedEcho : NetworkMessage { public int Sequence; public byte[] Payload; }
        private readonly System.Collections.Generic.List<double> _relayRtts = new System.Collections.Generic.List<double>();
        private static double Percentile(System.Collections.Generic.List<double> values, double fraction) =>
            values.Count == 0 ? -1 : values[(int)Math.Ceiling(values.Count * fraction) - 1];
        private void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += CountRenderedFrame;
        private void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= CountRenderedFrame;
        private void CountRenderedFrame(UnityEngine.Rendering.ScriptableRenderContext context, Camera[] cameras) => _renderedFrames++;
        private void Connected()
        {
            _connected = true; _connectedAt = Time.realtimeSinceStartupAsDouble;
            _connectedRoute = _relay.ConnectionRoute; _rendersAtConnect = _renderedFrames;
        }

        private static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private async void Start()
        {
            _host = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-host") >= 0;
            _direct = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-direct") >= 0;
            _automatic = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-auto") >= 0;
            _fallbackProbe = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-fallback") >= 0;
            _folder = Argument("-relay-probe-report", "Reports/PingDiagnosis/probe");
            _exchange = Argument("-relay-probe-exchange", "Temp/RelayPingProbe/join.txt");
            _seconds = double.Parse(Argument("-relay-probe-seconds", "60"), System.Globalization.CultureInfo.InvariantCulture);
            _rtc = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-rtc") >= 0;
#if UNITY_WEBGL && !UNITY_EDITOR
            _rtc = true;
#endif
            Application.runInBackground = true;
            Application.targetFrameRate = int.Parse(Argument("-relay-probe-fps", "60"));
            QualitySettings.vSyncCount = 0;
            NetworkServer.tickRate = int.Parse(Argument("-relay-probe-tick", "60"));
            _began = Time.realtimeSinceStartupAsDouble;
            _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            _relay = gameObject.AddComponent<UnityRelayTransport>(); Transport.active = _relay;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-lowlatency") >= 0)
                gameObject.AddComponent<RoomNetworkTiming>();
            try
            {
                // Separate anonymous profiles so the probe cannot replace the game's login session.
                if (!_direct && UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(_host ? "relay-probe-host" : "relay-probe-client"));
                if (_host)
                {
                    string code = _direct ? "loopback-ready" : await _relay.PrepareHostAsync(8, _cancel.Token);
                    if (_automatic) _relay.ConfigureLocalRoom(ProbeRoom, code);
                    NetworkServer.OnConnectedEvent = connection => { connection.isAuthenticated = true; Connected(); };
                    NetworkServer.OnErrorEvent = (connection, error, message) => Finish("server_" + error);
                    NetworkServer.listen = !_direct;
                    NetworkServer.Listen(8);
                    NetworkServer.RegisterHandler<WallEcho>((connection, message) => connection.Send(message, Channels.Unreliable));
                    NetworkServer.RegisterHandler<OrderedEcho>((connection, message) => connection.Send(message, Channels.Reliable));
                    if (_direct) { PrepareDirectDriver(true); NetworkServer.listen = true; }
                    while (!_direct && !_relay.ServerRelayReady)
                    {
                        _cancel.Token.ThrowIfCancellationRequested();
                        if (_relay.ServerRelayFailed) throw new InvalidOperationException("Probe relay host unavailable.");
                        await Task.Delay(25, _cancel.Token);
                    }
                    string publicCandidate = _automatic ? await _relay.GatherPublicEndpointAsync(_cancel.Token) : "";
                    _publicCandidateAvailable = !string.IsNullOrEmpty(publicCandidate);
                    Directory.CreateDirectory(Path.GetDirectoryName(_exchange));
                    // The local, ignored Temp file is only a rendezvous; never print/copy its contents into reports.
                    File.WriteAllText(_exchange, code + "\n" + publicCandidate);
                }
                else
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    string publishedCode;
                    using (var request = UnityEngine.Networking.UnityWebRequest.Get(Application.streamingAssetsPath + "/probe-config.json"))
                    {
                        request.SendWebRequest();
                        while (!request.isDone) await Task.Yield();
                        if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) throw new InvalidOperationException("Probe config unavailable.");
                        var config = JsonUtility.FromJson<BrowserConfig>(request.downloadHandler.text);
                        publishedCode = config.joinCode; _rtc = !config.disableRtc; _seconds = config.seconds;
                    }
                    var published = new[] { publishedCode }; string candidate = "";
#else
                    while (!File.Exists(_exchange)) await Task.Delay(100, _cancel.Token);
                    var published = File.ReadAllLines(_exchange);
                    string candidate = published.Length > 1 ? published[1] : "";
#endif
                    _publicCandidateAvailable = !string.IsNullOrEmpty(candidate);
                    bool publicOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-relay-probe-public-only") >= 0;
                    if (!_direct) await _relay.PrepareClientAsync(published[0], _cancel.Token, _automatic && !publicOnly ? ProbeRoom : null, candidate);
                    if (_fallbackProbe) Set("_directEndpoint", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 1));
                    NetworkClient.OnConnectedEvent = () => { NetworkClient.connection.isAuthenticated = true; NetworkClient.Ready(); Connected(); };
                    NetworkClient.OnErrorEvent = (error, message) => Finish("client_" + error);
                    NetworkClient.OnDisconnectedEvent = () => { if (!_finished) Finish("client_disconnected"); };
                    NetworkClient.Connect("relay");
                    NetworkClient.RegisterHandler<WallEcho>(message => {
                        double elapsed = Time.realtimeSinceStartupAsDouble - _connectedAt;
                        double rtt = (Time.realtimeSinceStartupAsDouble - message.Sent) * 1000;
                        if (_rtc && elapsed > 3 && elapsed < 14) _relayRtts.Add(rtt);
                        if (elapsed > (_rtc ? 30 : 10) && _wallRtts.Count < 1000) _wallRtts.Add(rtt);
                    });
                    NetworkClient.RegisterHandler<OrderedEcho>(message => {
                        if (message.Sequence != ++_receivedSequence || message.Payload.Length != 18000 || message.Payload[17999] != (byte)message.Sequence)
                            _sequenceErrors++;
                    });
                    if (_direct) PrepareDirectDriver(false);
                }
                _started = true;
#if !UNITY_WEBGL || UNITY_EDITOR
                _capture = RelayLatencyCapture.Arm(_seconds + 25, _folder);
#endif
            }
            catch (Exception error) { if (!_finished) Finish("setup_" + error.GetType().Name); }
        }

        // Same production UTP polling/flush/pipelines, with only the Relay route removed.
        // Driver injection is restricted to this explicit test build, like RelayPollingTests.
        private void PrepareDirectDriver(bool server)
        {
            var settings = new NetworkSettings();
            try
            {
                settings.WithFragmentationStageParameters(payloadCapacity: UnityRelayTransport.ReliablePacketCapacity);
                settings.WithReliableStageParameters(windowSize: 128);
                var driver = NetworkDriver.Create(settings);
                var pipeline = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
                Set(server ? "_serverDriver" : "_clientDriver", driver);
                Set(server ? "_serverReliablePipeline" : "_clientReliablePipeline", pipeline);
                var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(17965);
                if (server)
                {
                    if (driver.Bind(endpoint) != 0 || driver.Listen() != 0) throw new InvalidOperationException("Probe bind failed.");
                }
                else
                {
                    Set("_clientConnection", driver.Connect(endpoint));
                    Set("_clientConnectFailure", null);
                }
            }
            finally { settings.Dispose(); }
        }

        private void Set(string field, object value) => typeof(UnityRelayTransport)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_relay, value);

        private void Update()
        {
            if (_finished || _relay == null) return;
            if (!_host && _connected && Time.realtimeSinceStartupAsDouble >= _nextEcho)
            {
                _nextEcho = Time.realtimeSinceStartupAsDouble + .1;
                NetworkClient.Send(new WallEcho { Sent = Time.realtimeSinceStartupAsDouble }, Channels.Unreliable);
                if (_rtc)
                {
                    var payload = new byte[18000]; payload[17999] = (byte)++_sequence;
                    NetworkClient.Send(new OrderedEcho { Sequence = _sequence, Payload = payload }, Channels.Reliable);
                }
            }
            if (!_host && _rtc && _connected && !_rtcStarted && Time.realtimeSinceStartupAsDouble - _connectedAt > 15)
            { _rtcStarted = true; _relay.EnableRtcUpgrade(); }
            if (!_connected && Time.realtimeSinceStartupAsDouble - _began > 90) Finish("connection_deadline");
            else if (_started && _connected && Time.realtimeSinceStartupAsDouble - _connectedAt >= _seconds + (_host ? 3 : 0))
                Finish("success");
        }

        private void Finish(string outcome)
        {
            if (_finished) return;
            _finished = true; _cancel?.Cancel();
            _capture?.Finish(outcome);
            _wallRtts.Sort();
            _relayRtts.Sort();
            string result = JsonUtility.ToJson(new Result {
                role = _host ? "host" : "client", outcome = outcome, connected = _connected,
                route = _relay != null ? _relay.GameplayRouteLabel : "unknown", targetFps = Application.targetFrameRate,
                reliableEchoes = _receivedSequence, reliableErrors = _sequenceErrors,
                directSamples = _relay != null && _relay.ClientUsingRtc ? _wallRtts.Count : 0, relaySamples = _relayRtts.Count,
                relayRttP50Ms = Percentile(_relayRtts, .5), relayRttP95Ms = Percentile(_relayRtts, .95),
                tickRate = NetworkServer.tickRate,
                renderInterval = UnityEngine.Rendering.OnDemandRendering.renderFrameInterval,
                publicCandidateAvailable = _publicCandidateAvailable,
                renderedFps = _connected ? (_renderedFrames - _rendersAtConnect) / Math.Max(.001, Time.realtimeSinceStartupAsDouble - _connectedAt) : 0,
                directSocketSendFailures = _relay != null ? _relay.DirectSocketSendFailures : 0,
                wallRttP50Ms = _wallRtts.Count > 0 ? _wallRtts[(int)Math.Ceiling(_wallRtts.Count * .5) - 1] : -1,
                wallRttP95Ms = _wallRtts.Count > 0 ? _wallRtts[(int)Math.Ceiling(_wallRtts.Count * .95) - 1] : -1,
                wallRttMaxMs = _wallRtts.Count > 0 ? _wallRtts[_wallRtts.Count - 1] : -1,
                connectSeconds = _connected ? _connectedAt - _began : -1,
                protocol = _relay != null ? _relay.ConnectionProtocol : "unknown",
                region = _relay != null ? _relay.LastRelayRegion : "unknown"
            }, true);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("RELAY_PROBE_RESULT " + result);
#else
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, _host ? "host-result.json" : "client-result.json"), result);
#endif
            if (NetworkClient.active) NetworkClient.Disconnect();
            if (NetworkServer.active) NetworkServer.Shutdown();
            _relay?.Shutdown();
            if (_host && File.Exists(_exchange)) File.Delete(_exchange);
            _cancel?.Dispose(); _cancel = null;
#if !UNITY_WEBGL
            if (!Application.isEditor) Application.Quit(outcome == "success" ? 0 : 1);
#endif
        }

        private void OnDestroy() { if (!_finished && _relay != null) Finish("destroyed"); }
    }
}
#endif
