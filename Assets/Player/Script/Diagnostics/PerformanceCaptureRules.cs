using System;
using System.Collections.Generic;

namespace BattlePvp.Diagnostics
{
    [Serializable]
    public sealed class FrameStatistics
    {
        public int Count;
        public int InvalidCount;
        public double MeanMs;
        public double P95Ms;
        public double P99Ms;
        public double MaximumMs;

        public static FrameStatistics Calculate(IReadOnlyList<double> milliseconds)
        {
            var result = new FrameStatistics();
            if (milliseconds == null || milliseconds.Count == 0) return result;
            var sorted = new double[milliseconds.Count];
            double total = 0d;
            for (int i = 0; i < milliseconds.Count; i++)
            {
                double value = milliseconds[i];
                if (!double.IsFinite(value) || value <= 0d) { result.InvalidCount++; continue; }
                sorted[result.Count++] = value;
                total += value;
            }
            if (result.Count == 0) return result;
            Array.Sort(sorted, 0, result.Count);
            result.MeanMs = total / result.Count;
            result.P95Ms = sorted[(int)Math.Ceiling(result.Count * 0.95d) - 1];
            result.P99Ms = sorted[(int)Math.Ceiling(result.Count * 0.99d) - 1];
            result.MaximumMs = sorted[result.Count - 1];
            return result;
        }
    }

    [Serializable]
    public sealed class PerformanceCaptureContext
    {
        public string Series;
        public string BuildId;
        public string Platform;
        public bool IsEditor;
        public string UnityVersion;
        public string BuildType;
        public string Scene;
        public string HardwareDescription;
        public string Cpu;
        public string Gpu;
        public int MemoryMb;
        public string OperatingSystem;
        public string Browser;
        public int Width;
        public int Height;
        public string Quality;
        public int VSyncCount;
        public int TargetFrameRate;
        public string NetworkRole;
        public string NetworkConditions;
        public string Scenario;
        public bool MultipleClientsOnThisDevice;

        public bool IsDocumented =>
            !string.IsNullOrWhiteSpace(Series) && !string.IsNullOrWhiteSpace(BuildId) &&
            !string.IsNullOrWhiteSpace(HardwareDescription) && !string.IsNullOrWhiteSpace(Cpu) &&
            !string.IsNullOrWhiteSpace(Gpu) && !string.IsNullOrWhiteSpace(OperatingSystem) &&
            !string.IsNullOrWhiteSpace(Quality) && !string.IsNullOrWhiteSpace(BuildType) &&
            !string.IsNullOrWhiteSpace(UnityVersion) && !string.IsNullOrWhiteSpace(NetworkConditions) &&
            !string.IsNullOrWhiteSpace(Scenario) && Width > 0 && Height > 0 &&
            (NetworkRole == "Host" || NetworkRole == "Client") &&
            (Platform != "WebGLPlayer" || !string.IsNullOrWhiteSpace(Browser));

        public bool Matches(PerformanceCaptureContext other) => other != null &&
            Series == other.Series && BuildId == other.BuildId && Platform == other.Platform &&
            IsEditor == other.IsEditor && UnityVersion == other.UnityVersion && BuildType == other.BuildType && Scene == other.Scene &&
            HardwareDescription == other.HardwareDescription && Cpu == other.Cpu && Gpu == other.Gpu &&
            MemoryMb == other.MemoryMb && OperatingSystem == other.OperatingSystem && Browser == other.Browser &&
            Width == other.Width && Height == other.Height && Quality == other.Quality &&
            VSyncCount == other.VSyncCount && TargetFrameRate == other.TargetFrameRate &&
            NetworkRole == other.NetworkRole && NetworkConditions == other.NetworkConditions &&
            Scenario == other.Scenario && MultipleClientsOnThisDevice == other.MultipleClientsOnThisDevice;
    }

    [Serializable]
    public sealed class PerformanceRunRecord
    {
        public PerformanceCaptureContext Context;
        public string StartedUtc;
        public int RunNumber;
        public bool Completed;
        public string StopReason;
        public double WarmupSeconds;
        public double CaptureSeconds;
        public int MinimumObservedPlayers;
        public int MaximumObservedPlayers;
        public int ConditionProbes;
        public bool InBattleThroughout = true;
        public bool FocusMaintained = true;
        public bool SettingsMaintained = true;
        public bool GameplayInputMaintained = true;
        public int CombatWindows;
        public int ActiveCombatWindows;
        public bool OperatorConfirmedRepresentativeCombat;
        public FrameStatistics Frames;
        public string Verdict;
        public string Scope = "One local frame-interval run. Three compatible eligible runs are required per platform; this file alone is not a performance pass.";
        public string ActivityRule = "Conservative tool eligibility filter, separate from the project's p95 FPS criterion: each complete 10-second window requires movement from at least 4 distinct players and accepted HP damage. Operator workload confirmation is also required.";
        public string Unmeasured = "CPU/GPU frame breakdown, GC allocation/collections, transport bytes/messages and before/after improvement are not measured by this tool.";
        public string HardwareCaptureNote = "SystemInfo may be unavailable or approximate, particularly in WebGL. MemoryMb=0 means unavailable. HardwareDescription must identify physical CPU/GPU/RAM.";
    }

    public enum PerformanceVerdict
    {
        NoData,
        Incomplete,
        IneligibleConditions,
        AwaitingOperatorConfirmation,
        EligibleRunWithinBudget,
        EligibleRunExceedsBudget,
        SeriesWithinBudget,
        SeriesExceedsBudget
    }

    public static class PerformanceCaptureRules
    {
        public const double WarmupSeconds = 30d;
        public const double CaptureSeconds = 60d;
        public const double FrameBudgetMs = 1000d / 60d;
        public const int RequiredPlayers = 8;
        public const int RequiredRuns = 3;
        public const double CombatWindowSeconds = 10d;

        public static PerformanceVerdict EvaluateRun(PerformanceRunRecord run)
        {
            if (run?.Frames == null || run.Frames.Count == 0) return PerformanceVerdict.NoData;
            if (!double.IsFinite(run.WarmupSeconds) || !double.IsFinite(run.CaptureSeconds) ||
                run.WarmupSeconds < 0d || run.CaptureSeconds < 0d || !HasConsistentStatistics(run))
                return PerformanceVerdict.IneligibleConditions;
            if (!run.Completed || run.WarmupSeconds < WarmupSeconds || run.CaptureSeconds < CaptureSeconds)
                return PerformanceVerdict.Incomplete;
            if (run.Context == null || !run.Context.IsDocumented || run.Context.IsEditor ||
                (run.Context.Platform != "WindowsPlayer" && run.Context.Platform != "WebGLPlayer") ||
                run.RunNumber < 1 || run.RunNumber > RequiredRuns || run.Frames.InvalidCount != 0 ||
                run.MinimumObservedPlayers != RequiredPlayers || run.MaximumObservedPlayers != RequiredPlayers ||
                run.ConditionProbes < 60 || !run.InBattleThroughout || !run.FocusMaintained ||
                !run.SettingsMaintained || !run.GameplayInputMaintained ||
                run.CombatWindows < 6 || run.ActiveCombatWindows != run.CombatWindows ||
                run.CombatWindows * CombatWindowSeconds > run.CaptureSeconds ||
                (run.CombatWindows + 1d) * CombatWindowSeconds <= run.CaptureSeconds)
                return PerformanceVerdict.IneligibleConditions;
            if (!run.OperatorConfirmedRepresentativeCombat) return PerformanceVerdict.AwaitingOperatorConfirmation;
            return run.Frames.P95Ms <= FrameBudgetMs
                ? PerformanceVerdict.EligibleRunWithinBudget : PerformanceVerdict.EligibleRunExceedsBudget;
        }

        private static bool HasConsistentStatistics(PerformanceRunRecord run)
        {
            FrameStatistics frames = run.Frames;
            if (frames.Count < 1 || frames.InvalidCount != 0 ||
                !double.IsFinite(frames.MeanMs) || !double.IsFinite(frames.P95Ms) ||
                !double.IsFinite(frames.P99Ms) || !double.IsFinite(frames.MaximumMs) ||
                frames.MeanMs <= 0d || frames.P95Ms <= 0d || frames.P95Ms > frames.P99Ms ||
                frames.P99Ms > frames.MaximumMs || frames.MeanMs > frames.MaximumMs + 0.000001d)
                return false;
            double totalMs = frames.MeanMs * frames.Count;
            if (!double.IsFinite(totalMs) || frames.MaximumMs > totalMs + 0.000001d ||
                Math.Abs(totalMs - run.CaptureSeconds * 1000d) > Math.Max(1d, totalMs * 0.000001d))
                return false;
            // For small sample sets these nearest-rank percentiles must be the final sample.
            if (frames.Count < 20 && frames.P95Ms != frames.MaximumMs) return false;
            if (frames.Count < 100 && frames.P99Ms != frames.MaximumMs) return false;
            return true;
        }

        public static PerformanceVerdict EvaluateSeries(IReadOnlyList<PerformanceRunRecord> runs)
        {
            if (runs == null || runs.Count == 0) return PerformanceVerdict.NoData;
            if (runs.Count != RequiredRuns) return PerformanceVerdict.Incomplete;
            int seen = 0;
            bool exceeds = false;
            for (int i = 0; i < runs.Count; i++)
            {
                PerformanceVerdict verdict = EvaluateRun(runs[i]);
                if (verdict != PerformanceVerdict.EligibleRunWithinBudget && verdict != PerformanceVerdict.EligibleRunExceedsBudget)
                    return verdict;
                if (!runs[0].Context.Matches(runs[i].Context)) return PerformanceVerdict.IneligibleConditions;
                int bit = 1 << runs[i].RunNumber;
                if ((seen & bit) != 0) return PerformanceVerdict.IneligibleConditions;
                seen |= bit;
                exceeds |= verdict == PerformanceVerdict.EligibleRunExceedsBudget;
            }
            return exceeds ? PerformanceVerdict.SeriesExceedsBudget : PerformanceVerdict.SeriesWithinBudget;
        }
    }
}
