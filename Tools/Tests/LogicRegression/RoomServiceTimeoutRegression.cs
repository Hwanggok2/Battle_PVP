using System;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;

internal static class RoomServiceTimeoutRegression
{
    internal static void Run(Action<bool, string> require) => RunAsync(require).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<bool, string> require)
    {
        var queue = new RoomOperationQueue();
        var gate = new RoomServiceResponseGate();
        var response = new TaskCompletionSource<int>();
        var recovered = new TaskCompletionSource<int>();
        using var deadline = new CancellationTokenSource();
        Task<int> original = queue.Enqueue("account/room", () => gate.ExecuteAsync("account/room",
            () => response.Task, deadline.Token, value => recovered.TrySetResult(value)));
        require(!original.IsCompleted, "The original mutation must wait for its response before the deadline.");
        deadline.Cancel();
        await ExpectUnconfirmed(original, require);
        require(queue.PendingKeys == 0 && gate.UnconfirmedKeys == 1,
            "The timed-out queue must release waiters without declaring the mutation complete.");
        bool secondSent = false;
        await ExpectUnconfirmed(queue.Enqueue("account/room", () => gate.ExecuteAsync("account/room", () =>
        {
            secondSent = true;
            return Task.FromResult(2);
        }, CancellationToken.None)), require);
        require(!secondSent, "A new mutation must never overlap a timed-out mutation of the same membership.");
        require(await queue.Enqueue("account/other", () => gate.ExecuteAsync("account/other",
            () => Task.FromResult(3), CancellationToken.None)) == 3,
            "An unconfirmed room must not block another room.");
        require(await queue.Enqueue("otherAccount/room", () => gate.ExecuteAsync("otherAccount/room",
            () => Task.FromResult(4), CancellationToken.None)) == 4,
            "An unconfirmed account must not block a different account's membership.");
        response.SetResult(7);
        require(await recovered.Task == 7 && !gate.IsUnconfirmed("account/room"),
            "Only an actual late response may clear quarantine and initiate deferred cleanup.");
        require(await queue.Enqueue("account/room", () => gate.ExecuteAsync("account/room",
            () => Task.FromResult(8), CancellationToken.None)) == 8,
            "A confirmed old response must allow a subsequent serialized mutation.");

        using var failedDeadline = new CancellationTokenSource();
        var lateFailure = new TaskCompletionSource<int>();
        bool failureRecovered = false;
        Task<int> uncertain = gate.ExecuteAsync("failed/room", () => lateFailure.Task,
            failedDeadline.Token, _ => failureRecovered = true);
        failedDeadline.Cancel();
        await ExpectUnconfirmed(uncertain, require);
        lateFailure.SetException(new InvalidOperationException("Transport failure"));
        await ExpectUnconfirmed(gate.ExecuteAsync("failed/room", () => Task.FromResult(9), CancellationToken.None), require);
        require(gate.IsUnconfirmed("failed/room") && !failureRecovered,
            "A late transport failure must not be mistaken for a server acknowledgement.");
        await ExpectUnconfirmed(gate.ExecuteAsync("immediate/room",
            () => Task.FromException<int>(new InvalidOperationException()), CancellationToken.None), require);
        require(gate.IsUnconfirmed("immediate/room"),
            "An immediate transport error cannot prove that the server did not execute the mutation.");

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        bool cancelledSent = false;
        try
        {
            await gate.ExecuteAsync("unsent/room", () => { cancelledSent = true; return Task.FromResult(1); }, alreadyCancelled.Token);
            require(false, "Cancellation before dispatch must abort.");
        }
        catch (OperationCanceledException) { require(true, "Cancellation before dispatch propagated."); }
        require(!cancelledSent && !gate.IsUnconfirmed("unsent/room"),
            "An unsent cancelled request must not quarantine a membership.");

        using var preparation = new CancellationTokenSource();
        var allocation = new TaskCompletionSource<int>();
        int committed = 0;
        Task oldPreparation = CommitAfterPreparationAsync(allocation.Task, preparation.Token, value => committed = value);
        preparation.Cancel();
        try { await oldPreparation; require(false, "Cancelled Relay preparation must finish without committing."); }
        catch (OperationCanceledException) { require(true, "Relay cancellation propagated."); }
        await CommitAfterPreparationAsync(Task.FromResult(20), CancellationToken.None, value => committed = value);
        allocation.SetResult(10);
        require(committed == 20, "A detached old Relay result must not overwrite the next preparation.");
        require(await ServiceTaskDeadline.WaitAsync(Task.FromResult(30), CancellationToken.None) == 30,
            "A completed current preparation result must remain usable.");
        await CheckLeaseRefreshRetryAsync(require);
    }

    private static async Task CheckLeaseRefreshRetryAsync(Action<bool, string> require)
    {
        var queue = new RoomOperationQueue();
        var gate = new RoomServiceResponseGate();
        var lease = new HostRoomLease();
        lease.BeginRegistration(0d);
        require(lease.Accept(0d, 0d, 1000d, 61000d) && lease.TryBeginHeartbeat(15d),
            "A registered host must begin its first heartbeat normally.");
        var oldResponse = new TaskCompletionSource<int>();
        using var deadline = new CancellationTokenSource();
        Task<int> first = queue.Enqueue("host/room", () =>
            RoomServiceResponseGate.WaitForLeaseRefreshAsync(() => oldResponse.Task, deadline.Token));
        deadline.Cancel();
        try { await first; require(false, "A missing heartbeat response must end its wait."); }
        catch (OperationCanceledException) { require(true, "The heartbeat wait reached its deadline."); }
        lease.Failed(30d);
        require(queue.PendingKeys == 0 && gate.UnconfirmedKeys == 0 &&
            !lease.TryBeginHeartbeat(34d) && lease.TryBeginHeartbeat(35d),
            "Heartbeat timeout must allow the existing five-second retry without quarantining membership.");
        require(await queue.Enqueue("host/room", () =>
            RoomServiceResponseGate.WaitForLeaseRefreshAsync(() => Task.FromResult(2), CancellationToken.None)) == 2 &&
            lease.Accept(35d, 35d, 100000d, 160000d),
            "The next heartbeat must renew a still-live host after the retry delay.");
        oldResponse.SetResult(1);
        require(first.IsCanceled && !lease.HasExpired(75d) && lease.HasExpired(95d),
            "An old timed-out response must not replace the accepted retry deadline.");
        try
        {
            await RoomServiceResponseGate.WaitForLeaseRefreshAsync(
                () => Task.FromException<int>(new InvalidOperationException("temporary service error")), CancellationToken.None);
            require(false, "Heartbeat service error must propagate to the retry scheduler.");
        }
        catch (InvalidOperationException) { require(true, "Heartbeat service failure propagated."); }
        require(await RoomServiceResponseGate.WaitForLeaseRefreshAsync(() => Task.FromResult(3), CancellationToken.None) == 3,
            "A terminal heartbeat SDK error must not permanently disable subsequent heartbeats.");
        require(await queue.Enqueue("host/room", () => gate.ExecuteAsync("host/room", () => Task.FromResult(4),
            CancellationToken.None)) == 4,
            "A failed heartbeat must not block a later explicit room closure.");
    }

    private static async Task CommitAfterPreparationAsync(Task<int> preparation, CancellationToken token, Action<int> commit)
    {
        int result = await ServiceTaskDeadline.WaitAsync(preparation, token);
        token.ThrowIfCancellationRequested();
        commit(result);
    }

    private static async Task ExpectUnconfirmed(Task<int> task, Action<bool, string> require)
    {
        try { await task; require(false, "An unconfirmed mutation must fail instead of waiting or dispatching again."); }
        catch (RoomServiceUnconfirmedException) { require(true, "Unconfirmed mutation rejected."); }
    }
}
