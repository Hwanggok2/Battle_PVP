using System;
using System.Threading.Tasks;
using BattlePvp.Networking;
using BattlePvp.UI;

internal static class RoomFlowRegression
{
    private const string RoomA = "battle_a10_0123456789abcdef0123456789abcdef";
    private const string RoomB = "battle_b20_fedcba9876543210fedcba9876543210";

    internal static void Run(Action<bool, string> require)
    {
        CheckFlowReplacementAndAccountChanges(require);
        CheckMembershipCompensation(require);
        CheckInvalidFlowRequests(require);
        CheckBannerRoomChanges(require);
        CheckBannerActivation(require);
        CheckOperationQueue(require);
    }

    private static void CheckFlowReplacementAndAccountChanges(Action<bool, string> require)
    {
        var flow = new RoomFlowGeneration();
        require(flow.Current == null && !flow.IsCurrent(null, "a10"), "An empty room flow must not accept a callback.");
        require(!flow.ShouldCompensate(null, "a10"), "No request means no membership to compensate.");
        RoomFlowTicket firstA = flow.Begin("A10", RoomA);
        require(ReferenceEquals(flow.Current, firstA) && firstA.AccountId == "a10" && firstA.RoomId == RoomA,
            "A room request must retain its room and normalize the requesting account.");
        require(flow.IsCurrent(firstA, "a10") && flow.IsCurrent(firstA, "A10"),
            "Account casing must not make a current room response stale.");
        require(!flow.IsCurrent(firstA, "b20"), "Switching accounts must immediately reject the previous account's response.");
        require(flow.ShouldCompensate(firstA, "b20"), "Changing accounts must permit cleanup of the old account even before a new flow begins.");
        foreach (string invalidAccount in new[] { null, "", " a10", "guest" })
            require(!flow.IsCurrent(firstA, invalidAccount), "A missing or invalid authenticated account must reject room callbacks.");

        RoomFlowTicket firstB = flow.Begin("a10", RoomB);
        require(firstB.Generation > firstA.Generation && flow.IsCurrent(firstB, "a10"),
            "Switching from room A to room B must create a current generation.");
        require(!flow.IsCurrent(firstA, "a10"), "Room A's late success or failure must not replace room B.");
        require(flow.ShouldCompensate(firstA, "a10") && !flow.ShouldCompensate(firstB, "a10"),
            "Only the superseded membership should be eligible for cleanup.");

        RoomFlowTicket secondB = flow.Begin("A10", RoomB);
        require(secondB.Generation > firstB.Generation && !flow.IsCurrent(firstB, "a10"),
            "A new request for the same room must still invalidate its predecessor.");
        require(flow.IsCurrent(secondB, "a10") && !flow.ShouldCompensate(firstB, "a10"),
            "Cleaning up an old same-room response must not remove the current membership.");

        RoomFlowTicket otherAccountB = flow.Begin("B20", RoomB);
        require(!flow.IsCurrent(secondB, "b20") && !flow.IsCurrent(secondB, "a10"),
            "The old account's response must stay stale after a new account joins the same room.");
        require(flow.IsCurrent(otherAccountB, "b20") && flow.ShouldCompensate(secondB, "b20"),
            "Membership cleanup must distinguish two accounts in the same room.");

        flow.Invalidate();
        require(flow.Current == null && !flow.IsCurrent(otherAccountB, "b20"),
            "Clearing room context must invalidate in-flight callbacks.");
        require(flow.ShouldCompensate(otherAccountB, "b20"), "Clearing an unpreserved request must permit its eventual cleanup.");
        flow.Invalidate();
        RoomFlowTicket reopenedB = flow.Begin("b20", RoomB);
        require(reopenedB.Generation > otherAccountB.Generation && flow.IsCurrent(reopenedB, "b20"),
            "Repeated clears must not prevent a later valid request.");
        require(!flow.IsCurrent(otherAccountB, "b20") && !flow.ShouldCompensate(otherAccountB, "b20"),
            "Reopening the same membership must reject old callbacks without cleaning up the new membership.");
    }

    private static void CheckMembershipCompensation(Action<bool, string> require)
    {
        var flow = new RoomFlowGeneration();
        flow.PreserveMembership(null);
        RoomFlowTicket olderA = flow.Begin("a10", RoomA);
        RoomFlowTicket retainedA = flow.Begin("A10", RoomA);
        flow.PreserveMembership(retainedA);
        flow.PreserveMembership(retainedA);
        flow.Begin("a10", RoomB);
        require(!flow.IsCurrent(retainedA, "a10") && !flow.ShouldCompensate(retainedA, "a10"),
            "Preserving a hosted membership must not preserve its right to update current room UI.");
        require(!flow.ShouldCompensate(olderA, "a10"),
            "Rejecting a duplicate room flow must also prevent cleanup by older attempts at that membership.");
        flow.Invalidate();
        require(!flow.ShouldCompensate(retainedA, "a10"), "Clearing UI context must not delete an explicitly retained membership.");
        RoomFlowTicket sameMembership = flow.Begin("A10", RoomA);
        require(flow.IsCurrent(sameMembership, "a10"), "An earlier duplicate rejection must not prevent a later valid room flow.");
        flow.Invalidate();
        require(flow.ShouldCompensate(sameMembership, "a10"),
            "A later normal same-account, same-room flow must remain cleanable after leaving.");
        require(!flow.ShouldCompensate(olderA, "a10") && !flow.ShouldCompensate(retainedA, "a10"),
            "A later normal flow must not retroactively authorize cleanup by the earlier duplicate attempts.");
        flow.PreserveMembership(olderA);
        require(!flow.ShouldCompensate(retainedA, "a10") && flow.ShouldCompensate(sameMembership, "a10"),
            "An older preservation callback must not lower the cutoff or exempt a newer normal flow.");
        RoomFlowTicket otherAccount = flow.Begin("b20", RoomA);
        flow.Invalidate();
        require(flow.ShouldCompensate(otherAccount, "b20"), "Preserving one account's membership must not exempt another account from cleanup.");
        RoomFlowTicket otherRoom = flow.Begin("a10", RoomB);
        flow.Invalidate();
        require(flow.ShouldCompensate(otherRoom, "a10"), "Preserving one room must not exempt the same account's other rooms from cleanup.");
    }

    private static void CheckInvalidFlowRequests(Action<bool, string> require)
    {
        var flow = new RoomFlowGeneration();
        RoomFlowTicket valid = flow.Begin("a10", RoomA);
        foreach (var input in new[]
        {
            (Account: (string)null, Room: RoomA), (Account: "", Room: RoomA),
            (Account: " a10", Room: RoomA), (Account: "guest", Room: RoomA),
            (Account: "a10", Room: (string)null), (Account: "a10", Room: ""),
            (Account: "a10", Room: "reserved_room"), (Account: "a10", Room: RoomA.ToUpperInvariant())
        })
        {
            bool rejected = false;
            try { flow.Begin(input.Account, input.Room); }
            catch (ArgumentException) { rejected = true; }
            require(rejected, "Invalid room/account inputs must not create a flow ticket.");
            require(ReferenceEquals(flow.Current, valid) && flow.IsCurrent(valid, "a10"),
                "Rejected room requests must leave the current valid flow intact.");
        }
    }

    private static void CheckBannerRoomChanges(Action<bool, string> require)
    {
        var banner = new RoomBannerRequestState();
        banner.SetActive(true);
        uint firstA = banner.BeginRequest(RoomA);
        require(banner.IsCurrent(firstA, RoomA), "The current visible room must accept its metadata.");
        require(!banner.IsCurrent(firstA, RoomB), "A changed room context must reject the old room even before a new request starts.");
        require(!banner.IsCurrent(firstA, null) && !banner.IsCurrent(firstA, ""),
            "Cleared room context must not accept pending metadata.");

        uint firstB = banner.BeginRequest(RoomB);
        require(!banner.IsCurrent(firstA, RoomA) && !banner.IsCurrent(firstA, RoomB),
            "Room A's delayed metadata must not overwrite room B's banner.");
        require(banner.IsCurrent(firstB, RoomB), "Room B's current metadata must remain acceptable.");
        uint secondB = banner.BeginRequest(RoomB);
        require(!banner.IsCurrent(firstB, RoomB) && banner.IsCurrent(secondB, RoomB),
            "Repeated requests for the same room must accept only the newest response.");
        uint returnedA = banner.BeginRequest(RoomA);
        require(!banner.IsCurrent(firstA, RoomA) && !banner.IsCurrent(secondB, RoomA),
            "Returning to room A must not revive a response from its previous visit.");
        require(banner.IsCurrent(returnedA, RoomA), "The new room A request must still succeed.");
    }

    private static void CheckBannerActivation(Action<bool, string> require)
    {
        var banner = new RoomBannerRequestState();
        uint inactive = banner.BeginRequest(RoomA);
        require(!banner.IsCurrent(inactive, RoomA), "An inactive banner must reject metadata responses.");
        banner.SetActive(true);
        require(!banner.IsCurrent(inactive, RoomA), "Activating the banner must not revive a request from its inactive lifetime.");
        uint active = banner.BeginRequest(RoomA);
        require(banner.IsCurrent(active, RoomA), "A fresh request after activation must succeed.");
        banner.SetActive(false);
        require(!banner.IsCurrent(active, RoomA), "Disabling the banner must invalidate pending callbacks.");
        uint duringDisable = banner.BeginRequest(RoomA);
        require(!banner.IsCurrent(duringDisable, RoomA), "Starting a request while disabled must not make the banner writable.");
        banner.SetActive(true);
        require(!banner.IsCurrent(active, RoomA) && !banner.IsCurrent(duringDisable, RoomA),
            "Reactivation must reject both pre-disable and disabled-lifetime responses.");
        uint reactivated = banner.BeginRequest(RoomA);
        require(banner.IsCurrent(reactivated, RoomA), "A reactivated banner must accept a fresh request for the same room.");
        banner.SetActive(false);
        banner.SetActive(false);
        banner.SetActive(true);
        uint latest = banner.BeginRequest(RoomB);
        require(!banner.IsCurrent(reactivated, RoomA) && banner.IsCurrent(latest, RoomB),
            "Repeated disable cleanup must leave the next banner lifetime usable and previous callbacks stale.");
    }

    private static void CheckOperationQueue(Action<bool, string> require)
    {
        var queue = new RoomOperationQueue();
        var releaseFirst = new TaskCompletionSource<bool>();
        bool firstStarted = false, secondStarted = false;
        Task<int> first = queue.Enqueue("a10/" + RoomA, async () =>
        {
            firstStarted = true;
            await releaseFirst.Task;
            return 1;
        });
        Task<int> second = queue.Enqueue("a10/" + RoomA, () =>
        {
            secondStarted = true;
            return Task.FromResult(2);
        });
        require(firstStarted && !secondStarted && queue.PendingKeys == 1,
            "A second operation on the same membership must wait for the first operation to finish.");
        Task<int> otherRoom = queue.Enqueue("a10/" + RoomB, () => Task.FromResult(3));
        require(otherRoom.IsCompletedSuccessfully && !first.IsCompleted && !secondStarted,
            "A blocked room operation must not prevent an independent room from completing.");
        require(queue.PendingKeys == 1, "Completing another room must remove only that room's queue tail.");
        releaseFirst.SetResult(true);
        require(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully && secondStarted,
            "Same-membership operations must resume in order and preserve their results.");
        require(first.GetAwaiter().GetResult() == 1 && second.GetAwaiter().GetResult() == 2 &&
            otherRoom.GetAwaiter().GetResult() == 3, "Completed operations must return their own results.");
        require(queue.PendingKeys == 0, "Finishing the last same-membership operation must release its queue tail.");

        var releaseFailure = new TaskCompletionSource<bool>();
        bool recoveryStarted = false;
        Task<int> failure = queue.Enqueue<int>("a10/" + RoomA, async () =>
        {
            await releaseFailure.Task;
            throw new InvalidOperationException("Synthetic room operation failure.");
        });
        Task<int> recovery = queue.Enqueue("a10/" + RoomA, () =>
        {
            recoveryStarted = true;
            return Task.FromResult(4);
        });
        require(!recoveryStarted && queue.PendingKeys == 1,
            "A queued recovery must not overtake an operation that has not yet failed.");
        releaseFailure.SetResult(true);
        require(failure.IsFaulted && recovery.IsCompletedSuccessfully,
            "Explicitly completing the failed operation must finish both tasks without an external wait.");
        bool observedFailure = false;
        try { failure.GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { observedFailure = true; }
        require(observedFailure, "A failed operation must propagate its error to its caller.");
        require(recovery.GetAwaiter().GetResult() == 4 && recoveryStarted && queue.PendingKeys == 0,
            "A failed room operation must unblock its successor and leave no pending queue entry.");
    }
}
