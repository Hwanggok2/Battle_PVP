using System;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class RoomSessionStateTests
    {
        private const string Room = "battle_a10_0123456789abcdef0123456789abcdef";
        private const string OtherRoom = "battle_b20_fedcba9876543210fedcba9876543210";

        [Test]
        public void CleanupAndRelayQueuesReleaseOnlyWhenTheirOwnOperationCompletes()
        {
            var flows = new RoomFlowGeneration();
            var state = new RoomSessionState(flows.Begin("a10", Room));
            Assert.That(state.TryQueueCleanup(), Is.False);
            Assert.That(state.TryQueueRelay(), Is.True);
            Assert.That(state.TryQueueRelay(), Is.False);
            state.MarkMembershipPossible();
            Assert.That(state.TryQueueCleanup(), Is.True);
            Assert.That(state.TryQueueCleanup(), Is.False);
            state.ReleaseCleanupQueue();
            Assert.That(state.TryQueueCleanup(), Is.True);
            state.ConfirmCleanup();
            Assert.That(state.TryQueueCleanup(), Is.False);
            Assert.That(state.RelayQueued, Is.True);
            state.CompleteRelayPreparation();
            Assert.That(state.TryQueueRelay(), Is.True);
        }

        [Test]
        public void SameRoomRenewalInheritsCleanupEvenIfItCancelsBeforeSendingJoin()
        {
            var flows = new RoomFlowGeneration();
            var old = new RoomSessionState(flows.Begin("a10", Room));
            old.MarkMembershipPossible();
            old.TryQueueCleanup();
            Assert.That(old.AuthorizeCleanup(flows, "a10", old), Is.False);
            Assert.That(old.MembershipPossible, Is.True);
            var current = new RoomSessionState(flows.Begin("a10", Room));
            Assert.That(old.AuthorizeCleanup(flows, "A10", current), Is.False);
            Assert.That(old.MembershipPossible, Is.True);
            Assert.That(current.MembershipPossible, Is.True);
            old.ReleaseCleanupQueue();
            Assert.That(old.TryQueueCleanup(), Is.True);
            Assert.That(old.AuthorizeCleanup(flows, "a10", current), Is.False, "A late callback must recheck the current same-room flow.");
            flows.Invalidate();
            Assert.That(current.TryQueueCleanup(), Is.True);
            Assert.That(current.AuthorizeCleanup(flows, "a10", null), Is.True);
            current.ConfirmCleanup();
            Assert.That(current.TryQueueCleanup(), Is.False);
        }

        [Test]
        public void DuplicateRejectionDoesNotTransferExistingConnectionCleanupToAFutureAttempt()
        {
            var flows = new RoomFlowGeneration();
            var rejected = new RoomSessionState(flows.Begin("a10", Room));
            rejected.MarkMembershipPossible();
            flows.PreserveMembership(rejected.Ticket);
            var future = new RoomSessionState(flows.Begin("a10", Room));
            Assert.That(rejected.AuthorizeCleanup(flows, "a10", future), Is.False);
            Assert.That(future.MembershipPossible, Is.False);
            future.MarkMembershipPossible();
            flows.Invalidate();
            Assert.That(rejected.AuthorizeCleanup(flows, "a10", null), Is.False);
            Assert.That(future.AuthorizeCleanup(flows, "a10", null), Is.True);
        }

        [TestCase("a10", OtherRoom)]
        [TestCase("b20", Room)]
        public void DifferentMembershipNeverReceivesOldCleanupResponsibility(string account, string room)
        {
            var flows = new RoomFlowGeneration();
            var old = new RoomSessionState(flows.Begin("a10", Room));
            old.MarkMembershipPossible();
            var current = new RoomSessionState(flows.Begin(account, room));
            Assert.That(old.AuthorizeCleanup(flows, account, current), Is.True);
            Assert.That(current.MembershipPossible, Is.False);
        }

        [TestCase(9d, false)]
        [TestCase(10d, true)]
        [TestCase(13d, true)]
        [TestCase(13.001d, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        public void SnapshotReuseHonorsTtlAndFiniteMonotonicTime(double now, bool expected)
        {
            var state = new RoomListSnapshotState();
            Assert.That(state.CanReuse(now), Is.False);
            state.TryComplete(state.Revision, 10d);
            Assert.That(state.CanReuse(now), Is.EqualTo(expected));
        }

        [Test]
        public void MutationsRejectOlderListResponsesWithoutExtendingNewerSnapshotLifetime()
        {
            var state = new RoomListSnapshotState();
            ulong old = state.Revision;
            state.TryComplete(old, 1d);
            state.Invalidate();
            Assert.That(state.TryComplete(old, 2d), Is.False);
            Assert.That(state.CanReuse(2d), Is.False);
            Assert.That(state.TryComplete(state.Revision, 3d), Is.True);
            Assert.That(state.TryComplete(old, 4d), Is.False);
            Assert.That(state.CanReuse(6d), Is.True);
            Assert.That(state.CanReuse(6.001d), Is.False);
        }

        [Test]
        public void FailedListingMayReplaceUnverifiedRoomsWithEmptyDespiteRevisionChanges()
        {
            var state = new RoomListSnapshotState();
            ulong requestRevision = state.Revision;
            state.Invalidate();
            Assert.That(state.TryComplete(requestRevision, 10d), Is.False);
            Assert.That(state.TryComplete(requestRevision, 10d, discardUnverified: true), Is.True);
            Assert.That(state.CanReuse(13d), Is.True);
            Assert.That(state.CanReuse(13.001d), Is.False);
        }

        [Test]
        public async Task LateAcknowledgementCanResumeCleanupAfterInheritedAttemptWasCancelled()
        {
            var flows = new RoomFlowGeneration();
            var old = new RoomSessionState(flows.Begin("a10", Room));
            old.MarkMembershipPossible();
            var response = new TaskCompletionSource<int>();
            var lateCleanup = new TaskCompletionSource<bool>();
            var gate = new RoomServiceResponseGate();
            using var deadline = new CancellationTokenSource();
            Task<int> waiting = gate.ExecuteAsync(old.Ticket.MembershipKey, () => response.Task, deadline.Token, _ =>
            {
                old.ReleaseCleanupQueue();
                lateCleanup.TrySetResult(old.TryQueueCleanup() && old.AuthorizeCleanup(flows, "a10", null));
            });
            deadline.Cancel();
            try { await waiting; Assert.Fail("The initial write must remain unconfirmed."); }
            catch (RoomServiceUnconfirmedException) { }
            var renewed = new RoomSessionState(flows.Begin("a10", Room));
            old.TryQueueCleanup();
            Assert.That(old.AuthorizeCleanup(flows, "a10", renewed), Is.False);
            Assert.That(renewed.MembershipPossible, Is.True);
            bool sent = false;
            try { await gate.ExecuteAsync(renewed.Ticket.MembershipKey, () => { sent = true; return Task.FromResult(2); }, CancellationToken.None); }
            catch (RoomServiceUnconfirmedException) { }
            Assert.That(sent, Is.False);
            flows.Invalidate();
            response.SetResult(1);
            Assert.That(await Task.WhenAny(lateCleanup.Task, Task.Delay(2000)), Is.SameAs(lateCleanup.Task),
                "The late acknowledgement must complete its cleanup decision without hanging the runner.");
            Assert.That(await lateCleanup.Task, Is.True);
            Assert.That(gate.IsUnconfirmed(old.Ticket.MembershipKey), Is.False);
        }
    }
}
