using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    public static class RelayNetworkValidation
    {
        public static string Build(bool web = false)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes first.");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            const string scene = "Assets/Remodel/Validation/RelayNetworkProbe.unity";
            try
            {
                var created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Relay network probe").AddComponent<Validation.RelayNetworkProbe>();
                new GameObject("Probe camera").AddComponent<Camera>();
                new GameObject("Probe light").AddComponent<Light>().type = LightType.Directional;
                EditorSceneManager.SaveScene(created, scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
            var compression = PlayerSettings.WebGL.compressionFormat;
            try
            {
            if (web) PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { scene }, locationPathName = web ? "Builds/WebRtcProbe" : "Builds/RelayNetworkProbe/RelayNetworkProbe.exe",
                target = web ? BuildTarget.WebGL : BuildTarget.StandaloneWindows64, options = web ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = new[] { "BATTLE_PVP_RELAY_PROBE" }
            });
            Directory.CreateDirectory("Reports/PingDiagnosis");
            string summary = report.summary.result + " errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings;
            File.WriteAllText("Reports/PingDiagnosis/relay-probe-build.txt", summary);
            return summary;
            }
            finally { PlayerSettings.WebGL.compressionFormat = compression; }
        }

        [MenuItem("Tools/Battle PVP/Capture Relay Latency (120 seconds)")]
        public static void Capture()
        {
            if (EditorApplication.isPlaying) Diagnostics.RelayLatencyCapture.Arm(120, "Reports/PingDiagnosis/live");
        }
    }
}
