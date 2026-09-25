using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class RoomServiceTimeoutTests
    {
        [Test]
        public async Task HeartbeatFailureAllowsRetryWithoutReplayingItsLateResponse()
        {
            var queue = new RoomOperationQueue();
            var lease = new HostRoomLease();
            lease.BeginRegistration(0d);
            Assert.That(lease.Accept(0d, 0d, 1000d, 61000d), Is.True);
            Assert.That(lease.TryBeginHeartbeat(15d), Is.True);
            var response = new TaskCompletionSource<int>();
            using (var deadline = new CancellationTokenSource())
            {
                Task<int> heartbeat = queue.Enqueue("host/room", () =>
                    RoomServiceResponseGate.WaitForLeaseRefreshAsync(() => response.Task, deadline.Token));
                deadline.Cancel();
                try { await heartbeat; Assert.Fail("The heartbeat wait must end."); }
                catch (OperationCanceledException) { }
                lease.Failed(30d);
                Assert.That(queue.PendingKeys, Is.Zero);
                Assert.That(lease.TryBeginHeartbeat(34d), Is.False);
                Assert.That(lease.TryBeginHeartbeat(35d), Is.True);
                Assert.That(await queue.Enqueue("host/room", () =>
                    RoomServiceResponseGate.WaitForLeaseRefreshAsync(() => Task.FromResult(2), CancellationToken.None)), Is.EqualTo(2));
                Assert.That(lease.Accept(35d, 35d, 100000d, 160000d), Is.True);
                response.SetResult(1);
                Assert.That(heartbeat.IsCanceled, Is.True);
                Assert.That(lease.HasExpired(75d), Is.False);
                Assert.That(lease.HasExpired(95d), Is.True);
            }
        }

        [Test]
        public async Task MissingResponseReleasesQueueButBlocksSameMembershipUntilLateAcknowledgement()
        {
            var queue = new RoomOperationQueue();
            var gate = new RoomServiceResponseGate();
            var response = new TaskCompletionSource<int>();
            var late = new TaskCompletionSource<int>();
            using (var timeout = new CancellationTokenSource())
            {
                Task<int> request = queue.Enqueue("a/room", () => gate.ExecuteAsync("a/room", () => response.Task,
                    timeout.Token, value => late.TrySetResult(value)));
                timeout.Cancel();
                try { await request; Assert.Fail("Timeout must end this wait."); }
                catch (RoomServiceUnconfirmedException) { }
                Assert.That(queue.PendingKeys, Is.Zero);
                bool sent = false;
                try
                {
                    await queue.Enqueue("a/room", () => gate.ExecuteAsync("a/room", () =>
                    { sent = true; return Task.FromResult(2); }, CancellationToken.None));
                    Assert.Fail("The unresolved operation must prevent another write.");
                }
                catch (RoomServiceUnconfirmedException) { }
                Assert.That(sent, Is.False);
                Assert.That(await queue.Enqueue("a/other", () => gate.ExecuteAsync("a/other",
                    () => Task.FromResult(3), CancellationToken.None)), Is.EqualTo(3));
                response.SetResult(1);
                Assert.That(await late.Task, Is.EqualTo(1));
                Assert.That(gate.UnconfirmedKeys, Is.Zero);
            }
        }

        [Test]
        public async Task TransportFailureDoesNotAuthorizeASecondMutation()
        {
            var gate = new RoomServiceResponseGate();
            try
            {
                await gate.ExecuteAsync("a/room", () => Task.FromException<int>(new InvalidOperationException()),
                    CancellationToken.None);
                Assert.Fail("No server response was received.");
            }
            catch (RoomServiceUnconfirmedException) { }
            Assert.That(gate.IsUnconfirmed("a/room"), Is.True);
        }

        [Test]
        public async Task LatePreparationDoesNotResumeTheCancelledCommitPath()
        {
            var result = new TaskCompletionSource<int>();
            using (var cancellation = new CancellationTokenSource())
            {
                Task<int> wait = ServiceTaskDeadline.WaitAsync(result.Task, cancellation.Token);
                cancellation.Cancel();
                try { await wait; Assert.Fail("The old caller must stop waiting."); }
                catch (OperationCanceledException) { }
                result.SetResult(10);
                Assert.That(wait.IsCanceled, Is.True);
                Assert.That(await ServiceTaskDeadline.WaitAsync(Task.FromResult(20), CancellationToken.None), Is.EqualTo(20));
            }
        }

        [Test]
        public void StaleTransportPreparationCannotCommitOrClearTheNextCancellationSource()
        {
            var go = new GameObject("Relay preparation lifecycle test");
            var relay = go.AddComponent<UnityRelayTransport>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(UnityRelayTransport);
            MethodInfo begin = type.GetMethod("BeginPreparation", flags);
            MethodInfo finish = type.GetMethod("FinishPreparation", flags);
            MethodInfo require = type.GetMethod("RequireCurrentPreparation", flags);
            FieldInfo version = type.GetField("_preparationVersion", flags);
            FieldInfo pending = type.GetField("_preparationCancellation", flags);
            CancellationTokenSource first = null;
            CancellationTokenSource second = null;
            try
            {
                first = (CancellationTokenSource)begin.Invoke(relay, new object[] { CancellationToken.None });
                long oldVersion = (long)version.GetValue(relay);
                CancellationToken oldToken = first.Token;
                second = (CancellationTokenSource)begin.Invoke(relay, new object[] { CancellationToken.None });
                Assert.That(oldToken.IsCancellationRequested, Is.True);
                TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
                    require.Invoke(relay, new object[] { oldVersion, oldToken }));
                Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
                finish.Invoke(relay, new object[] { first });
                first = null;
                Assert.That(pending.GetValue(relay), Is.SameAs(second));
                require.Invoke(relay, new object[] { version.GetValue(relay), second.Token });
                relay.CancelPendingPreparation();
                Assert.That(second.IsCancellationRequested, Is.True);
            }
            finally
            {
                if (first != null) finish.Invoke(relay, new object[] { first });
                if (second != null) finish.Invoke(relay, new object[] { second });
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
