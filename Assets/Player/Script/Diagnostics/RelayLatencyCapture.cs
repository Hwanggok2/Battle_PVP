using System;
using System.Globalization;
using System.IO;
using System.Text;
using BattlePvp.Networking;
using Mirror;
using UnityEngine;

namespace BattlePvp.Diagnostics
{
    /// <summary>Opt-in, bounded native capture. No per-frame file writes or packet payload storage.</summary>
    public sealed class RelayLatencyCapture : MonoBehaviour
    {
        private struct Frame
        {
            public double Time, FrameMs, RttMs, PollMs, FlushMs, BacklogAge;
            public long Sent, Received, QueueFull, Dropped, SendErrors;
            public int Backlog, Peers, Tick, Gc;
            public bool Focus;
        }
        private struct Pong { public double Time, RttMs; }
        [Serializable] private sealed class Metadata
        {
            public string utc, role, protocol, region, scene, unity, reason, route;
            public int frames, pongs;
            public double seconds;
            public string note = "RTT includes game-loop waiting. Raw host pongs aggregate peers. Bytes exclude transport headers. Diagnostic message hooks may add boxing allocations.";
        }
        private readonly Frame[] _frames = new Frame[30000];
        private readonly Pong[] _pongs = new Pong[15000];
        private int _frameCount, _pongCount;
        private double _start, _previous, _duration;
        private string _folder, _utc, _role, _scene, _protocol, _region, _route;
        private UnityRelayTransport _relay;
        private bool _recording, _finished, _previousCollect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoArm()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-battleNetworkCapture") < 0) return;
            Arm();
        }

        public static RelayLatencyCapture Arm(double seconds = 120, string folder = null)
        {
            var existing = FindFirstObjectByType<RelayLatencyCapture>();
            if (existing != null) return existing;
            var capture = new GameObject("Relay latency capture").AddComponent<RelayLatencyCapture>();
            DontDestroyOnLoad(capture.gameObject);
            capture._duration = Math.Max(1, Math.Min(300, seconds));
            capture._folder = folder ?? Path.Combine(Application.persistentDataPath, "NetworkDiagnostics");
            return capture;
        }

        private void Update()
        {
            if (_recording || _finished || (!NetworkClient.active && !NetworkServer.active)) return;
            _relay = Transport.active as UnityRelayTransport;
            if (_relay == null) return;
            _previousCollect = _relay.CollectDiagnostics;
            _relay.CollectDiagnostics = true;
            _utc = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            _role = NetworkServer.active ? "host" : "client";
            _protocol = _relay.ConnectionProtocol; _region = _relay.LastRelayRegion;
            _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            _start = _previous = Time.realtimeSinceStartupAsDouble;
            _recording = true;
            NetworkLoop.OnLateUpdate += Sample;
            NetworkDiagnostics.InMessageEvent += Receive;
        }

        private void Sample()
        {
            if (!_recording) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (_relay == null) { Finish("transport_removed"); return; }
            double rtt = NetworkClient.isConnected && !NetworkServer.active ? NetworkTime.rtt * 1000d : -1d;
            int peers = 0;
            if (NetworkServer.active)
            {
                foreach (var peer in NetworkServer.connections.Values)
                    if (peer != null && peer is not LocalConnectionToClient)
                    { peers++; rtt = Math.Max(rtt, peer.rtt * 1000d); }
            }
            else if (NetworkClient.isConnected) peers = 1;
            _frames[_frameCount++] = new Frame {
                Time = now - _start, FrameMs = (now - _previous) * 1000d, RttMs = rtt,
                PollMs = _relay.PollMilliseconds, FlushMs = _relay.FlushMilliseconds,
                Sent = _relay.SentBytes, Received = _relay.ReceivedBytes,
                QueueFull = _relay.SendQueueFullCount, Dropped = _relay.UnreliableSendDropCount,
                SendErrors = _relay.SendErrorCount + _relay.DirectSocketSendFailures, Backlog = _relay.BacklogBytes,
                BacklogAge = _relay.OldestBacklogSeconds, Peers = peers, Tick = NetworkServer.actualTickRate,
                Gc = GC.CollectionCount(0), Focus = Application.isFocused
            };
            _previous = now;
            if (peers > 0) { _route = _relay.ConnectionRoute; _region = _relay.LastRelayRegion; }
            if (_frameCount == _frames.Length) Finish("frame_buffer_full");
            else if (now - _start >= _duration) Finish("duration_complete");
            else if (!NetworkClient.active && !NetworkServer.active) Finish("network_stopped");
        }

        private void Receive(NetworkDiagnostics.MessageInfo message)
        {
            if (!_recording || message.message is not NetworkPongMessage pong) return;
            if (_pongCount == _pongs.Length) { Finish("pong_buffer_full"); return; }
            double rtt = (NetworkTime.localTime - pong.localTime) * 1000d;
            if (rtt >= 0d && !double.IsInfinity(rtt))
                _pongs[_pongCount++] = new Pong { Time = Time.realtimeSinceStartupAsDouble - _start, RttMs = rtt };
        }

        public void Finish(string reason = "manual_stop")
        {
            if (!_recording) return;
            _recording = false; _finished = true;
            NetworkLoop.OnLateUpdate -= Sample;
            NetworkDiagnostics.InMessageEvent -= Receive;
            if (_relay != null) _relay.CollectDiagnostics = _previousCollect;
            try
            {
                Directory.CreateDirectory(_folder);
                string stem = Path.Combine(_folder, "relay-" + _utc + "-" + _role);
                var text = new StringBuilder("seconds,frameMs,smoothedRttMs,peers,hostTick,focused,gc0,sentBytes,receivedBytes,queueFull,unreliableDrops,sendErrors,backlogBytes,backlogAgeSeconds,pollTotalMs,flushTotalMs\n");
                for (int i = 0; i < _frameCount; i++)
                {
                    var f = _frames[i];
                    text.AppendFormat(CultureInfo.InvariantCulture,
                        "{0:F6},{1:F4},{2:F4},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13:F6},{14:F4},{15:F4}\n",
                        f.Time, f.FrameMs, f.RttMs, f.Peers, f.Tick, f.Focus, f.Gc, f.Sent, f.Received,
                        f.QueueFull, f.Dropped, f.SendErrors, f.Backlog, f.BacklogAge, f.PollMs, f.FlushMs);
                }
                File.WriteAllText(stem + "-frames.csv", text.ToString());
                text.Clear(); text.Append("seconds,rawRttMs\n");
                for (int i = 0; i < _pongCount; i++)
                    text.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F4}\n", _pongs[i].Time, _pongs[i].RttMs);
                File.WriteAllText(stem + "-pongs.csv", text.ToString());
                File.WriteAllText(stem + ".json", JsonUtility.ToJson(new Metadata {
                    utc = _utc, role = _role, protocol = _protocol,
                    region = _region, scene = _scene, route = _route,
                    unity = Application.unityVersion, frames = _frameCount, pongs = _pongCount,
                    seconds = Time.realtimeSinceStartupAsDouble - _start, reason = reason
                }, true));
                Debug.Log("[RelayCapture] Saved " + _frameCount + " frames and " + _pongCount + " raw pongs.");
            }
            catch (IOException) { Debug.LogWarning("[RelayCapture] Could not write diagnostic files."); }
            catch (UnauthorizedAccessException) { Debug.LogWarning("[RelayCapture] Diagnostic directory is not writable."); }
            Destroy(gameObject);
        }

        private void OnApplicationQuit() => Finish("application_quit");
        private void OnDestroy() => Finish("capture_destroyed");
    }
}
