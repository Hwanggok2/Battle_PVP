using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BattlePvp.Diagnostics;
using BattlePvp.Networking;

internal static class RoomAndPerformanceRegression
{
    internal static void Run(Action<bool, string> require)
    {
        CheckRoomIdentity(require);
        CheckFrameStatistics(require);
        CheckRunEligibility(require);
        CheckSeriesEligibility(require);
        CheckCaptureFileAnalysis(require);
    }

    private static void CheckRoomIdentity(Action<bool, string> require)
    {
        Guid nonce = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        const string suffix = "0123456789abcdef0123456789abcdef";
        foreach (string account in new[] { "A1B2C3", "0", "a", "0123456789ABCDEF", "00aBcD" })
        {
            require(RoomIdentity.TryCreate(account, nonce, out string room), "Authenticated hex account must form a room id.");
            require(room == "battle_" + account.ToLowerInvariant() + "_" + suffix,
                "Creation must preserve leading zeros and normalize account/nonce to lowercase hex.");
            require(RoomIdentity.IsValid(room), "Created room id must pass format validation.");
            require(RoomIdentity.TryGetOwner(room, out string owner) && owner == account.ToLowerInvariant(),
                "Room owner must round-trip without treating the nonce as part of the account.");
        }

        foreach (string invalidAccount in new[] { null, "", " ", " AB12", "AB12 ", "AB_12", "AB-12",
            "AB:12", "G123", "１２", "abc\ndef", "abc\tdef" })
        {
            require(!RoomIdentity.TryCreate(invalidAccount, nonce, out string room) && room == null,
                "Creation must reject non-hex or whitespace account text.");
        }
        require(!RoomIdentity.TryCreate("a1b2", Guid.Empty, out string emptyRoom) && emptyRoom == null,
            "Creation must reject an accidentally uninitialized nonce.");
        require(RoomIdentity.IsValid("battle_a1b2_" + new string('0', 32)),
            "Format validation intentionally permits a zero nonce; format is not authorization.");
        require(RoomIdentity.TryCreate("a1b2", Guid.Parse("01234567-89ab-cdef-0123-456789abcdee"), out string other)
            && other != "battle_a1b2_" + suffix, "Different nonces must create different room ids.");

        string valid = "battle_a1b2_" + suffix;
        string[] invalidRooms =
        {
            null, "", "RoomRegistry", "GlobalRoomRegistry", nonce.ToString(), nonce.ToString("N"),
            "battle_", "battle__" + suffix, "battle_a1b2", "Battle_a1b2_" + suffix,
            "battle_A1B2_" + suffix, "battle_a1b2_" + suffix.ToUpperInvariant(),
            " " + valid, valid + " ", valid + "\n", "battle_a1 b2_" + suffix,
            "battle_a1_b2_" + suffix, "battle:a1b2:" + suffix, "battle-a1b2-" + suffix,
            "battle_a1b2_" + nonce.ToString(), "battle_a1b2_" + suffix.Substring(1),
            valid + "0", "battle_a1b2_g" + suffix.Substring(1), "battle_g123_" + suffix
        };
        foreach (string room in invalidRooms)
        {
            require(!RoomIdentity.IsValid(room), "Reserved/legacy/malformed room ids must be rejected.");
            require(!RoomIdentity.TryGetOwner(room, out string owner) && owner == null,
                "Rejected room ids must not expose a usable owner.");
        }
    }

    private static void CheckFrameStatistics(Action<bool, string> require)
    {
        var random = new Random(95899);
        for (int count = 1; count <= 100; count++)
        {
            double[] samples = Enumerable.Range(1, count).Select(x => (double)x)
                .OrderBy(_ => random.Next()).ToArray();
            double[] original = (double[])samples.Clone();
            FrameStatistics stats = FrameStatistics.Calculate(samples);
            require(stats.Count == count && stats.InvalidCount == 0, "Valid sample count must be retained.");
            require(stats.MeanMs == (count + 1d) / 2d, "Mean must use all valid frame intervals.");
            require(stats.P95Ms == Math.Ceiling(count * .95d), "p95 must use the nearest-rank definition.");
            require(stats.P99Ms == Math.Ceiling(count * .99d), "p99 must use the nearest-rank definition.");
            require(stats.MaximumMs == count, "Maximum must be the largest frame interval.");
            require(samples.SequenceEqual(original), "Statistics must not reorder the caller's capture buffer.");
        }

        FrameStatistics mixed = FrameStatistics.Calculate(new[] { 1d, double.NaN, 2d, double.PositiveInfinity,
            double.NegativeInfinity, 0d, -1d, 3d });
        require(mixed.Count == 3 && mixed.InvalidCount == 5, "Invalid frames must remain visible in metadata.");
        require(mixed.MeanMs == 2d && mixed.P95Ms == 3d && mixed.P99Ms == 3d && mixed.MaximumMs == 3d,
            "Invalid values must not contaminate the valid sample statistics.");
        FrameStatistics empty = FrameStatistics.Calculate(Array.Empty<double>());
        require(empty.Count == 0 && empty.InvalidCount == 0, "Empty capture is no data.");
        require(FrameStatistics.Calculate(null).Count == 0, "Missing sample list is no data.");
        FrameStatistics invalidOnly = FrameStatistics.Calculate(new[] { double.NaN, 0d, -1d });
        require(invalidOnly.Count == 0 && invalidOnly.InvalidCount == 3, "All-invalid capture must retain rejection evidence.");
    }

    private static void CheckRunEligibility(Action<bool, string> require)
    {
        require(PerformanceCaptureRules.EvaluateRun(CreateRun(1)) == PerformanceVerdict.EligibleRunWithinBudget,
            "A fully documented synthetic 8-player Windows run should be eligible.");
        require(PerformanceCaptureRules.EvaluateRun(CreateRun(1, "WebGLPlayer")) == PerformanceVerdict.EligibleRunWithinBudget,
            "A documented WebGL player run with browser identity should be eligible.");
        PerformanceRunRecord exactBudget = CreateRun(1, sampleCount: 3600);
        require(exactBudget.Frames.P95Ms == PerformanceCaptureRules.FrameBudgetMs,
            "Boundary fixture must be exactly 1000/60 milliseconds.");
        require(PerformanceCaptureRules.EvaluateRun(exactBudget) == PerformanceVerdict.EligibleRunWithinBudget,
            "Exactly 60 FPS at p95 is inside the budget.");
        require(PerformanceCaptureRules.EvaluateRun(CreateRun(1, sampleCount: 3599)) == PerformanceVerdict.EligibleRunExceedsBudget,
            "A p95 above the exact 60 FPS boundary must fail the budget.");

        require(PerformanceCaptureRules.EvaluateRun(null) == PerformanceVerdict.NoData, "Missing run cannot pass.");
        ExpectRun(require, run => run.Frames = null, PerformanceVerdict.NoData, "Missing frame summary");
        ExpectRun(require, run => run.Frames.Count = 0, PerformanceVerdict.NoData, "Empty frame summary");
        ExpectRun(require, run => { run.Completed = false; run.StopReason = "Operator interrupted"; },
            PerformanceVerdict.Incomplete, "Interrupted capture");
        ExpectRun(require, run => run.WarmupSeconds = 29.999d, PerformanceVerdict.Incomplete, "Short warmup");
        PerformanceRunRecord shortRun = CreateRun(1, captureSeconds: 59.999d);
        require(PerformanceCaptureRules.EvaluateRun(shortRun) == PerformanceVerdict.Incomplete,
            "Consistent but short capture is incomplete.");
        ExpectRun(require, run => run.OperatorConfirmedRepresentativeCombat = false,
            PerformanceVerdict.AwaitingOperatorConfirmation, "Unconfirmed representative combat");

        var conditions = new (string Name, Action<PerformanceRunRecord> Change)[]
        {
            ("Missing context", run => run.Context = null),
            ("Seven players", run => run.MinimumObservedPlayers = 7),
            ("Player count exceeded", run => run.MaximumObservedPlayers = 9),
            ("Too few condition probes", run => run.ConditionProbes = 59),
            ("Outside battle", run => run.InBattleThroughout = false),
            ("Focus lost", run => run.FocusMaintained = false),
            ("Settings changed", run => run.SettingsMaintained = false),
            ("Gameplay input blocked", run => run.GameplayInputMaintained = false),
            ("Too few activity windows", run => run.CombatWindows = 5),
            ("No combat in one window", run => run.ActiveCombatWindows = 5),
            ("Too many activity windows", run => { run.CombatWindows = 7; run.ActiveCombatWindows = 7; }),
            ("Invalid frame metadata", run => run.Frames.InvalidCount = 1),
            ("Negative frame metadata", run => run.Frames.InvalidCount = -1),
            ("Negative frame count", run => run.Frames.Count = -1),
            ("Uninitialized run number", run => run.RunNumber = 0),
            ("Run number above required series", run => run.RunNumber = 4),
            ("Editor", run => run.Context.IsEditor = true),
            ("Unsupported platform", run => run.Context.Platform = "LinuxPlayer"),
            ("Empty platform", run => run.Context.Platform = ""),
            ("WebGL missing browser", run => { run.Context.Platform = "WebGLPlayer"; run.Context.Browser = ""; }),
            ("Invalid network role", run => run.Context.NetworkRole = "Observer"),
            ("Zero screen width", run => run.Context.Width = 0),
            ("Zero screen height", run => run.Context.Height = 0),
            ("No series id", run => run.Context.Series = ""),
            ("No build id", run => run.Context.BuildId = ""),
            ("No hardware description", run => run.Context.HardwareDescription = " "),
            ("No CPU identity", run => run.Context.Cpu = ""),
            ("No GPU identity", run => run.Context.Gpu = ""),
            ("No OS identity", run => run.Context.OperatingSystem = ""),
            ("No quality setting", run => run.Context.Quality = ""),
            ("No build type", run => run.Context.BuildType = ""),
            ("No Unity version", run => run.Context.UnityVersion = ""),
            ("No network conditions", run => run.Context.NetworkConditions = ""),
            ("No workload scenario", run => run.Context.Scenario = "")
        };
        foreach (var condition in conditions)
            ExpectRun(require, condition.Change, PerformanceVerdict.IneligibleConditions, condition.Name);

        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            ExpectRun(require, run => run.WarmupSeconds = invalid, PerformanceVerdict.IneligibleConditions,
                "Invalid warmup duration");
            ExpectRun(require, run => run.CaptureSeconds = invalid, PerformanceVerdict.IneligibleConditions,
                "Invalid capture duration");
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -1d })
        {
            ExpectRun(require, run => run.Frames.MeanMs = invalid, PerformanceVerdict.IneligibleConditions, "Invalid mean");
            ExpectRun(require, run => run.Frames.P95Ms = invalid, PerformanceVerdict.IneligibleConditions, "Invalid p95");
            ExpectRun(require, run => run.Frames.P99Ms = invalid, PerformanceVerdict.IneligibleConditions, "Invalid p99");
            ExpectRun(require, run => run.Frames.MaximumMs = invalid, PerformanceVerdict.IneligibleConditions, "Invalid maximum");
        }

        ExpectRun(require, run => run.Frames.P95Ms = run.Frames.P99Ms + 1d,
            PerformanceVerdict.IneligibleConditions, "p95 above p99");
        ExpectRun(require, run => run.Frames.P99Ms = run.Frames.MaximumMs + 1d,
            PerformanceVerdict.IneligibleConditions, "p99 above maximum");
        ExpectRun(require, run => run.Frames.MeanMs = run.Frames.MaximumMs + 1d,
            PerformanceVerdict.IneligibleConditions, "Mean above maximum");
        ExpectRun(require, run => run.Frames.MaximumMs = 60001d,
            PerformanceVerdict.IneligibleConditions, "Maximum above total captured duration");
        ExpectRun(require, run => run.CaptureSeconds = 61d,
            PerformanceVerdict.IneligibleConditions, "Summary total mismatches capture duration");
        ExpectRun(require, run => run.Frames.Count++,
            PerformanceVerdict.IneligibleConditions, "Sample count mismatches duration");

        PerformanceRunRecord tiny95 = CreateRun(1, sampleCount: 19);
        tiny95.Frames.P95Ms = 1d;
        require(PerformanceCaptureRules.EvaluateRun(tiny95) == PerformanceVerdict.IneligibleConditions,
            "For fewer than 20 samples p95 must be the maximum.");
        PerformanceRunRecord tiny99 = CreateRun(1, sampleCount: 99);
        tiny99.Frames.P95Ms = 1d;
        tiny99.Frames.P99Ms = 1d;
        require(PerformanceCaptureRules.EvaluateRun(tiny99) == PerformanceVerdict.IneligibleConditions,
            "For fewer than 100 samples p99 must be the maximum.");
        PerformanceRunRecord overflow = CreateRun(1);
        overflow.Frames.Count = int.MaxValue;
        overflow.Frames.MeanMs = overflow.Frames.P95Ms = overflow.Frames.P99Ms = overflow.Frames.MaximumMs = double.MaxValue;
        require(PerformanceCaptureRules.EvaluateRun(overflow) == PerformanceVerdict.IneligibleConditions,
            "Finite summary fields with an overflowing aggregate cannot pass.");

        PerformanceRunRecord webNoMemoryProbe = CreateRun(1, "WebGLPlayer");
        webNoMemoryProbe.Context.MemoryMb = 0;
        require(PerformanceCaptureRules.EvaluateRun(webNoMemoryProbe) == PerformanceVerdict.EligibleRunWithinBudget,
            "Unavailable WebGL memory probe is allowed when physical RAM is documented in hardware description.");
    }

    private static void CheckSeriesEligibility(Action<bool, string> require)
    {
        require(PerformanceCaptureRules.EvaluateSeries(null) == PerformanceVerdict.NoData, "Missing series cannot pass.");
        require(PerformanceCaptureRules.EvaluateSeries(Array.Empty<PerformanceRunRecord>()) == PerformanceVerdict.NoData,
            "Empty series cannot pass.");
        require(PerformanceCaptureRules.EvaluateSeries(new[] { CreateRun(1) }) == PerformanceVerdict.Incomplete,
            "One good run is not a three-run performance pass.");
        require(PerformanceCaptureRules.EvaluateSeries(new[] { CreateRun(1), CreateRun(2) }) == PerformanceVerdict.Incomplete,
            "Two good runs are incomplete.");
        require(PerformanceCaptureRules.EvaluateSeries(new[] { CreateRun(1), CreateRun(2), CreateRun(3), CreateRun(3) })
            == PerformanceVerdict.Incomplete, "An extra record must not be silently ignored.");

        foreach (string platform in new[] { "WindowsPlayer", "WebGLPlayer" })
        {
            PerformanceRunRecord[] runs = { CreateRun(3, platform), CreateRun(1, platform), CreateRun(2, platform) };
            require(PerformanceCaptureRules.EvaluateSeries(runs) == PerformanceVerdict.SeriesWithinBudget,
                "Three compatible distinct runs may pass regardless of input order.");
        }
        PerformanceRunRecord[] atBoundary =
        {
            CreateRun(1, sampleCount: 3600), CreateRun(2, sampleCount: 3600), CreateRun(3, sampleCount: 3600)
        };
        require(PerformanceCaptureRules.EvaluateSeries(atBoundary) == PerformanceVerdict.SeriesWithinBudget,
            "All three runs at the exact budget should pass.");
        PerformanceRunRecord[] overBudget = { CreateRun(1), CreateRun(2, sampleCount: 3599), CreateRun(3) };
        require(PerformanceCaptureRules.EvaluateSeries(overBudget) == PerformanceVerdict.SeriesExceedsBudget,
            "A slower run must not be averaged away by faster runs.");
        require(PerformanceCaptureRules.EvaluateSeries(new[] { CreateRun(1), CreateRun(1), CreateRun(3) })
            == PerformanceVerdict.IneligibleConditions, "Repeated run numbers cannot replace a missing repeat.");
        require(PerformanceCaptureRules.EvaluateSeries(new[] { CreateRun(1), CreateRun(2, "WebGLPlayer"), CreateRun(3) })
            == PerformanceVerdict.IneligibleConditions, "Windows and WebGL need separate series.");

        var mismatches = new (string Name, Action<PerformanceCaptureContext> Change)[]
        {
            ("series", context => context.Series = "other-series"),
            ("build", context => context.BuildId = "other-build"),
            ("Unity version", context => context.UnityVersion = "other-version"),
            ("build type", context => context.BuildType = "Development"),
            ("scene", context => context.Scene = "Battle_waiting"),
            ("hardware description", context => context.HardwareDescription = "other-machine"),
            ("CPU", context => context.Cpu = "other-cpu"),
            ("GPU", context => context.Gpu = "other-gpu"),
            ("memory", context => context.MemoryMb++),
            ("OS", context => context.OperatingSystem = "other-os"),
            ("browser", context => context.Browser = "other-browser"),
            ("width", context => context.Width++),
            ("height", context => context.Height++),
            ("quality", context => context.Quality = "Low"),
            ("VSync", context => context.VSyncCount = 1),
            ("target frame rate", context => context.TargetFrameRate = 120),
            ("role", context => context.NetworkRole = "Client"),
            ("network conditions", context => context.NetworkConditions = "100 ms delay"),
            ("workload", context => context.Scenario = "idle"),
            ("multiple client arrangement", context => context.MultipleClientsOnThisDevice = true)
        };
        foreach (var mismatch in mismatches)
        {
            PerformanceRunRecord[] runs = { CreateRun(1), CreateRun(2), CreateRun(3) };
            mismatch.Change(runs[1].Context);
            require(PerformanceCaptureRules.EvaluateRun(runs[1]) == PerformanceVerdict.EligibleRunWithinBudget,
                "Mixed-condition fixture must remain individually eligible: " + mismatch.Name);
            require(PerformanceCaptureRules.EvaluateSeries(runs) == PerformanceVerdict.IneligibleConditions,
                "Conditions must match across all repeats: " + mismatch.Name);
        }
        PerformanceRunRecord[] unconfirmed = { CreateRun(1), CreateRun(2), CreateRun(3) };
        unconfirmed[1].OperatorConfirmedRepresentativeCombat = false;
        require(PerformanceCaptureRules.EvaluateSeries(unconfirmed) == PerformanceVerdict.AwaitingOperatorConfirmation,
            "An unconfirmed repeat cannot join a passing series.");
        PerformanceRunRecord[] interrupted = { CreateRun(1), CreateRun(2), CreateRun(3) };
        interrupted[2].Completed = false;
        require(PerformanceCaptureRules.EvaluateSeries(interrupted) == PerformanceVerdict.Incomplete,
            "An interrupted repeat cannot join a passing series.");
    }

    private static void ExpectRun(Action<bool, string> require, Action<PerformanceRunRecord> mutate,
        PerformanceVerdict expected, string label)
    {
        PerformanceRunRecord run = CreateRun(1);
        mutate(run);
        PerformanceVerdict actual = PerformanceCaptureRules.EvaluateRun(run);
        require(actual == expected, label + ": expected " + expected + ", got " + actual + ".");
    }

    private static void CheckCaptureFileAnalysis(Action<bool, string> require)
    {
        string directory = Path.Combine(Path.GetTempPath(), "BattlePvpSyntheticAnalysis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, "synthetic-run-" + i + ".json")).ToArray();
        var options = new JsonSerializerOptions { IncludeFields = true };
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                PerformanceRunRecord run = CreateRun(i + 1);
                run.Verdict = "SeriesExceedsBudget";
                File.WriteAllText(paths[i], JsonSerializer.Serialize(run, options));
            }
            using var output = new StringWriter();
            require(PerformanceCaptureAnalysis.Run(paths, output) == 0,
                "Valid field-based JSON fixtures should analyze successfully regardless of stored Verdict.");
            require(output.ToString().Contains("Series: SeriesWithinBudget") && output.ToString().Contains("p95=16 ms"),
                "File analysis must display recalculated series outcome and actual field values.");
            require(output.ToString().Contains("Conditions: EligibleRunWithinBudget"),
                "Each file must display its recalculated condition verdict.");
            require(PerformanceCaptureAnalysis.Run(new[] { paths[0], paths[0], paths[2] }, TextWriter.Null) == 1,
                "The same file twice must not produce a successful exit status.");

            PerformanceRunRecord duplicate = CreateRun(1);
            File.WriteAllText(paths[1], JsonSerializer.Serialize(duplicate, options));
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 1,
                "Different files containing the same run number must not pass.");
            PerformanceRunRecord mixedPlatform = CreateRun(2, "WebGLPlayer");
            File.WriteAllText(paths[1], JsonSerializer.Serialize(mixedPlatform, options));
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 1,
                "Mixed-platform JSON files must not produce a successful exit status.");

            PerformanceRunRecord incomplete = CreateRun(2);
            incomplete.Completed = false;
            incomplete.Verdict = "SeriesWithinBudget";
            File.WriteAllText(paths[1], JsonSerializer.Serialize(incomplete, options));
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 1,
                "A forged stored success verdict cannot make an incomplete capture pass.");
            File.WriteAllText(paths[1], JsonSerializer.Serialize(CreateRun(2, sampleCount: 3599), options));
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 1,
                "An over-budget JSON run must not produce a successful exit status.");
            File.WriteAllText(paths[1], "null");
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 1,
                "A syntactically valid but missing record is not a pass.");
            File.WriteAllText(paths[1], "{invalid JSON");
            require(PerformanceCaptureAnalysis.Run(paths, TextWriter.Null) == 2,
                "Malformed JSON must produce an input-error exit status.");
            require(PerformanceCaptureAnalysis.Run(new[] { paths[0], Path.Combine(directory, "missing.json"), paths[2] }, TextWriter.Null) == 2,
                "Missing capture files must produce an input-error exit status.");
            require(PerformanceCaptureAnalysis.Run(paths.Take(2).ToArray(), TextWriter.Null) == 2,
                "The analyzer requires exactly three supplied files.");
        }
        finally
        {
            foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static PerformanceRunRecord CreateRun(int number, string platform = "WindowsPlayer",
        int sampleCount = 3750, double captureSeconds = 60d)
    {
        double frameMs = captureSeconds * 1000d / sampleCount;
        return new PerformanceRunRecord
        {
            Context = new PerformanceCaptureContext
            {
                Series = "synthetic-test-series",
                BuildId = "synthetic-build",
                Platform = platform,
                IsEditor = false,
                UnityVersion = "synthetic-version",
                BuildType = "Release",
                Scene = "Battle",
                HardwareDescription = "Synthetic CPU/GPU/16 GB RAM fixture; not physical measurement",
                Cpu = "Synthetic CPU",
                Gpu = "Synthetic GPU",
                MemoryMb = 16384,
                OperatingSystem = "Synthetic OS",
                Browser = platform == "WebGLPlayer" ? "Synthetic browser" : "",
                Width = 1920,
                Height = 1080,
                Quality = "High",
                VSyncCount = 0,
                TargetFrameRate = 60,
                NetworkRole = "Host",
                NetworkConditions = "Synthetic local network fixture",
                Scenario = "Synthetic representative combat fixture",
                MultipleClientsOnThisDevice = false
            },
            StartedUtc = "2026-09-06T00:00:00Z",
            RunNumber = number,
            Completed = true,
            WarmupSeconds = 30d,
            CaptureSeconds = captureSeconds,
            MinimumObservedPlayers = 8,
            MaximumObservedPlayers = 8,
            ConditionProbes = 60,
            InBattleThroughout = true,
            FocusMaintained = true,
            SettingsMaintained = true,
            GameplayInputMaintained = true,
            CombatWindows = 6,
            ActiveCombatWindows = 6,
            OperatorConfirmedRepresentativeCombat = true,
            Frames = FrameStatistics.Calculate(Enumerable.Repeat(frameMs, sampleCount).ToArray())
        };
    }
}
