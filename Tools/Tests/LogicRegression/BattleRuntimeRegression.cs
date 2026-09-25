using System;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;

internal static class BattleRuntimeRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var clock = new MatchClock();
        require(!clock.TryFinish(100d), "A clock that has not started cannot finish.");
        clock.Start(100d, 180d);
        require(clock.Remaining(100d) == 180d && clock.EndsAt == 280d, "The server establishes one absolute deadline.");
        require(clock.Remaining(151.75d) == 128.25d, "A hitch consumes its actual duration.");
        require(!clock.TryFinish(279.999d), "The deadline must not fire early.");
        require(clock.TryFinish(280d), "The exact deadline finishes the match.");
        require(!clock.TryFinish(281d), "A match can finish only once.");
        clock.Start(500d, 180d);
        require(clock.TryFinish(900d), "A suspension beyond the end must finish immediately on resume.");
        clock.Start(1000d, 0d);
        require(clock.TryFinish(1000d), "A zero duration match finishes without a frame count dependency.");
        clock.Start(1000d, 5d);
        clock.Stop();
        require(!clock.TryFinish(1010d) && clock.Remaining(1001d) == 0d, "Stopping invalidates the prior match deadline.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            bool threw = false;
            try { clock.Start(0d, invalid); } catch (ArgumentOutOfRangeException) { threw = true; }
            require(threw, "Invalid match durations must be rejected.");
        }
        clock.Start(20d, 5d);
        require(!clock.TryFinish(double.NaN) && !clock.TryFinish(double.PositiveInfinity), "Nonfinite ticks cannot end a match.");

        var ledger = new MatchLedger();
        ledger.Begin("retained", "battle_a10_0123456789abcdef0123456789abcdef");
        require(ledger.TryAttach(1, "a10", "Host", out _) && ledger.TryAttach(2, "b20", "Guest", out _), "Attach both verified participants.");
        require(ledger.RecordKill(1, 2, 1, true), "The initial death is recorded.");
        require(ledger.SetConnectionState(2, false), "Only connection state changes for a retained body.");
        require(ledger.RecordDamage(2, 4f, 12f), "A retained body can still take damage and deal poison damage.");
        require(!ledger.RecordKill(1, 2, 1, true), "Disconnect must not reset the existing death sequence.");
        require(!ledger.TryAttach(20, "b20", "Another body", out _), "A retained body prevents a second body for the same account.");
        require(ledger.TryAttach(2, "B20", "Guest", out MatchTotals totals), "The same verified account reattaches the same body.");
        require(totals.Deaths == 1 && totals.DamageTaken == 12f && totals.DamageDealt == 4f, "Reattach retains offline damage and death totals.");
        require(!ledger.TryAttach(2, "c30", "Wrong account", out _), "Another account cannot take over a retained body.");
        require(!ledger.RecordKill(1, 2, 1, true), "Reattach must not reset the death sequence either.");
        require(ledger.RecordKill(1, 2, 2, true), "A later real death still counts.");
        require(ledger.SetConnectionState(2, false), "Disconnect again before match end.");
        MatchResultSnapshot result = ledger.Finish();
        MatchParticipantResult guest = result.Participants[1];
        require(!guest.WasConnectedAtEnd && guest.Totals.Deaths == 2 && guest.LastNetId == 2, "Final results preserve offline status and the original body id.");
        require(!ledger.SetConnectionState(2, true) && !ledger.RecordDamage(2, 1f, 1f), "Final results cannot change during retained-body cleanup.");

        PlayerRespawnCountdown countdown = PlayerRespawnCountdown.FromServerDeadline(10d, 103d, 105d);
        require(countdown.SecondsRemaining(10d) == 2 && countdown.IsReady(12d), "A reconnect keeps the two seconds remaining on the server.");
        countdown = PlayerRespawnCountdown.FromServerDeadline(10d, 110d, 105d);
        require(countdown.IsReady(10d), "An already elapsed respawn deadline is immediately available.");
        countdown = PlayerRespawnCountdown.FromServerDeadline(10d, 100d, 1000d);
        require(countdown.SecondsRemaining(10d) == 5, "Respawn presentation is bounded by the authored delay.");
        require(!PlayerRespawnCountdown.FromServerDeadline(0d, double.NaN, 0d).HasStarted, "Invalid clock data must not start a countdown.");
        require(PlayerRespawnCountdown.FromServerDeadline(30d, 0d, 105d, 120d).IsReady(30d),
            "A reliable reconnect spawn before TimeSnapshot must use its server batch time instead of restarting the wait.");
        require(PlayerRespawnCountdown.FromServerDeadline(30d, 0d, 105d, 103d).SecondsRemaining(30d) == 2,
            "Initial timeline zero must preserve only the server's remaining two seconds.");
    }
}
