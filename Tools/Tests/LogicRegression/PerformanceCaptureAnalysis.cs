using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using BattlePvp.Diagnostics;

internal static class PerformanceCaptureAnalysis
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };

    internal static int Run(IReadOnlyList<string> paths, TextWriter output)
    {
        if (paths == null || paths.Count != PerformanceCaptureRules.RequiredRuns)
        {
            output.WriteLine("Usage: LogicRegression --analyze-performance path1 path2 path3");
            return 2;
        }

        var runs = new PerformanceRunRecord[paths.Count];
        var seenPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        output.WriteLine("Recomputing supplied capture summaries with production rules; stored Verdict fields are ignored.");
        output.WriteLine("This command does not run the game or independently verify capture-file provenance.");
        for (int i = 0; i < paths.Count; i++)
        {
            string path;
            try
            {
                path = Path.GetFullPath(paths[i]);
                if (!seenPaths.Add(path))
                {
                    output.WriteLine("REJECTED: repeated capture file: " + path);
                    return 1;
                }
                runs[i] = JsonSerializer.Deserialize<PerformanceRunRecord>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is JsonException || error is ArgumentException || error is NotSupportedException)
            {
                output.WriteLine("ERROR reading capture " + paths[i] + ": " + error.Message);
                return 2;
            }

            PerformanceRunRecord run = runs[i];
            PerformanceVerdict verdict = PerformanceCaptureRules.EvaluateRun(run);
            output.WriteLine(path);
            output.WriteLine("  Run=" + (run?.RunNumber.ToString(CultureInfo.InvariantCulture) ?? "missing") +
                ", platform=" + (run?.Context?.Platform ?? "missing") +
                ", samples=" + (run?.Frames?.Count.ToString(CultureInfo.InvariantCulture) ?? "missing") +
                ", invalid=" + (run?.Frames?.InvalidCount.ToString(CultureInfo.InvariantCulture) ?? "missing") +
                ", p95=" + Number(run?.Frames?.P95Ms) + " ms, p99=" + Number(run?.Frames?.P99Ms) +
                " ms, max=" + Number(run?.Frames?.MaximumMs) + " ms");
            output.WriteLine("  Conditions: " + verdict +
                "; completed=" + (run?.Completed.ToString() ?? "missing") +
                ", warmup=" + Number(run?.WarmupSeconds) + " s, capture=" + Number(run?.CaptureSeconds) +
                " s, players=" + (run == null ? "missing" : run.MinimumObservedPlayers + ".." + run.MaximumObservedPlayers) +
                ", active windows=" + (run == null ? "missing" : run.ActiveCombatWindows + "/" + run.CombatWindows) +
                ", representative combat confirmed=" + (run?.OperatorConfirmedRepresentativeCombat.ToString() ?? "missing"));
        }

        PerformanceVerdict series = PerformanceCaptureRules.EvaluateSeries(runs);
        output.WriteLine("Series: " + series + "; p95 budget=" + Number(PerformanceCaptureRules.FrameBudgetMs) + " ms.");
        output.WriteLine("Scope: three compatible local frame-interval runs for one platform. CPU/GPU breakdown, GC, network traffic, and before/after improvement are not established by this analysis.");
        return series == PerformanceVerdict.SeriesWithinBudget ? 0 : 1;
    }

    private static string Number(double? value) => value.HasValue
        ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "missing";
}
