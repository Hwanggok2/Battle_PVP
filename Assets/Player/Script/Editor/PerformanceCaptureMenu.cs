using BattlePvp.Diagnostics;
using UnityEditor;

namespace BattlePvp.EditorTools
{
    public static class PerformanceCaptureMenu
    {
        [MenuItem("Tools/Battle PVP/Development Frame Capture")]
        private static void Open() => DevelopmentPerformanceCapture.Open();

        [MenuItem("Tools/Battle PVP/Development Frame Capture", true)]
        private static bool CanOpen() => EditorApplication.isPlaying;
    }
}
