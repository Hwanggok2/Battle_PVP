using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>입장 이후 종료 경로를 기록한다. 계정·방 ID와 인증 정보는 기록하지 않는다.</summary>
    public static class RoomConnectionDiagnostics
    {
        [Serializable] private sealed class Report
        {
            public string timeUtc;
            public bool host;
            public double secondsSinceJoin;
            public string[] events;
        }
        private static readonly List<string> Events = new List<string>(16);
        private static double _started;
        private static bool _host, _active, _saved;

        public static void Begin(bool host)
        {
            Events.Clear();
            _started = Time.realtimeSinceStartupAsDouble;
            _host = host; _active = Application.isPlaying; _saved = false;
            Record("room_entry_started");
        }

        public static void Record(string eventCode)
        {
            if (!_active || _saved) return;
            if (Events.Count == 16) Events.RemoveAt(0);
            Events.Add((Time.realtimeSinceStartupAsDouble - _started).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s: " + eventCode);
        }

        public static void SaveExit(string eventCode)
        {
            if (!_active || _saved) return;
            Record(eventCode);
            _saved = true;
            string summary = "[RoomConnection] " + string.Join(" | ", Events);
            if (eventCode == "room_left") Debug.Log(summary);
            else Debug.LogWarning(summary);
            try
            {
                string directory = Application.isEditor ? "Reports/NetworkDiagnostics" :
                    System.IO.Path.Combine(Application.persistentDataPath, "NetworkDiagnostics");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "latest-room-exit.json"), JsonUtility.ToJson(new Report
                {
                    timeUtc = DateTime.UtcNow.ToString("O"), host = _host,
                    secondsSinceJoin = Time.realtimeSinceStartupAsDouble - _started, events = Events.ToArray()
                }, true));
            }
            catch (System.IO.IOException) { Debug.LogWarning("[RoomConnection] Could not save the diagnostic report."); }
            catch (UnauthorizedAccessException) { Debug.LogWarning("[RoomConnection] Diagnostic directory is not writable."); }
        }
    }
}
