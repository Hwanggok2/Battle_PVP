using System;
using System.Collections.Generic;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.UI;

internal static class Program
{
    private static int _checks;
    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (args[0] == "--analyze-performance")
                return PerformanceCaptureAnalysis.Run(args.Skip(1).ToArray(), Console.Out);
            Console.Error.WriteLine("Usage: LogicRegression [--analyze-performance path1 path2 path3]");
            return 2;
        }
        foreach (int capacity in new[] { 1, 3, 64 }) CheckRing(capacity);
        CheckRanks();
        CheckPredictions();
        CheckInput();
        CheckRespawnCountdown();
        RoomAndPerformanceRegression.Run(Require);
        MatchLedgerRegression.Run(Require);
        RoomFlowRegression.Run(Require);
        HostRoomLeaseRegression.Run(Require);
        int existingChecks = _checks;
        BattleRuntimeRegression.Run(Require);
        RoomServiceTimeoutRegression.Run(Require);
        CombatExecutionRegression.Run(Require);
        ProfileRequestQueueRegression.Run(Require);
        MovementPermissionRegression.Run(Require);
        ChatPermissionRegression.Run(Require);
        AuthenticationFlowRegression.Run(Require);
        ForcedMotionRegression.Run(Require);
        ProfileWriteLifetimeRegression.Run(Require);
        UserDisplayTextRegression.Run(Require);
        CombatSkillPlanRegression.Run(Require);
        RoomSessionCacheRegression.Run(Require);
        GameplayFollowUpRegression.Run(Require);
        Console.WriteLine($"PASS: {_checks} assertions against linked production sources; {existingChecks} existing assertions preserved, {_checks - existingChecks} follow-up assertions added.");
        Console.WriteLine("Synthetic input regression only: no actual FPS measurement or performance acceptance is claimed. Unity lifecycle, physics, network, Windows/WebGL workload, and hardware execution are not covered.");
        return 0;
    }

    private static void CheckRing(int capacity)
    {
        var actual = new FixedRingBuffer<int>(capacity);
        var expected = new Queue<int>();
        var random = new Random(486 + capacity);
        for (int step = 0; step < 5000; step++)
        {
            int operation = random.Next(10);
            if (operation < 7)
            {
                if (expected.Count == capacity) expected.Dequeue();
                expected.Enqueue(step);
                actual.Add(step);
            }
            else if (operation == 7)
            {
                if (expected.Count > 0) expected.Dequeue();
                actual.RemoveFirst();
            }
            else if (operation == 8)
            {
                int keep = random.Next(capacity + 1);
                while (expected.Count > keep) expected.Dequeue();
                actual.TrimToCount(keep);
            }
            else { expected.Clear(); actual.Clear(); }
            Require(actual.Count == expected.Count, "Ring count differs from queue.");
            int index = 0;
            foreach (int value in expected)
                Require(actual[index++] == value, "Ring order differs after wrapping/removing/clearing.");
        }
    }

    private static void CheckRanks()
    {
        int[] scores = { 20, 20, 10, 5 };
        var random = new Random(2048);
        for (int pass = 0; pass < 100; pass++)
        {
            scores = scores.OrderBy(_ => random.Next()).ToArray();
            Require(CompetitionRanking.GetRank(20, scores, x => x) == 1, "Joint winners must both rank first.");
            Require(CompetitionRanking.GetRank(10, scores, x => x) == 3, "Third score must rank third after two joint winners.");
            Require(CompetitionRanking.GetRank(5, scores, x => x) == 4, "Input order must not alter rank.");
        }
        Require(CompetitionRanking.GetRank(0, Array.Empty<int>(), x => x) == 0, "Empty leaderboard has no rank.");
    }

    private static void CheckPredictions()
    {
        var cache = new PopupPredictionCache();
        Require(cache.TryClaim(1, 2, 3, 0f), "First hit should display.");
        Require(!cache.TryClaim(1, 2, 3, 29.999f), "Authoritative repeat should not display twice.");
        Require(cache.TryClaim(1, 4, 3, 29.999f), "Different victim must remain independent.");
        Require(cache.TryClaim(1, 2, 3, 30f), "Expired hit id should be reusable.");
        Require(cache.Count == 2, "Pruning must preserve unexpired victims.");
        cache.Clear();
        for (uint i = 0; i < 1000; i++) Require(cache.TryClaim(1, 2, i, i * .001f), "Unique hit missing.");
        Require(cache.TryClaim(1, 2, 5000, 100f) && cache.Count == 1, "Expired queue must drain fully.");
        Require(cache.TryClaim(1, 2, 5000, 0f), "Clock reset must not retain previous-session hits.");
        Require(!cache.TryClaim(1, 2, 6000, float.NaN), "Non-finite clock must be rejected.");
    }

    private static void CheckInput()
    {
        Require(InputModeRules.Resolve(true, false, true, true, true) == GameInputMode.TextInput, "Chat cancellation takes priority.");
        Require(InputModeRules.Resolve(false, false, false, false, true) == GameInputMode.Results, "Results stay unlocked.");
        Require(InputModeRules.Resolve(false, false, true, false, false) == GameInputMode.Spectating, "Dead players cannot resume gameplay through ESC.");
        Require(InputModeRules.Resolve(false, false, false, false, false) == GameInputMode.Gameplay, "Winner may move before results show.");
        Require(!InputModeRules.CanToggleMenu(GameInputMode.Results), "ESC cannot dismiss result input lock.");
        Require(!InputModeRules.CanToggleMenu(GameInputMode.Spectating), "ESC cannot dismiss spectator lock.");
        Require(InputModeRules.CanTrackCamera(GameInputMode.Spectating, true, false), "Blocking spectator input must not stop following the winner.");
        Require(!InputModeRules.CanTrackCamera(GameInputMode.Menu, true, false), "Paused menu preserves camera state.");
        Require(!InputModeRules.CanTrackCamera(GameInputMode.TextInput, false, true), "Typing must block camera input.");
        int cursorChecksBefore = _checks;
        foreach (GameInputMode mode in Enum.GetValues<GameInputMode>())
        {
            Require(InputModeRules.CanLockCursor("Battle", mode) == (mode == GameInputMode.Gameplay),
                "Only active battle gameplay may lock the cursor; menus/chat/death/results stay free.");
            Require(InputModeRules.CanLockCursor("Battle_waiting", mode) == (mode == GameInputMode.Gameplay),
                "Waiting-room practice uses the same FPS cursor policy as battle.");
            foreach (string scene in new[] { "Lobby", "Battle_wait", "Login", "Battle_preview", "", null })
                Require(!InputModeRules.CanLockCursor(scene, mode),
                    $"UI scene '{scene}' must keep its cursor free, including after clicking or resetting to {mode}.");
        }
        Console.WriteLine($"PASS: {_checks - cursorChecksBefore} cursor scene/mode policy assertions.");
        var gate = new FrameInputGate();
        for (int frame = 0; frame < 100; frame++)
        {
            Require(gate.TryConsumeEscape(frame), "New ESC frame was dropped.");
            Require(!gate.TryConsumeEscape(frame), "One ESC toggled two consumers.");
            Require(!gate.IsSubmitConsumed(frame), "Submit consumption leaked across frames.");
            gate.ConsumeSubmit(frame);
            Require(gate.IsSubmitConsumed(frame), "Chat submit must block result restart in same frame.");
        }
        gate.Clear();
        Require(gate.TryConsumeEscape(0) && !gate.IsSubmitConsumed(0), "New scene must reset consumption.");
    }

    private static void CheckRespawnCountdown()
    {
        var unstarted = default(PlayerRespawnCountdown);
        Require(!unstarted.HasStarted && !unstarted.IsReady(100d), "A default presentation must not enable respawn.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var invalidStart = new PlayerRespawnCountdown(invalid);
            Require(!invalidStart.HasStarted && !invalidStart.IsReady(100d), "Invalid start time must not enable respawn.");
            var countdown = new PlayerRespawnCountdown(10d);
            Require(!countdown.IsReady(invalid) && countdown.SecondsRemaining(invalid) == 5, "Invalid current time must not finish the countdown.");
        }
        for (int life = 0; life < 10; life++)
        {
            double start = 100d + life * 20d;
            var countdown = new PlayerRespawnCountdown(start);
            Require(countdown.ReadyAt == start + 5d, "Presentation and server must share the five-second delay.");
            Require(countdown.SecondsRemaining(start - 1d) == 5, "A backwards clock must not show more than five seconds.");
            for (int second = 0; second < 5; second++)
            {
                Require(countdown.SecondsRemaining(start + second) == 5 - second, "Countdown must show the full next second at the boundary.");
                Require(!countdown.IsReady(start + second + .999d), "Countdown must not unlock before the full delay.");
            }
            // A disabled view resumes from the same stored value; it must not construct a new deadline.
            var resumed = countdown;
            Require(resumed.SecondsRemaining(start + 4.5d) == 1, "A resumed view must preserve the remaining deadline.");
            Require(resumed.IsReady(start + 5d) && resumed.SecondsRemaining(start + 5d) == 0, "The five-second boundary must finish the countdown.");
            Require(resumed.IsReady(start + 9d) && resumed.SecondsRemaining(start + 9d) == 0, "Reopening a finished view must not restart the delay.");
            Require(HealthUiLifeRules.CanRequestRevive(true, start + 5d, start + 5d, 1f, true), "Server must accept a valid request at the shared delay boundary.");
            Require(!HealthUiLifeRules.CanRequestRevive(true, start + 4.999d, start + 5d, 1f, true), "A visible countdown is not permission to skip server time validation.");
            Require(!HealthUiLifeRules.CanRequestRevive(false, start + 6d, start + 5d, 1f, true), "Ready UI must not permit a duplicate revive.");
            Require(!HealthUiLifeRules.CanRequestRevive(true, start + 6d, start + 5d, 1f, false), "Ready UI must not permit a revive outside battle.");
        }
    }
}
