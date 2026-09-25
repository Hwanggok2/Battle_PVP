using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Combat;
using BattlePvp.Networking;

internal static class GameplayFollowUpRegression
{
    private const string Room = "battle_a10_0123456789abcdef0123456789abcdef";
    public static void Run(Action<bool, string> require)
    {
        CheckBacklog(require);
        CheckCombo(require);
        CheckReplacement(require).GetAwaiter().GetResult();
    }

    private static void CheckBacklog(Action<bool, string> require)
    {
        var queue = new ReliableSendBacklog();
        byte[] mirrorBatch = { 99, 1, 2, 3, 99 };
        require(queue.TryEnqueue(new ArraySegment<byte>(mirrorBatch, 1, 3), 5d), "The rejected Mirror segment is retained.");
        Array.Fill(mirrorBatch, (byte)0);
        require(queue.Peek().Count == 3 && queue.Peek().Array[0] == 1 && queue.Peek().Array[2] == 3,
            "A reused Mirror batch buffer must not corrupt retained payload or include bytes outside the segment.");
        require(!queue.HasExpired(14.999) && queue.HasExpired(15), "The oldest packet has a bounded ten-second wait.");
        queue.Clear();
        var expected = new Queue<byte>();
        for (int i = 0; i < 800; i++)
        {
            byte value = (byte)i;
            require(queue.TryEnqueue(new ArraySegment<byte>(new[] { value }), 0), "A bounded burst is retained.");
            expected.Enqueue(value);
            // Simulate intermittent safe BeginSend refusal. The same head stays queued.
            if (i % 3 != 0)
            {
                require(queue.Peek().Array[0] == expected.Dequeue(), "Recovery must preserve reliable order.");
                queue.RemoveFirst();
            }
        }
        while (expected.Count > 0)
        {
            require(queue.Peek().Array[0] == expected.Dequeue(), "Burst recovery cannot skip a message.");
            queue.RemoveFirst();
        }
        require(queue.Count == 0 && queue.Bytes == 0, "Draining releases all retained memory.");
        require(queue.TryEnqueue(new ArraySegment<byte>(new byte[ReliableSendBacklog.MaxBytes]), 0), "The exact byte bound is allowed.");
        require(!queue.TryEnqueue(new ArraySegment<byte>(new byte[1]), 0), "Overflow must be reported to the transport, never silently evicted.");
        queue.Clear();
        for (int i = 0; i < ReliableSendBacklog.MaxPackets; i++)
            require(queue.TryEnqueue(new ArraySegment<byte>(Array.Empty<byte>()), 0), "Packet count is independently bounded.");
        require(!queue.TryEnqueue(new ArraySegment<byte>(Array.Empty<byte>()), 0), "Zero-byte messages cannot bypass the memory bound.");
        queue.Clear();
        require(queue.Count == 0 && queue.Bytes == 0 && !queue.HasExpired(100), "Disconnect clears both deadlines and payloads.");
    }

    private static void CheckCombo(Action<bool, string> require)
    {
        var combo = new ServerComboSequence();
        require(combo.CanStart(0, false, 0, float.NaN, 10), "The first attack starts from idle.");
        require(!combo.CanStart(1, false, 0, float.NaN, 10) && !combo.CanStart(2, false, 0, float.NaN, 10),
            "A fresh request sequence cannot bypass the first hit.");
        for (int index = 0; index < 2; index++)
        {
            require(!combo.CanStart(index + 1, true, index, .799f, 10), "An early follow-up is rejected.");
            require(combo.CanStart(index + 1, true, index, .8f, 10), "The normal next combo stage remains available.");
            require(!combo.CanStart(0, true, index, .9f, 10), "An active combo cannot be restarted early.");
            require(!combo.CanStart(index + 1, true, index, float.NaN, 10), "Missing animation timing cannot authorize a chain.");
            combo.Complete(index, 3, 10);
            require(combo.CanStart(index + 1, false, 0, 0, 10.2), "A completed attack permits the delayed owner continuation.");
            require(!combo.CanStart(index + 1, false, 0, 0, 10.351), "Continuation expires after the bounded network grace.");
            combo.Reset();
            require(!combo.CanStart(index + 1, false, 0, 0, 10.2), "Cancellation revokes continuation immediately.");
        }
        combo.Complete(2, 3, 10);
        require(!combo.CanStart(3, false, 0, 0, 10) && combo.CanStart(0, false, 0, 0, 10), "Finishing the chain requires a fresh first hit.");
    }

    private static async Task CheckReplacement(Action<bool, string> require)
    {
        var lifetime = new RoomServiceLifetime();
        var old = lifetime.Begin("a10", Room);
        old.MarkMembershipPossible();
        var ack = new TaskCompletionSource<int>();
        var lateAck = new TaskCompletionSource<bool>();
        using var timeout = new CancellationTokenSource();
        Task<int> waiting = lifetime.Mutations.Enqueue(old.Ticket.MembershipKey, () =>
            lifetime.Responses.ExecuteAsync(old.Ticket.MembershipKey, () => ack.Task, timeout.Token,
                _ => lateAck.TrySetResult(lifetime.AuthorizeCleanup(old, "a10"))));
        lifetime.End(old); // old manager OnDisable/OnDestroy
        var replacement = lifetime.Begin("a10", Room);
        lifetime.End(old); // delayed teardown must not invalidate the replacement
        require(lifetime.Flows.IsCurrent(replacement.Ticket, "a10"), "Old teardown cannot cancel the new manager's room.");
        bool replacementSent = false;
        Task<int> second = lifetime.Mutations.Enqueue(replacement.Ticket.MembershipKey, () =>
            lifetime.Responses.ExecuteAsync(replacement.Ticket.MembershipKey,
                () => { replacementSent = true; return Task.FromResult(2); }, CancellationToken.None));
        require(!replacementSent, "Manager replacement cannot bypass an in-flight membership write.");
        timeout.Cancel();
        try { await waiting; require(false, "Old response should time out."); }
        catch (RoomServiceUnconfirmedException) { }
        try { await second; require(false, "Replacement must respect old response quarantine."); }
        catch (RoomServiceUnconfirmedException) { }
        require(!replacementSent, "A timed-out old Join stays quarantined across manager replacement.");
        ack.SetResult(1);
        require(await Task.WhenAny(lateAck.Task, Task.Delay(2000)) == lateAck.Task, "Late acknowledgement is observed.");
        require(!await lateAck.Task && replacement.MembershipPossible,
            "Old acknowledgement transfers cleanup ownership without removing the replacement's membership.");
        int result = await lifetime.Mutations.Enqueue(replacement.Ticket.MembershipKey, () =>
            lifetime.Responses.ExecuteAsync(replacement.Ticket.MembershipKey,
                () => Task.FromResult(2), CancellationToken.None));
        require(result == 2 && !lifetime.AuthorizeCleanup(old, "a10"), "Confirmed replacement is protected from repeated old cleanup.");
        lifetime.End(replacement);
        require(lifetime.AuthorizeCleanup(replacement, "a10"), "A truly abandoned replacement is still cleaned up.");
        var accountB = lifetime.Begin("b20", Room);
        require(lifetime.AuthorizeCleanup(old, "b20") && !accountB.MembershipPossible,
            "Account changes isolate cleanup from the new account's membership.");
    }
}
