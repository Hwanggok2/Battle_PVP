#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Networking;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using BattlePvp.UI;

namespace BattlePvp.Diagnostics
{
    /// <summary>명시적으로 켠 개발 빌드에서만 프레임 간격을 수집한다. 플레이나 부하를 생성하지 않는다.</summary>
    public sealed class DevelopmentPerformanceCapture : MonoBehaviour
    {
        private const int MaximumFrames = 120000;
        private static DevelopmentPerformanceCapture _instance;
        private readonly List<double> _frames = new List<double>(MaximumFrames);
        private readonly List<ConditionProbe> _probes = new List<ConditionProbe>(64);
        private readonly Dictionary<uint, Vector3> _positions = new Dictionary<uint, Vector3>(8);
        private readonly Dictionary<uint, float> _damage = new Dictionary<uint, float>(8);
        private readonly HashSet<uint> _movingInWindow = new HashSet<uint>(8);
        private string _series = string.Empty;
        private string _buildId = string.Empty;
        private string _hardware = string.Empty;
        private string _browser = string.Empty;
        private string _network = string.Empty;
        private string _scenario = string.Empty;
        private string _runNumber = "1";
        private bool _multipleClients;
        private bool _freezeQuality = true, _previousQualityPause;
        private RuntimePerformanceCounters _counters;
        private bool _running;
        private bool _capturing;
        private double _startedAt;
        private double _captureStartedAt;
        private double _lastFrameAt;
        private double _nextProbeAt;
        private double _nextCombatWindow;
        private double _windowDamage;
        private int _qualityLevel;
        private int _sceneHandle;
        private PerformanceRunRecord _record;
        private string _fileStem;
        private string _notice = "Fill the conditions. Press F8 or Start for one 30s warmup + 60s run. Use three separate runs per platform.";
        private Vector2 _scroll;

        private struct ConditionProbe
        {
            public double Elapsed;
            public int Players;
            public int Moving;
            public double Damage;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OpenWhenExplicitlyRequested()
        {
            bool requested = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri uri))
                foreach (string part in uri.Query.TrimStart('?').Split('&'))
                    requested |= part == "battlePerf=1";
#else
            foreach (string argument in Environment.GetCommandLineArgs()) requested |= argument == "-battlePerf";
#endif
            if (requested) Open();
        }

        public static void Open()
        {
            if (_instance != null) return;
            var owner = new GameObject("Development Performance Capture");
            owner.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(owner);
            _instance = owner.AddComponent<DevelopmentPerformanceCapture>();
        }

        private void OnEnable() => ScoreSystem.OnScoreUpdated += OnPlayerRosterOrScoreChanged;

        private void OnPlayerRosterOrScoreChanged(ScoreSystem _)
        {
            if (!_running || !_capturing) return;
            // Join/leave events also observe the count, so a sub-second disconnect is not hidden by the one-second probes.
            int players = 0;
            foreach (ScoreSystem score in ScoreSystem.ActiveScores)
                if (score != null && score.netId != 0 && NetworkClient.spawned.TryGetValue(score.netId, out NetworkIdentity identity) &&
                    identity == score.netIdentity) players++;
            ObservePlayerCount(players);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                if (_running) Finish(false, "Stopped manually with F8");
                else StartCapture();
                return;
            }
            if (!_running) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_capturing)
            {
                _record.WarmupSeconds = now - _startedAt;
                if (_record.WarmupSeconds < PerformanceCaptureRules.WarmupSeconds || !OnDemandRendering.willCurrentFrameRender) return;
                _capturing = true;
                _captureStartedAt = _lastFrameAt = now;
                _nextProbeAt = now + 1d;
                _nextCombatWindow = PerformanceCaptureRules.CombatWindowSeconds;
                ProbeConditions(now);
                _counters = new RuntimePerformanceCounters();
                _record.Counters = _counters.Records;
                return;
            }

            CheckContinuousConditions();
            _counters?.Sample();
            // Network/input updates can run at 120 Hz while only 30/60 frames are displayed.
            if (!OnDemandRendering.willCurrentFrameRender) return;

            if (_frames.Count == MaximumFrames) { Finish(false, "Frame buffer capacity reached; no samples were silently dropped"); return; }
            _frames.Add((now - _lastFrameAt) * 1000d);
            _lastFrameAt = now;
            _record.CaptureSeconds = now - _captureStartedAt;
            CheckContinuousConditions();
            if (now >= _nextProbeAt)
            {
                _nextProbeAt += 1d;
                ProbeConditions(now);
            }
            if (_record.CaptureSeconds >= PerformanceCaptureRules.CaptureSeconds)
                Finish(true, "Completed the requested observation interval");
        }

        private void StartCapture()
        {
            if (_running) return;
            PerformanceCaptureContext context = BuildContext();
            if (!context.IsDocumented || !int.TryParse(_runNumber, out int number) || number < 1 || number > 3)
            {
                _notice = "Conditions are incomplete. Record build/revision, hardware, scenario, network setup and run number 1–3. WebGL also requires browser/version.";
                return;
            }
            _frames.Clear();
            _probes.Clear();
            _positions.Clear();
            _damage.Clear();
            _movingInWindow.Clear();
            _windowDamage = 0d;
            _qualityLevel = QualitySettings.GetQualityLevel();
            _sceneHandle = SceneManager.GetActiveScene().handle;
            _previousQualityPause = LocalGameSettings.AdaptiveQualityPaused;
            if (_freezeQuality) LocalGameSettings.AdaptiveQualityPaused = true;
            context = BuildContext();
            _record = new PerformanceRunRecord
            {
                Context = context, RunNumber = number, StartedUtc = DateTime.UtcNow.ToString("O"),
                MinimumObservedPlayers = int.MaxValue
            };
            _fileStem = "frames-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _startedAt = Time.realtimeSinceStartupAsDouble;
            _capturing = false;
            _running = true;
            _notice = "Warmup started. Return to gameplay with the existing Esc control if needed. F8 stops and records an incomplete run.";
            Debug.Log("[Performance Capture] " + _notice);
        }

        private PerformanceCaptureContext BuildContext() => new PerformanceCaptureContext
        {
            Series = _series, BuildId = _buildId, Platform = Application.platform.ToString(), IsEditor = Application.isEditor,
            UnityVersion = Application.unityVersion, BuildType = Application.isEditor ? "Editor" : "Development Player",
            Scene = SceneManager.GetActiveScene().name,
            HardwareDescription = _hardware, Cpu = DescribeDetected(SystemInfo.processorType), Gpu = DescribeDetected(SystemInfo.graphicsDeviceName),
            MemoryMb = SystemInfo.systemMemorySize, OperatingSystem = SystemInfo.operatingSystem, Browser = _browser,
            Width = Screen.width, Height = Screen.height, Quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            VSyncCount = QualitySettings.vSyncCount, TargetFrameRate = Application.targetFrameRate,
            RenderFrameInterval = OnDemandRendering.renderFrameInterval, EffectiveQuality = LocalGameSettings.EffectiveQuality,
            AdaptiveTier = LocalGameSettings.EffectiveTier, RenderScale = LocalGameSettings.EffectiveRenderScale,
            AdaptiveQualityPaused = LocalGameSettings.AdaptiveQualityPaused,
            NetworkRole = NetworkServer.active && NetworkClient.active ? "Host" : NetworkClient.active ? "Client" : "Offline",
            NetworkConditions = _network, Scenario = _scenario, MultipleClientsOnThisDevice = _multipleClients
        };

        private static string DescribeDetected(string value) => string.IsNullOrWhiteSpace(value)
            ? "Unavailable; see HardwareDescription" : value;

        private void CheckContinuousConditions()
        {
            _record.FocusMaintained &= Application.isFocused;
            // A normal death/respawn is part of combat load. Only deliberate menu/text input interrupts this observation.
            _record.GameplayInputMaintained &= GameInputController.CurrentMode != GameInputMode.Menu &&
                !GameInputController.IsTextInputActive;
            _record.InBattleThroughout &= BattleStateMachine.Instance != null &&
                BattleStateMachine.Instance.CurrentState == BattleState.InBattle;
            _record.SettingsMaintained &= Screen.width == _record.Context.Width && Screen.height == _record.Context.Height &&
                QualitySettings.GetQualityLevel() == _qualityLevel && QualitySettings.vSyncCount == _record.Context.VSyncCount &&
                Application.targetFrameRate == _record.Context.TargetFrameRate && SceneManager.GetActiveScene().handle == _sceneHandle;
            _record.SettingsMaintained &= OnDemandRendering.renderFrameInterval == _record.Context.RenderFrameInterval &&
                LocalGameSettings.EffectiveQuality == _record.Context.EffectiveQuality && LocalGameSettings.EffectiveTier == _record.Context.AdaptiveTier &&
                Mathf.Approximately(LocalGameSettings.EffectiveRenderScale, _record.Context.RenderScale) &&
                LocalGameSettings.AdaptiveQualityPaused == _record.Context.AdaptiveQualityPaused;
        }

        private void ProbeConditions(double now)
        {
            CheckContinuousConditions();
            int players = 0;
            int moving = 0;
            double damageDelta = 0d;
            foreach (ScoreSystem score in ScoreSystem.ActiveScores)
            {
                if (score == null || score.netId == 0 || !NetworkClient.spawned.TryGetValue(score.netId, out NetworkIdentity identity) ||
                    identity != score.netIdentity) continue;
                players++;
                Vector3 position = score.transform.position;
                if (_positions.TryGetValue(score.netId, out Vector3 previous) && (position - previous).sqrMagnitude >= 0.0025f)
                {
                    moving++;
                    _movingInWindow.Add(score.netId);
                }
                _positions[score.netId] = position;
                if (_damage.TryGetValue(score.netId, out float previousDamage))
                    damageDelta += Math.Max(0d, score.MatchDamageDealt - previousDamage);
                _damage[score.netId] = score.MatchDamageDealt;
            }
            ObservePlayerCount(players);
            _record.ConditionProbes++;
            _windowDamage += damageDelta;
            double elapsed = now - _captureStartedAt;
            _probes.Add(new ConditionProbe { Elapsed = elapsed, Players = players, Moving = moving, Damage = damageDelta });
            if (elapsed >= _nextCombatWindow)
            {
                _record.CombatWindows++;
                if (_movingInWindow.Count >= 4 && _windowDamage > 0d) _record.ActiveCombatWindows++;
                _movingInWindow.Clear();
                _windowDamage = 0d;
                _nextCombatWindow += PerformanceCaptureRules.CombatWindowSeconds;
            }
        }

        private void ObservePlayerCount(int players)
        {
            _record.MinimumObservedPlayers = Math.Min(_record.MinimumObservedPlayers, players);
            _record.MaximumObservedPlayers = Math.Max(_record.MaximumObservedPlayers, players);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_running && _capturing && !focused) _record.FocusMaintained = false;
        }

        private void Finish(bool completed, string reason)
        {
            if (!_running) return;
            _running = false;
            _counters?.Dispose(); _counters = null;
            LocalGameSettings.AdaptiveQualityPaused = _previousQualityPause;
            _record.Completed = completed;
            _record.StopReason = reason;
            if (_record.ConditionProbes == 0) _record.MinimumObservedPlayers = 0;
            _record.Frames = FrameStatistics.Calculate(_frames);
            _record.Verdict = PerformanceCaptureRules.EvaluateRun(_record).ToString();
            _notice = reason + ". " + _record.Verdict + ". This single run is not a platform performance pass.";
            Debug.Log("[Performance Capture] " + _notice);
#if !UNITY_WEBGL || UNITY_EDITOR
            ExportJson();
            ExportCsv();
#endif
        }

        private void OnDisable()
        {
            ScoreSystem.OnScoreUpdated -= OnPlayerRosterOrScoreChanged;
            Finish(false, "Capture component disabled");
        }
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void OnGUI()
        {
            // No IMGUI controls or text formatting run during the measured interval or warmup.
            if (_running) return;
            GUILayout.BeginArea(new Rect(12, 12, Math.Min(620, Screen.width - 24), Math.Min(660, Screen.height - 24)), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Development frame capture — F8 starts/stops. Esc uses the game's existing cursor control.");
            GUILayout.Label(_notice);
            _series = Field("Comparison series", _series);
            _buildId = Field("Build / revision identifier", _buildId);
            _hardware = Field("Physical CPU/GPU/RAM and device notes", _hardware);
            _browser = Field("Browser and version (WebGL)", _browser);
            _network = Field("Connection/RTT/loss setup (state unknown values)", _network);
            _scenario = Field("Repeatable representative combat scenario", _scenario);
            _runNumber = Field("Run number (1–3)", _runNumber);
            _multipleClients = GUILayout.Toggle(_multipleClients, "Multiple clients share this physical device (resource contention)");
            _freezeQuality = GUILayout.Toggle(_freezeQuality, "Freeze automatic quality for a comparable fixed-quality capture");
            GUILayout.Label("A valid run needs 8 observed players, an active battle, unchanged settings/focus, and combat activity in every 10s window. Editor runs remain diagnostics only.");
            if (GUILayout.Button("Start one 30s warmup + 60s capture")) StartCapture();
            if (_record?.Frames != null)
            {
                bool confirmed = GUILayout.Toggle(_record.OperatorConfirmedRepresentativeCombat,
                    "I observed representative 8-player combat throughout this recorded run (not idle work time)");
                if (confirmed != _record.OperatorConfirmedRepresentativeCombat)
                {
                    _record.OperatorConfirmedRepresentativeCombat = confirmed;
                    _record.Verdict = PerformanceCaptureRules.EvaluateRun(_record).ToString();
#if !UNITY_WEBGL || UNITY_EDITOR
                    ExportJson();
#endif
                }
                GUILayout.Label(_record.Frames.Count == 0 ? _record.Verdict + " | No measured frame samples" :
                    _record.Verdict + " | p95 " + _record.Frames.P95Ms.ToString("F3") + " ms, p99 " +
                    _record.Frames.P99Ms.ToString("F3") + " ms, max " + _record.Frames.MaximumMs.ToString("F3") + " ms");
                GUILayout.Label("Observed players " + _record.MinimumObservedPlayers + "–" + _record.MaximumObservedPlayers +
                    "; active combat windows " + _record.ActiveCombatWindows + "/" + _record.CombatWindows +
                    "; focused " + _record.FocusMaintained + "; settings unchanged " + _record.SettingsMaintained);
                if (GUILayout.Button("Export JSON conditions and summary")) ExportJson();
                if (GUILayout.Button("Export CSV raw frames and condition probes")) ExportCsv();
                GUILayout.Label("Save both files before another run. JSON includes available engine counters; unsupported counters are explicitly marked. CPU timing can include waits.");
            }
            if (GUILayout.Button("Close diagnostic controls")) Destroy(gameObject);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string Field(string label, string value)
        {
            GUILayout.Label(label);
            return GUILayout.TextField(value);
        }

        private void ExportJson()
        {
            if (_record?.Frames == null) return;
            _record.Verdict = PerformanceCaptureRules.EvaluateRun(_record).ToString();
            Save(_fileStem + ".json", JsonUtility.ToJson(_record, true), "application/json");
        }

        private void ExportCsv()
        {
            if (_record?.Frames == null) return;
            var csv = new StringBuilder(_frames.Count * 35 + _probes.Count * 70);
            csv.AppendLine("kind,index,elapsed_seconds,frame_ms,observed_players,moving_players,accepted_hp_damage");
            double elapsed = 0d;
            for (int i = 0; i < _frames.Count; i++)
            {
                elapsed += _frames[i] / 1000d;
                csv.Append("frame,").Append(i).Append(',').Append(elapsed.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(_frames[i].ToString("R", CultureInfo.InvariantCulture)).AppendLine(",,,");
            }
            for (int i = 0; i < _probes.Count; i++)
            {
                ConditionProbe probe = _probes[i];
                csv.Append("probe,").Append(i).Append(',').Append(probe.Elapsed.ToString("R", CultureInfo.InvariantCulture)).Append(",,")
                    .Append(probe.Players).Append(',').Append(probe.Moving).Append(',')
                    .Append(probe.Damage.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
            Save(_fileStem + ".csv", csv.ToString(), "text/csv");
        }

        private void Save(string filename, string contents, string mime)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BattlePvpPerformance_Download(filename, contents, mime);
#else
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "PerformanceCaptures");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, filename), contents, new UTF8Encoding(false));
                _notice = "Saved to " + Path.Combine(directory, filename);
            }
            catch (Exception error) { _notice = "Export failed: " + error.Message; Debug.LogWarning(_notice); }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BattlePvpPerformance_Download(string filename, string contents, string mime);
#endif
    }
}
#endif
