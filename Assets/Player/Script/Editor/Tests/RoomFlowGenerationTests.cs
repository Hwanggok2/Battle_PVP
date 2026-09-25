using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattlePvp.Networking;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class RoomFlowGenerationTests
    {
        private const string Account = "ABC123";
        private static readonly string RoomA = "battle_abc123_" + new string('1', 32);
        private static readonly string RoomB = "battle_abc123_" + new string('2', 32);

        [Test]
        public void NewRoomCancelAndAccountChangeInvalidateOldResponses()
        {
            var generation = new RoomFlowGeneration();
            RoomFlowTicket a = generation.Begin(Account, RoomA);
            RoomFlowTicket b = generation.Begin(Account, RoomB);
            Assert.That(generation.IsCurrent(a, Account), Is.False);
            Assert.That(generation.ShouldCompensate(a, Account), Is.True);
            Assert.That(generation.IsCurrent(b, Account), Is.True);
            Assert.That(generation.IsCurrent(b, "DEF456"), Is.False);
            Assert.That(generation.ShouldCompensate(b, "DEF456"), Is.True);
            generation.Invalidate();
            Assert.That(generation.IsCurrent(b, Account), Is.False);
            Assert.That(generation.ShouldCompensate(b, Account), Is.True);
        }

        [Test]
        public void SameRoomNewAttemptOwnsMembershipAndPreservationDoesNotLeakToFutureAttempt()
        {
            var generation = new RoomFlowGeneration();
            RoomFlowTicket old = generation.Begin(Account, RoomA);
            generation.Begin(Account, RoomB);
            RoomFlowTicket rejected = generation.Begin(Account, RoomA);
            Assert.That(generation.ShouldCompensate(old, Account), Is.False);
            generation.PreserveMembership(rejected);
            generation.Invalidate();
            Assert.That(generation.IsMembershipPreserved(old), Is.True);
            Assert.That(generation.ShouldCompensate(rejected, Account), Is.False);
            RoomFlowTicket future = generation.Begin(Account, RoomA);
            generation.PreserveMembership(old); // Late older callbacks cannot lower the preservation cutoff.
            generation.Invalidate();
            Assert.That(generation.ShouldCompensate(old, Account), Is.False);
            Assert.That(generation.ShouldCompensate(rejected, Account), Is.False);
            Assert.That(generation.ShouldCompensate(future, Account), Is.True);
        }

        [Test]
        public async Task SameRoomLeaveCompletesBeforeNewJoinButOtherRoomsRemainIndependent()
        {
            var queue = new RoomOperationQueue();
            var response = new TaskCompletionSource<int>();
            var order = new List<string>();
            Task<int> leave = queue.Enqueue(RoomA, () => { order.Add("leave requested"); return response.Task; });
            Task<int> rejoin = queue.Enqueue(RoomA, () => { order.Add("new join"); return Task.FromResult(2); });
            Task<int> otherRoom = queue.Enqueue(RoomB, () => { order.Add("other room"); return Task.FromResult(3); });
            Assert.That(await otherRoom, Is.EqualTo(3));
            Assert.That(rejoin.IsCompleted, Is.False);
            CollectionAssert.AreEqual(new[] { "leave requested", "other room" }, order);
            response.SetResult(0);
            Assert.That(await leave, Is.Zero);
            Assert.That(await rejoin, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { "leave requested", "other room", "new join" }, order);
            Assert.That(queue.PendingKeys, Is.Zero);
        }

        [Test]
        public async Task FailedMutationReleasesItsQueueWithoutDroppingTheNextOperation()
        {
            var queue = new RoomOperationQueue();
            var response = new TaskCompletionSource<int>();
            Task<int> failed = queue.Enqueue(RoomA, () => response.Task);
            Task<int> following = queue.Enqueue(RoomA, () => Task.FromResult(7));
            response.SetException(new InvalidOperationException("Service failed"));
            try { await failed; Assert.Fail("The service failure must propagate."); }
            catch (InvalidOperationException) { }
            Assert.That(await following, Is.EqualTo(7));
            Assert.That(queue.PendingKeys, Is.Zero);
        }

        [Test]
        public async Task QueuedOldCleanupRechecksTheCurrentRoomAtDispatch()
        {
            var generation = new RoomFlowGeneration();
            var queue = new RoomOperationQueue();
            var response = new TaskCompletionSource<bool>();
            RoomFlowTicket old = generation.Begin(Account, RoomA);
            Task<bool> oldJoin = queue.Enqueue(old.MembershipKey, () => response.Task);
            generation.Begin(Account, RoomB);
            bool removed = false;
            Task<bool> cleanup = queue.Enqueue(old.MembershipKey, () =>
            {
                removed = generation.ShouldCompensate(old, Account);
                return Task.FromResult(removed);
            });
            generation.Begin(Account, RoomA);
            response.SetResult(true);
            await oldJoin;
            Assert.That(await cleanup, Is.False);
            Assert.That(removed, Is.False);
        }
    }
}
