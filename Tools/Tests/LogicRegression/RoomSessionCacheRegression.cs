using System;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;

internal static class RoomSessionCacheRegression
{
    private const string RoomA = "battle_a10_0123456789abcdef0123456789abcdef";
    private const string RoomB = "battle_b20_fedcba9876543210fedcba9876543210";

    internal static void Run(Action<bool, string> require)
    {
        var snapshots = new RoomListSnapshotState();
        require(!snapshots.CanReuse(0), "No initial list snapshot exists.");
        require(snapshots.TryComplete(snapshots.Revision, 10), "A current list response is accepted.");
        require(snapshots.CanReuse(10) && snapshots.CanReuse(13), "The three-second cache includes its boundary.");
        require(!snapshots.CanReuse(13.001) && !snapshots.CanReuse(9), "Expired or clock-reset snapshots cannot be reused.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            require(!snapshots.CanReuse(invalid), "A nonfinite list clock is rejected.");
            require(!snapshots.TryComplete(snapshots.Revision, invalid), "A nonfinite completion cannot refresh the cache.");
        }
        ulong old = snapshots.Revision;
        snapshots.Invalidate();
        require(!snapshots.CanReuse(11), "A mutation immediately invalidates list freshness.");
        require(!snapshots.TryComplete(old, 11), "A response started before a mutation cannot commit.");
        require(!snapshots.CanReuse(11), "A rejected old response must not make the cache fresh.");
        require(snapshots.TryComplete(snapshots.Revision, 12), "A new post-mutation request may commit.");
        require(!snapshots.TryComplete(old, 13) && snapshots.CanReuse(15) && !snapshots.CanReuse(15.001),
            "An even later old response cannot extend the new snapshot deadline.");
        for (int i = 0; i < 10; i++)
        {
            old = snapshots.Revision;
            snapshots.Invalidate();
            require(snapshots.Revision != old && !snapshots.TryComplete(old, 20 + i), "Every mutation invalidates older list revisions.");
            require(snapshots.TryComplete(snapshots.Revision, 20 + i), "Repeated mutation and refresh remain usable.");
        }
        old = snapshots.Revision;
        snapshots.Invalidate();
        require(!snapshots.TryComplete(old, 30), "A normal stale response cannot overwrite mutation changes.");
        require(snapshots.TryComplete(old, 30, discardUnverified: true), "A failed read must still replace unverified rooms with empty after mutation.");
        require(snapshots.CanReuse(33) && !snapshots.CanReuse(33.001), "Failed reads retain the existing three-second empty-cache interval.");

        var flows = new RoomFlowGeneration();
        var first = new RoomSessionState(flows.Begin("a10", RoomA));
        require(!first.TryQueueCleanup(), "A flow that never sent membership writes cannot leave.");
        require(first.TryQueueRelay() && !first.TryQueueRelay(), "Relay preparation is queued once.");
        first.CompleteRelayPreparation();
        require(first.TryQueueRelay(), "A completed relay preparation releases only its own queue flag.");
        first.MarkMembershipPossible();
        require(first.TryQueueCleanup() && !first.TryQueueCleanup(), "Cleanup is queued once while membership is possible.");
        require(!first.AuthorizeCleanup(flows, "a10", first) && first.MembershipPossible,
            "A current flow cannot clean itself up or transfer ownership to itself.");

        var renewed = new RoomSessionState(flows.Begin("a10", RoomA));
        require(!first.AuthorizeCleanup(flows, "A10", renewed), "Same-room renewal suppresses the old Leave.");
        require(renewed.MembershipPossible && first.MembershipPossible, "The newest flow inherits cleanup while the old request retains its late-ACK recovery path.");
        first.ReleaseCleanupQueue();
        require(first.TryQueueCleanup() && !first.AuthorizeCleanup(flows, "a10", renewed),
            "A delayed old callback rechecks current membership instead of sending an unsafe Leave.");
        flows.Invalidate();
        require(renewed.TryQueueCleanup() && renewed.AuthorizeCleanup(flows, "a10", null),
            "Cancelling before the renewed Join was sent still cleans inherited membership.");
        renewed.ReleaseCleanupQueue();
        require(renewed.MembershipPossible && renewed.TryQueueCleanup(), "An unconfirmed cleanup can be retried through the existing response gate.");
        renewed.ConfirmCleanup();
        require(!renewed.MembershipPossible && !renewed.CleanupQueued && !renewed.TryQueueCleanup(),
            "Confirmed cleanup cannot be submitted twice.");

        var rejected = new RoomSessionState(flows.Begin("a10", RoomA));
        rejected.MarkMembershipPossible();
        flows.PreserveMembership(rejected.Ticket);
        var later = new RoomSessionState(flows.Begin("a10", RoomA));
        require(!rejected.AuthorizeCleanup(flows, "a10", later) && !later.MembershipPossible,
            "Duplicate-account preservation must never transfer cleanup of the existing connection.");
        later.MarkMembershipPossible();
        flows.Invalidate();
        require(!rejected.AuthorizeCleanup(flows, "a10", null) && later.AuthorizeCleanup(flows, "a10", null),
            "Preservation covers old tickets, not a future independently joined flow.");
        foreach (var replacement in new[] { ("a10", RoomB), ("b20", RoomA) })
        {
            var abandoned = new RoomSessionState(flows.Begin("a10", RoomA));
            abandoned.MarkMembershipPossible();
            var current = new RoomSessionState(flows.Begin(replacement.Item1, replacement.Item2));
            require(abandoned.AuthorizeCleanup(flows, replacement.Item1, current), "Changing room or account permits old membership cleanup.");
            require(abandoned.MembershipPossible && !current.MembershipPossible, "Different memberships never inherit cleanup responsibility.");
        }

        // Listing invalidation never changes the independent session lease or response quarantine.
        var lease = new HostRoomLease();
        lease.BeginRegistration(0);
        require(lease.Accept(0, 1, 1000, 61000), "A session receives its own server lease.");
        snapshots.Invalidate();
        require(lease.TryBeginHeartbeat(15), "List invalidation cannot postpone session heartbeat.");
        lease.Failed(15);
        require(!lease.TryBeginHeartbeat(19.99) && lease.TryBeginHeartbeat(20), "Heartbeat failure retains the five-second retry contract.");
        require(lease.HasExpired(60), "List refresh cannot extend the host's lease.");
        CheckLateAcknowledgement(require).GetAwaiter().GetResult();
    }

    private static async Task CheckLateAcknowledgement(Action<bool, string> require)
    {
        var flows = new RoomFlowGeneration();
        var old = new RoomSessionState(flows.Begin("a10", RoomA));
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
        try { await waiting; require(false, "The first write should be unconfirmed."); }
        catch (RoomServiceUnconfirmedException) { require(true, "The first write stays quarantined."); }
        var renewed = new RoomSessionState(flows.Begin("a10", RoomA));
        old.TryQueueCleanup();
        require(!old.AuthorizeCleanup(flows, "a10", renewed) && renewed.MembershipPossible, "The renewed flow inherits old uncertain membership.");
        bool sent = false;
        try { await gate.ExecuteAsync(renewed.Ticket.MembershipKey, () => { sent = true; return Task.FromResult(2); }, CancellationToken.None); }
        catch (RoomServiceUnconfirmedException) { }
        require(!sent, "A renewed request cannot bypass the old unconfirmed write.");
        flows.Invalidate();
        response.SetResult(1);
        bool completed = await Task.WhenAny(lateCleanup.Task, Task.Delay(2000)) == lateCleanup.Task;
        require(completed, "A missing late-cleanup callback must fail without hanging the regression runner.");
        if (!completed) return;
        require(await lateCleanup.Task, "Late ACK must still trigger cleanup after the inherited attempt was cancelled.");
        require(!gate.IsUnconfirmed(old.Ticket.MembershipKey), "Only the real old ACK releases the response quarantine.");
    }
}
