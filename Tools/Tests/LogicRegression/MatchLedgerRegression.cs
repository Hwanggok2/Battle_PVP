using System;
using System.Collections.Generic;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Networking;

internal static class MatchLedgerRegression
{
    private const string Room = "battle_a10_0123456789abcdef0123456789abcdef";
    private const string OtherRoom = "battle_b20_0123456789abcdef0123456789abcdef";
    private const string Challenge = "fedcba9876543210fedcba9876543210";

    internal static void Run(Action<bool, string> require)
    {
        CheckDepartedParticipantsAndRanks(require);
        CheckReconnects(require);
        CheckReconnectNameFallback(require);
        CheckConnectionBindingAndRosterSize(require);
        CheckInvalidEventsAndClamps(require);
        CheckDeathSequenceWrap(require);
        CheckFinalizationAndNewMatch(require);
        CheckRoomAuthentication(require);
    }

    private static void CheckDepartedParticipantsAndRanks(Action<bool, string> require)
    {
        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "A10", " Alice ");
        Attach(require, ledger, 2, "b20", "Bob");
        Attach(require, ledger, 3, "c30", "Cara");
        require(ledger.RecordKill(1, 2, 1, true), "Alice's first confirmed kill must count.");
        require(ledger.RecordKill(1, 2, 2, true), "Alice's second distinct death must count.");
        require(ledger.RecordKill(3, 2, 3, true), "Cara's first kill must count.");
        require(ledger.RecordKill(3, 2, 4, true), "Cara's second kill must count.");
        require(ledger.RecordDamage(1, 80f, 10f) && ledger.RecordDamage(2, 0f, 140f),
            "Accepted damage totals must be recorded independently.");
        require(ledger.Rename(3, "Cara final"), "Connected participants may update their display name.");
        require(ledger.Detach(3), "Departing player must detach from the active connection map.");
        require(!ledger.TryGetTotals(3, out _), "Disconnected net ids must no longer be event sources.");

        MatchResultSnapshot result = ledger.Finish();
        require(result.Participants.Count == 3, "Departed participants must remain in the final roster.");
        MatchParticipantResult alice = Find(result, "a10");
        MatchParticipantResult bob = Find(result, "b20");
        MatchParticipantResult cara = Find(result, "c30");
        require(alice.Rank == 1 && cara.Rank == 1 && bob.Rank == 3, "Joint first place must produce ranks 1, 1, 3.");
        require(!cara.WasConnectedAtEnd && cara.PlayerName == "Cara final" && cara.LastNetId == 3,
            "Departure must preserve identity, last display name, and final connection status.");
        require(alice.Totals.Points == 2 && cara.Totals.Points == 2 && bob.Totals.Deaths == 4,
            "Disconnected results must retain kill/death totals.");
        require(alice.Totals.DamageDealt == 80f && alice.Totals.DamageTaken == 10f && bob.Totals.DamageTaken == 140f,
            "Damage totals must survive result creation.");
        require(alice.ProvisionalXp == 120 && cara.ProvisionalXp == 120 && bob.ProvisionalXp == 20,
            "Equal ranks must retain equal provisional XP, including a departed winner.");
        result.GetTopOpponent(cara.KillsByOpponent, out string victimName, out int killedCount);
        require(victimName == "Bob" && killedCount == 2, "Top-opponent names/counts must survive departure.");
        result.GetTopOpponent(bob.DeathsByOpponent, out string killerName, out int deathCount);
        require(killerName == "Alice" && deathCount == 2, "Equal opponent counts must resolve deterministically by account id.");
        require(bob.DeathsByOpponent["a10"] == 2 && bob.DeathsByOpponent["c30"] == 2,
            "The complete opponent map must preserve tied counts, not only a display winner.");
        result.GetTopOpponent(alice.DeathsByOpponent, out string noneName, out int noneCount);
        require(noneName == "None" && noneCount == 0, "An empty opponent history must remain explicit.");
        require(result.LocalMatchId == "local-match-1" && result.RoomId == Room,
            "Snapshot metadata must preserve the host-provided local match id and room.");
    }

    private static void CheckReconnects(Action<bool, string> require)
    {
        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "a10", "Alice");
        Attach(require, ledger, 2, "B20", "Bob");
        require(ledger.RecordDamage(2, 12f, 34f), "Initial damage should be recorded.");
        require(ledger.RecordKill(1, 2, 1, true), "Initial victim generation should accept its first death.");
        require(ledger.Detach(2), "Victim must detach before reconnecting.");
        require(ledger.TryAttach(20, "b20", "Bob reconnected", out MatchTotals restored), "Verified same-account reconnect must attach.");
        require(restored.Points == 0 && restored.Deaths == 1 && restored.DamageDealt == 12f && restored.DamageTaken == 34f,
            "Reconnect must restore all prior totals.");
        require(ledger.ParticipantCount == 2, "Reconnect must not create a second participant row.");
        require(!ledger.RecordKill(1, 2, 2, true), "Old net id must not remain a valid victim after reconnect.");
        require(ledger.RecordKill(1, 20, 1, true), "A new network object generation may start its death sequence at one.");
        require(!ledger.RecordKill(1, 20, 1, true), "Repeated death in the new generation must not count twice.");
        require(ledger.TryAttach(20, "B20", "ignored repeated bind", out _), "An identical live binding is idempotent.");
        require(!ledger.RecordKill(1, 20, 1, true), "Idempotent attach must not reset the live death sequence.");
        require(ledger.RecordKill(20, 1, 1, true), "Reconnected participant must also act as killer.");
        require(ledger.RecordDamage(20, 5f, 6f), "Reconnect must continue existing damage totals.");

        MatchResultSnapshot result = ledger.Finish();
        MatchParticipantResult bob = Find(result, "b20");
        MatchParticipantResult alice = Find(result, "a10");
        require(bob.LastNetId == 20 && bob.WasConnectedAtEnd && bob.PlayerName == "Bob reconnected",
            "Final record must use the current connection and preserve idempotent binding name.");
        require(bob.Totals.Points == 1 && bob.Totals.Deaths == 2 && bob.Totals.DamageDealt == 17f && bob.Totals.DamageTaken == 40f,
            "Reconnect totals must accumulate across both network generations.");
        require(alice.KillsByOpponent.Count == 1 && alice.KillsByOpponent["b20"] == 2 && bob.DeathsByOpponent["a10"] == 2,
            "Opponent history must merge by verified account, not split by net id.");
        result.GetTopOpponent(alice.KillsByOpponent, out string name, out int count);
        require(name == "Bob reconnected" && count == 2, "Opponent display must resolve against the retained participant ledger.");
    }

    private static void CheckConnectionBindingAndRosterSize(Action<bool, string> require)
    {
        var unstarted = new MatchLedger();
        require(!unstarted.TryAttach(1, "a10", "Alice", out _) && unstarted.Finish() == null,
            "A ledger must not record or produce results before Begin.");
        RequireThrows<ArgumentException>(require, () => unstarted.Begin("", Room), "Empty local match id must be rejected.");
        RequireThrows<ArgumentException>(require, () => unstarted.Begin("local", "RoomRegistry"), "Reserved room id must be rejected.");

        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "a10", "Alice");
        require(!ledger.TryAttach(2, "A10", "Alice duplicate", out _), "A verified account cannot own two live connections.");
        require(!ledger.TryAttach(1, "b20", "Impostor", out _), "A live net id cannot change its bound account.");
        require(!ledger.TryAttach(0, "b20", "Bob", out _), "Zero net id cannot be a participant.");
        foreach (string invalid in new[] { null, "", "not-hex", "a10 ", " a10", "a10\n" })
            require(!ledger.TryAttach(2, invalid, "Bad", out _), "Malformed verified-account input must be rejected.");
        require(ledger.ParticipantCount == 1, "Rejected bindings must not grow the roster.");
        require(!ledger.Detach(999) && !ledger.Rename(999, "nobody"), "Unknown connections cannot alter the ledger.");

        for (uint id = 2; id <= 8; id++) Attach(require, ledger, id, id.ToString("x"), "Player " + id);
        require(ledger.ParticipantCount == 8, "Initial concurrent roster should contain eight players.");
        // Capacity is enforced by the network layer. Replacing one of eight active connections
        // must not discard prior participants or cap the cumulative match ledger at eight rows.
        require(ledger.Detach(1), "One active slot is released.");
        Attach(require, ledger, 9, "f09", "Replacement 9");
        require(ledger.Detach(2), "A second active slot is released.");
        Attach(require, ledger, 10, "f0a", "Replacement 10");
        MatchResultSnapshot result = ledger.Finish();
        require(result.Participants.Count == 10 && result.Participants.Count(x => x.WasConnectedAtEnd) == 8,
            "Ten cumulative participants with eight active connections must all survive the result.");
        require(Find(result, "a10").WasConnectedAtEnd == false && Find(result, "2").WasConnectedAtEnd == false,
            "Departed rows must remain distinct from replacement connections.");
        require(MatchLedger.NormalizeName("  Bob  ") == "Bob" && MatchLedger.NormalizeName(" ") == "Unknown",
            "Display name normalization must preserve the documented fallback.");
        require(MatchLedger.NormalizeName(new string('x', 100)).Length == 64, "Display names must be bounded.");
    }

    private static void CheckReconnectNameFallback(Action<bool, string> require)
    {
        foreach (string pendingProfileName in new[] { "Unknown", null })
        {
            MatchLedger ledger = Begin();
            Attach(require, ledger, 1, "a10", "Previously verified name");
            require(ledger.RecordDamage(1, 12f, 34f), "Reconnect name fixture must retain existing totals.");
            require(ledger.Detach(1) && ledger.TryAttach(10, "A10", pendingProfileName, out MatchTotals totals) &&
                totals.DamageDealt == 12f && totals.DamageTaken == 34f,
                "Reconnect with pending profile name must restore the same account totals.");
            MatchParticipantResult participant = Find(ledger.Finish(), "a10");
            require(participant.PlayerName == "Previously verified name" && participant.LastNetId == 10 && participant.WasConnectedAtEnd,
                "An Unknown or missing initial reconnect name must not overwrite the retained display name.");
        }
    }

    private static void CheckInvalidEventsAndClamps(Action<bool, string> require)
    {
        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "a10", "Alice");
        Attach(require, ledger, 2, "b20", "Bob");
        require(!ledger.RecordKill(1, 1, 1, true), "Self-kills must not count.");
        require(!ledger.RecordKill(1, 2, 0, true), "An uninitialized death sequence must not count.");
        require(!ledger.RecordKill(1, 2, 1, false), "A living victim must not produce a kill.");
        require(!ledger.RecordKill(999, 2, 1, true) && !ledger.RecordKill(1, 999, 1, true),
            "Unregistered killers and victims must be rejected.");
        require(!ledger.RecordDamage(999, 1f, 1f), "Unregistered connections cannot submit damage.");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            require(!ledger.RecordDamage(1, invalid, 1f), "Invalid outgoing damage must reject the entire event.");
            require(!ledger.RecordDamage(1, 1f, invalid), "Invalid incoming damage must reject the entire event.");
        }
        require(ledger.TryGetTotals(1, out MatchTotals untouched) && untouched.Points == 0 && untouched.Deaths == 0 &&
            untouched.DamageDealt == 0f && untouched.DamageTaken == 0f, "Rejected events must leave all totals untouched.");
        require(ledger.RecordDamage(1, 0f, 0f), "A finite zero delta is a harmless accepted no-op.");
        require(ledger.RecordKill(1, 2, 1, true), "First valid death must count.");
        require(!ledger.RecordKill(1, 2, 1, true), "Duplicate death must be rejected.");
        require(ledger.RecordKill(1, 2, 2, true), "Next victim death must count.");
        require(!ledger.RecordKill(1, 2, 1, true), "Delayed replay of an earlier death in the same generation must be rejected.");
        require(ledger.TryGetTotals(1, out MatchTotals afterKills) && afterKills.Points == 2,
            "Rejected death replays must not inflate the score.");

        require(ledger.RecordDamage(1, float.MaxValue, float.MaxValue), "Largest finite damage should be accepted.");
        require(ledger.RecordDamage(1, float.MaxValue, float.MaxValue), "Finite cumulative overflow should clamp, not become infinity.");
        require(ledger.TryGetTotals(1, out MatchTotals clamped) && float.IsFinite(clamped.DamageDealt) &&
            clamped.DamageDealt == float.MaxValue && clamped.DamageTaken == float.MaxValue,
            "Aggregated damage must remain finite at its representable maximum.");
        var xp = new SimpleXpDistributor();
        require(xp.CalculateXp(1, int.MaxValue) == int.MaxValue && xp.CalculateXp(2, int.MaxValue) == int.MaxValue &&
            xp.CalculateXp(3, int.MaxValue) == int.MaxValue,
            "Provisional XP overflow must clamp rather than wrap.");
        require(xp.CalculateXp(1, -1) == 100 && xp.CalculateXp(3, -1) == 20,
            "Negative point input must not produce a negative provisional reward.");
    }

    private static void CheckDeathSequenceWrap(Action<bool, string> require)
    {
        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "a10", "Alice");
        Attach(require, ledger, 2, "b20", "Bob");
        require(ledger.RecordKill(1, 2, uint.MaxValue - 1, true), "The first nonzero sequence establishes its generation baseline.");
        require(ledger.RecordKill(1, 2, uint.MaxValue, true), "A sequence may reach the uint boundary.");
        require(!ledger.RecordKill(1, 2, 0, true), "Wrapping does not make zero a valid death sequence.");
        require(ledger.RecordKill(1, 2, 1, true), "A wrapped sequence should advance within the same generation.");
        require(!ledger.RecordKill(1, 2, uint.MaxValue, true), "A delayed pre-wrap sequence must remain stale.");
        require(!ledger.RecordKill(1, 2, 1, true), "A duplicate wrapped sequence must not count twice.");
        require(!ledger.RecordKill(1, 2, 0x80000001u, true), "An ambiguous half-range sequence jump must be rejected.");
        require(ledger.TryGetTotals(1, out MatchTotals totals) && totals.Points == 3,
            "Only the three accepted sequence transitions may affect points.");
    }

    private static void CheckFinalizationAndNewMatch(Action<bool, string> require)
    {
        MatchLedger ledger = Begin();
        Attach(require, ledger, 1, "a10", "Alice");
        Attach(require, ledger, 2, "b20", "Bob");
        require(ledger.RecordKill(1, 2, 1, true) && ledger.RecordDamage(1, 10f, 5f), "Finalization fixture must record valid events.");
        RequireThrows<InvalidOperationException>(require, () => ledger.Begin("accidental-restart", OtherRoom),
            "Begin during an active match must reject accidental ledger replacement.");
        require(ledger.IsRecording && ledger.ParticipantCount == 2 && ledger.TryGetTotals(1, out MatchTotals retained) &&
            retained.Points == 1 && retained.DamageDealt == 10f && retained.DamageTaken == 5f,
            "A rejected Begin must preserve the current participant bindings and recorded totals.");
        MatchResultSnapshot oldResult = ledger.Finish();
        require(!ledger.IsRecording && ReferenceEquals(oldResult, ledger.Finish()),
            "Finish must freeze once and return the identical snapshot on retry.");
        require(!ledger.RecordDamage(1, 10f, 5f) && !ledger.RecordKill(1, 2, 2, true),
            "Events after finalization must be rejected.");
        require(!ledger.Rename(1, "changed") && !ledger.Detach(1) && !ledger.TryAttach(3, "c30", "Cara", out _),
            "Participant mutations after finalization must be rejected.");
        MatchParticipantResult oldAlice = Find(oldResult, "a10");
        RequireThrows<NotSupportedException>(require,
            () => ((IDictionary<string, int>)oldAlice.KillsByOpponent).Add("c30", 99),
            "Published opponent dictionaries must reject mutation.");
        RequireThrows<NotSupportedException>(require,
            () => ((IDictionary<string, int>)Find(oldResult, "b20").DeathsByOpponent).Clear(),
            "Published death histories must also reject mutation.");
        RequireThrows<NotSupportedException>(require,
            () => ((IList<MatchParticipantResult>)oldResult.Participants)[0] = null,
            "Published participant collection must reject mutation.");

        ledger.Begin("local-match-2", OtherRoom);
        require(ledger.IsRecording && ledger.ParticipantCount == 0 && !ledger.TryGetTotals(1, out _),
            "A new Begin must clear connections and cumulative participants.");
        Attach(require, ledger, 10, "a10", "Alice new match");
        Attach(require, ledger, 20, "b20", "Bob new match");
        require(ledger.TryGetTotals(10, out MatchTotals reset) && reset.Points == 0 && reset.Deaths == 0 &&
            reset.DamageDealt == 0f && reset.DamageTaken == 0f, "A new match must not inherit previous totals.");
        require(ledger.RecordKill(20, 10, 1, true), "A new match accepts a fresh death generation.");
        MatchResultSnapshot newResult = ledger.Finish();
        require(newResult.LocalMatchId == "local-match-2" && newResult.RoomId == OtherRoom &&
            !ReferenceEquals(oldResult, newResult), "New match must produce a distinct local result.");
        require(oldResult.LocalMatchId == "local-match-1" && oldAlice.PlayerName == "Alice" &&
            oldAlice.Totals.Points == 1 && oldAlice.Totals.DamageDealt == 10f &&
            oldAlice.KillsByOpponent["b20"] == 1 && oldAlice.DeathsByOpponent.Count == 0,
            "Beginning and recording another match must not mutate the previous published snapshot.");

        ledger.Begin("empty-match", Room);
        require(ledger.Finish().Participants.Count == 0, "An empty match snapshot must not invent a participant.");
    }

    private static void CheckRoomAuthentication(Action<bool, string> require)
    {
        string maximumAccount = new string('A', 64);
        string oversizedAccount = new string('a', 65);
        require(RoomAuthenticationRules.TryNormalizeAccountId(maximumAccount, out string normalizedMaximum) &&
            normalizedMaximum == new string('a', 64), "Authentication must accept and normalize exactly 64 hex characters.");
        require(!RoomAuthenticationRules.TryNormalizeAccountId(oversizedAccount, out string rejectedAccount) && rejectedAccount == null,
            "Authentication must reject 65-character accounts without returning a usable identity.");
        require(RoomIdentity.TryNormalizePlayerId(oversizedAccount, out string formatOnlyId) && formatOnlyId == oversizedAccount,
            "RoomIdentity format normalization intentionally remains wider than authentication limits.");
        RequireThrows<ArgumentException>(require, () => new AuthenticatedRoomPlayer(oversizedAccount, Room),
            "The authenticated identity object must also enforce the account length limit.");
        var lengthReservations = new RoomAuthenticationReservations<object>();
        require(lengthReservations.TryReserve(maximumAccount, new object()) &&
            !lengthReservations.TryReserve(oversizedAccount, new object()) && lengthReservations.Count == 1,
            "Reservation ownership must use the bounded authentication contract.");
        var identity = new AuthenticatedRoomPlayer("B20", Room);
        require(identity.PlayFabId == "b20" && identity.RoomId == Room,
            "Authenticated participant account must normalize; participants need not own the room.");
        RequireThrows<ArgumentException>(require, () => new AuthenticatedRoomPlayer("bad-id", Room),
            "Malformed authenticated account must be rejected.");
        RequireThrows<ArgumentException>(require, () => new AuthenticatedRoomPlayer("a10", "RoomRegistry"),
            "Malformed authenticated room must be rejected.");
        require(RoomAuthenticationRules.IsChallenge(Challenge), "A lowercase nonzero N-format challenge is valid.");
        foreach (string invalid in new[] { null, "", new string('0', 32), Challenge.ToUpperInvariant(),
            Guid.Parse(Challenge).ToString(), Challenge.Substring(1), Challenge + "0", "g" + Challenge.Substring(1), " " + Challenge })
            require(!RoomAuthenticationRules.IsChallenge(invalid), "Noncanonical or empty challenges must be rejected.");
        require(RoomAuthenticationRules.IsBeforeDeadline(29.999d, 30d), "Verification before the deadline is accepted.");
        require(!RoomAuthenticationRules.IsBeforeDeadline(30d, 30d) && !RoomAuthenticationRules.IsBeforeDeadline(30.001d, 30d),
            "The exact authentication deadline and late replies are expired.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            require(!RoomAuthenticationRules.IsBeforeDeadline(invalid, 30d), "Invalid current time cannot authorize authentication.");
            require(!RoomAuthenticationRules.IsBeforeDeadline(10d, invalid), "Invalid deadline cannot authorize authentication.");
        }
        require(RoomAuthenticationRules.IsLocalOwner(Room, "A10"), "Room owner comparison must normalize account case.");
        require(!RoomAuthenticationRules.IsLocalOwner(Room, "b20") && !RoomAuthenticationRules.IsLocalOwner("RoomRegistry", "a10"),
            "Other accounts and malformed rooms cannot become the local owner.");
        require(RoomAuthenticationRules.MatchesProof(Room, Challenge, "B20", Room, Challenge, "b20"),
            "Verified room/challenge/account proof should match normalized account input.");
        require(!RoomAuthenticationRules.MatchesProof(Room, Challenge, "b20", OtherRoom, Challenge, "b20"),
            "Proof for another room must be rejected.");
        require(!RoomAuthenticationRules.MatchesProof(Room, Challenge, "b20", Room, new string('1', 32), "b20"),
            "Proof for another challenge must be rejected.");
        require(!RoomAuthenticationRules.MatchesProof(Room, Challenge, "b20", Room, Challenge, "c30"),
            "Proof for another account must be rejected.");
        require(!RoomAuthenticationRules.MatchesProof(Room, new string('0', 32), "b20", Room, new string('0', 32), "b20"),
            "Matching but invalid challenge text must not authorize a proof.");

        var reservations = new RoomAuthenticationReservations<object>();
        var first = new object();
        var replacement = new object();
        require(!reservations.TryReserve("bad-id", first) && !reservations.TryReserve("a10", null) && reservations.Count == 0,
            "Invalid reservation requests must not modify live account bindings.");
        require(reservations.TryReserve("A10", first) && reservations.Count == 1, "A verified account may reserve one connection.");
        require(reservations.TryReserve("a10", first) && reservations.Count == 1, "Repeating the same reservation must be idempotent.");
        require(!reservations.TryReserve("a10", replacement), "A second connection for a reserved account must be rejected.");
        reservations.Release("a10", replacement);
        require(reservations.Count == 1, "A different connection cannot release someone else's reservation.");
        reservations.Release("A10", first);
        require(reservations.Count == 0 && reservations.TryReserve("a10", replacement), "Disconnect must allow a verified replacement.");
        reservations.Release("a10", first);
        require(reservations.Count == 1, "A stale disconnect must not remove a newer reservation.");
        reservations.Clear();
        require(reservations.Count == 0 && reservations.TryReserve("a10", first), "Room shutdown must reset all reservations.");
    }

    private static MatchLedger Begin()
    {
        var ledger = new MatchLedger();
        ledger.Begin("local-match-1", Room);
        return ledger;
    }

    private static void Attach(Action<bool, string> require, MatchLedger ledger, uint netId, string account, string name)
        => require(ledger.TryAttach(netId, account, name, out _), "Expected verified participant attach: " + account);

    private static MatchParticipantResult Find(MatchResultSnapshot result, string account)
        => result.Participants.Single(participant => participant.PlayFabId == account);

    private static void RequireThrows<T>(Action<bool, string> require, Action action, string message) where T : Exception
    {
        bool thrown = false;
        try { action(); }
        catch (T) { thrown = true; }
        require(thrown, message);
    }
}
