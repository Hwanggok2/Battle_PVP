using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Networking;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class BattleWithdrawalTests
    {
        private static MatchLedger Create(int count)
        {
            RoomIdentity.TryCreate("a", System.Guid.NewGuid(), out string room);
            var ledger = new MatchLedger(); ledger.Begin("withdrawal", room);
            for (uint i = 1; i <= count; i++) Assert.That(ledger.TryAttach(i, i.ToString("x"), "Player " + i, out _), Is.True);
            return ledger;
        }

        [Test] public void LastParticipantWinsEvenWhenDepartedLeaderHasMoreKills()
        {
            var ledger = Create(3);
            for (uint i = 1; i <= 4; i++) ledger.RecordKill(1, 3, i, true);
            ledger.Withdraw(2);
            Assert.That(ledger.TryGetLastParticipant(out _), Is.False);
            ledger.Withdraw(1);
            Assert.That(ledger.TryGetLastParticipant(out uint winner), Is.True);
            Assert.That(winner, Is.EqualTo(3));
            var result = ledger.Finish(winner);
            Assert.That(result.Participants.Single(p => p.Rank == 1).LastNetId, Is.EqualTo(3));
            Assert.That(result.Participants.Single(p => p.LastNetId == 1).Totals.Points, Is.EqualTo(4));
            Assert.That(result.Participants.Single(p => p.LastNetId == 1).WasConnectedAtEnd, Is.False);
            Assert.That(ledger.Finish(), Is.SameAs(result), "The timer cannot overwrite an early result.");
        }

        [Test] public void DeathDoesNotWithdrawAndDisconnectedRetainedBodyDoesNotCount()
        {
            var ledger = Create(2);
            ledger.RecordKill(1, 2, 1, true);
            Assert.That(ledger.TryGetLastParticipant(out _), Is.False, "A dead player still has a respawn.");
            ledger.SetConnectionState(1, false);
            Assert.That(ledger.TryGetLastParticipant(out uint winner), Is.True);
            Assert.That(winner, Is.EqualTo(2));
            ledger.SetConnectionState(1, true);
            Assert.That(ledger.TryGetLastParticipant(out _), Is.False);
            ledger.Detach(1);
            Assert.That(ledger.TryGetLastParticipant(out winner), Is.True);
            Assert.That(winner, Is.EqualTo(2));
        }

        [Test] public void SoloAndEmptyRoomsCannotProduceAutomaticWinner()
        {
            var solo = Create(1);
            Assert.That(solo.TryGetLastParticipant(out _), Is.False);
            var empty = Create(2); empty.Withdraw(1); empty.Withdraw(2);
            Assert.That(empty.TryGetLastParticipant(out _), Is.False);
        }

        [Test] public void WithdrawalFreezesScoresAndCannotReenterCurrentRound()
        {
            var ledger = Create(3);
            ledger.RecordKill(1, 2, 1, true);
            ledger.Withdraw(1);
            Assert.That(ledger.Withdraw(1), Is.False);
            Assert.That(ledger.RecordDamage(1, 100, 100), Is.False);
            Assert.That(ledger.RecordKill(1, 2, 2, true), Is.False);
            Assert.That(ledger.TryAttach(10, "1", "Returning", out _), Is.False);
            Assert.That(ledger.TryGetLastParticipant(out _), Is.False);
            var result = ledger.Finish();
            Assert.That(result.Participants.Single(p => p.LastNetId == 1).Rank, Is.EqualTo(3));
            Assert.That(result.Participants.Count(p => p.Rank == 1), Is.EqualTo(2), "Active tied scores retain shared first place.");
        }
    }
}
