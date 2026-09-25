using System;
using BattlePvp.Diagnostics;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class PerformanceCaptureRulesTests
    {
        [Test]
        public void NearestRankPercentilesKeepTailFramesAndDoNotMutateInput()
        {
            var input = new double[100];
            for (int i = 0; i < input.Length; i++) input[i] = 100 - i;
            FrameStatistics result = FrameStatistics.Calculate(input);
            Assert.That(result.Count, Is.EqualTo(100));
            Assert.That(result.MeanMs, Is.EqualTo(50.5d));
            Assert.That(result.P95Ms, Is.EqualTo(95d));
            Assert.That(result.P99Ms, Is.EqualTo(99d));
            Assert.That(result.MaximumMs, Is.EqualTo(100d));
            Assert.That(input[0], Is.EqualTo(100d));
        }

        [Test]
        public void InvalidAndAbsentSamplesNeverBecomeZeroCostPasses()
        {
            Assert.That(PerformanceCaptureRules.EvaluateRun(null), Is.EqualTo(PerformanceVerdict.NoData));
            FrameStatistics result = FrameStatistics.Calculate(new[] { 16d, 0d, -1d, double.NaN, double.PositiveInfinity });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.InvalidCount, Is.EqualTo(4));
            PerformanceRunRecord run = DocumentedSyntheticRun();
            run.Frames = result;
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
            run.Frames = FrameStatistics.Calculate(Array.Empty<double>());
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.NoData));
        }

        [Test]
        public void SingleRunCannotStandInForThreeMeasurementsOrAnotherPlatform()
        {
            PerformanceRunRecord first = DocumentedSyntheticRun();
            Assert.That(PerformanceCaptureRules.EvaluateRun(first), Is.EqualTo(PerformanceVerdict.EligibleRunWithinBudget));
            Assert.That(PerformanceCaptureRules.EvaluateSeries(new[] { first }), Is.EqualTo(PerformanceVerdict.Incomplete));
            PerformanceRunRecord second = DocumentedSyntheticRun(2);
            PerformanceRunRecord third = DocumentedSyntheticRun(3);
            Assert.That(PerformanceCaptureRules.EvaluateSeries(new[] { first, second, third }), Is.EqualTo(PerformanceVerdict.SeriesWithinBudget));
            third.Context.Platform = "WebGLPlayer";
            third.Context.Browser = "Synthetic browser";
            Assert.That(PerformanceCaptureRules.EvaluateSeries(new[] { first, second, third }), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
            third = DocumentedSyntheticRun(2);
            Assert.That(PerformanceCaptureRules.EvaluateSeries(new[] { first, second, third }), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
        }

        [TestCase("players")]
        [TestCase("idle")]
        [TestCase("focus")]
        [TestCase("menu")]
        [TestCase("scene")]
        [TestCase("quality")]
        [TestCase("hardware")]
        [TestCase("editor")]
        public void LowFrameTimesDoNotOverrideMissingOrInvalidLoadConditions(string missing)
        {
            PerformanceRunRecord run = DocumentedSyntheticRun();
            switch (missing)
            {
                case "players": run.MinimumObservedPlayers = 7; break;
                case "idle": run.ActiveCombatWindows = 0; break;
                case "focus": run.FocusMaintained = false; break;
                case "menu": run.GameplayInputMaintained = false; break;
                case "scene": run.InBattleThroughout = false; break;
                case "quality": run.SettingsMaintained = false; break;
                case "hardware": run.Context.HardwareDescription = string.Empty; break;
                case "editor": run.Context.IsEditor = true; break;
            }
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
        }

        [Test]
        public void AbortedAndUnconfirmedRunsRetainTheirUnfinishedStatus()
        {
            PerformanceRunRecord run = DocumentedSyntheticRun();
            run.Completed = false;
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.Incomplete));
            run.Completed = true;
            run.OperatorConfirmedRepresentativeCombat = false;
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.AwaitingOperatorConfirmation));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidDurationsAreNotCompletedMeasurements(double invalid)
        {
            PerformanceRunRecord run = DocumentedSyntheticRun();
            run.WarmupSeconds = invalid;
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
            run.WarmupSeconds = 30d;
            run.CaptureSeconds = invalid;
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
        }

        [Test]
        public void OneFastFrameIsNotSixtySecondsOfObservation()
        {
            PerformanceRunRecord run = DocumentedSyntheticRun();
            run.Frames = FrameStatistics.Calculate(new[] { 16d });
            Assert.That(PerformanceCaptureRules.EvaluateRun(run), Is.EqualTo(PerformanceVerdict.IneligibleConditions));
        }

        [Test]
        public void EveryEligibleRunMustMeetTheBudget()
        {
            PerformanceRunRecord slow = DocumentedSyntheticRun(3);
            var frames = new double[3000];
            Array.Fill(frames, 20d);
            slow.Frames = FrameStatistics.Calculate(frames);
            Assert.That(PerformanceCaptureRules.EvaluateRun(slow), Is.EqualTo(PerformanceVerdict.EligibleRunExceedsBudget));
            Assert.That(PerformanceCaptureRules.EvaluateSeries(new[] { DocumentedSyntheticRun(1), DocumentedSyntheticRun(2), slow }),
                Is.EqualTo(PerformanceVerdict.SeriesExceedsBudget));
        }

        // Synthetic inputs validate analysis rules; they are never exported as measured performance results.
        private static PerformanceRunRecord DocumentedSyntheticRun(int number = 1)
        {
            var frames = new double[3750];
            Array.Fill(frames, 16d);
            return new PerformanceRunRecord
            {
                Context = new PerformanceCaptureContext
                {
                    Series = "Synthetic test", BuildId = "Test-only", Platform = "WindowsPlayer",
                    UnityVersion = "Test", BuildType = "Development", HardwareDescription = "Synthetic CPU/GPU/RAM",
                    Cpu = "Test CPU", Gpu = "Test GPU", MemoryMb = 16000, OperatingSystem = "Test OS",
                    Width = 1920, Height = 1080, Quality = "Test", NetworkRole = "Host",
                    NetworkConditions = "Synthetic network", Scenario = "Test fixture"
                },
                RunNumber = number, Completed = true, WarmupSeconds = 30d, CaptureSeconds = 60d,
                MinimumObservedPlayers = 8, MaximumObservedPlayers = 8, ConditionProbes = 61,
                CombatWindows = 6, ActiveCombatWindows = 6, OperatorConfirmedRepresentativeCombat = true,
                Frames = FrameStatistics.Calculate(frames)
            };
        }
    }
}
