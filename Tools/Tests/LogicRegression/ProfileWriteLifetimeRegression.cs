using System;
using System.Collections.Generic;
using BattlePvp.Networking;

internal static class ProfileWriteLifetimeRegression
{
    private sealed class Owner
    {
        public string Account = "title/a";
        public double Now;
        public readonly List<Action> Success = new List<Action>();
        public readonly List<Action<string>> Rejected = new List<Action<string>>();
        public readonly List<Action<string>> Unknown = new List<Action<string>>();
        public readonly List<string> Writes = new List<string>();
        public readonly ProfileRequestQueue<string> Queue;
        public Owner(ProfileWriteCoordinator writes = null)
        {
            Queue = new ProfileRequestQueue<string>((ok, _) => ok("profile"), null, error => error,
                () => Now, () => Account, writeWithUnconfirmed: (data, ok, rejected, unknown) =>
                { Writes.Add(data["value"]); Success.Add(ok); Rejected.Add(rejected); Unknown.Add(unknown); }, writeCoordinator: writes);
        }
        public void Save(string value, Action<bool, string> completed = null) =>
            Queue.Save(new Dictionary<string, string> { { "value", value } }, completed);
    }

    internal static void Run(Action<bool, string> require)
    {
        CheckLeases(require);
        CheckReplacementAndNamespaces(require);
        CheckTimeoutAndAcknowledgement(require);
        CheckConfirmedAndUnknownFailures(require);
        CheckReentryAndIsolation(require);
        CheckReplacementDuringQueuedCompletion(require);
    }

    private static void CheckLeases(Action<bool, string> require)
    {
        var writes = new ProfileWriteCoordinator();
        require(writes.TryAcquire("title/a", out ProfileWriteLease first), "The first actual write acquires the account lease.");
        require(!writes.IsBlocked("TITLE/A", first), "The owning queue may enqueue its normal FIFO saves.");
        require(writes.IsBlocked("TITLE/A") && !writes.TryAcquire("TITLE/A", out _), "Another owner of the same account cannot overlap the write.");
        require(writes.TryAcquire("title/b", out ProfileWriteLease otherAccount), "A different account has an independent lease.");
        require(writes.TryAcquire("other-title/a", out ProfileWriteLease otherTitle), "The same account ID in another title is independent.");
        require(writes.PendingAccounts == 3, "Only unresolved actual writes occupy coordinator entries.");
        require(!writes.Confirm(null), "A missing lease is not a completion proof.");
        require(writes.Confirm(first), "The exact original lease may be confirmed.");
        require(writes.TryAcquire("TITLE/A", out ProfileWriteLease next), "A confirmed write permits a fresh lease.");
        require(!writes.Confirm(first) && writes.IsBlocked("title/a"), "An old duplicate cannot release the replacement write.");
        require(writes.Confirm(next) && writes.Confirm(otherAccount) && writes.Confirm(otherTitle) && writes.PendingAccounts == 0,
            "Confirmed writes leave no historical cache entries behind.");
    }

    private static void CheckReplacementAndNamespaces(Action<bool, string> require)
    {
        var shared = new ProfileWriteCoordinator();
        var old = new Owner(shared);
        var replacement = new Owner(shared);
        bool? blockedResult = null;
        old.Save("old request");
        old.Queue.ResetSession();
        replacement.Save("must not overlap", (ok, _) => blockedResult = ok);
        require(replacement.Writes.Count == 0 && blockedResult == false, "Replacing a reset owner must not bypass its unresolved write.");
        replacement.Account = "title/b";
        replacement.Save("different account");
        require(replacement.Writes.Count == 1, "Another account continues while the old owner is gone.");
        replacement.Account = "other-title/a";
        replacement.Save("different title");
        require(replacement.Writes.Count == 2, "Different titles must not falsely block each other's account IDs.");
        replacement.Account = "TITLE/A";
        replacement.Save("still old account");
        require(replacement.Writes.Count == 2 && shared.PendingAccounts == 3, "Switching away and back preserves every actual write's ownership.");
        old.Success[0]();
        replacement.Save("safe retry");
        require(replacement.Writes.Count == 3 && replacement.Writes[2] == "safe retry", "An old owner's actual response releases the account for the replacement.");
        old.Success[0]();
        old.Rejected[0]("duplicate old rejection");
        var third = new Owner(shared);
        third.Save("must still wait");
        require(third.Writes.Count == 0, "Old duplicate callbacks cannot release the replacement owner's active write.");
        replacement.Success[2]();
        third.Save("third owner retry");
        require(third.Writes.Count == 1, "Only the replacement write's own response allows the third owner to continue.");
    }

    private static void CheckTimeoutAndAcknowledgement(Action<bool, string> require)
    {
        var shared = new ProfileWriteCoordinator();
        var old = new Owner(shared);
        var results = new List<bool>();
        old.Save("old active", (ok, _) => results.Add(ok));
        old.Save("old queued", (ok, _) => results.Add(ok));
        old.Now = 15;
        old.Queue.Tick();
        require(results.Count == 2 && !results[0] && !results[1], "Timeout completes old callers with failure once.");
        var replacement = new Owner(shared);
        replacement.Save("replacement while unknown", (ok, _) => results.Add(ok));
        require(replacement.Writes.Count == 0 && results.Count == 3 && !results[2], "A new owner cannot reinterpret timeout as confirmed cancellation.");
        old.Queue.ResetSession();
        replacement.Queue.ResetSession();
        replacement.Now = 100000;
        replacement.Queue.Tick();
        replacement.Save("time alone is no proof");
        require(replacement.Writes.Count == 0 && shared.PendingAccounts == 1, "Session resets and long time passage do not expire an unresolved lease.");
        old.Success[0]();
        require(results.Count == 3 && replacement.Writes.Count == 0, "A late response does not replay old payloads or notify abandoned callbacks twice.");
        replacement.Save("fresh after confirmation");
        require(replacement.Writes.Count == 1, "Explicit retry after confirmation starts a new actual write.");
    }

    private static void CheckConfirmedAndUnknownFailures(Action<bool, string> require)
    {
        foreach (bool confirmed in new[] { false, true })
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            var replacement = new Owner(shared);
            old.Save("first");
            if (confirmed) old.Rejected[0]("confirmed rejection");
            else old.Unknown[0]("unknown transport outcome");
            old.Queue.ResetSession();
            replacement.Save("next");
            require(replacement.Writes.Count == (confirmed ? 1 : 0), "Only a confirmed service rejection releases the account for a new owner.");
            if (!confirmed)
            {
                old.Unknown[0]("duplicate unknown");
                require(shared.PendingAccounts == 1, "Repeated uncertain errors cannot release the lease.");
                old.Rejected[0]("eventual confirmed rejection");
                replacement.Save("recovered");
                require(replacement.Writes.Count == 1, "An eventual confirmed rejection restores the retry path.");
            }
        }
    }

    private static void CheckReentryAndIsolation(Action<bool, string> require)
    {
        var shared = new ProfileWriteCoordinator();
        var old = new Owner(shared);
        var replacement = new Owner(shared);
        old.Save("old", (_, _) => replacement.Save("reentrant after reset"));
        old.Queue.ResetSession();
        require(replacement.Writes.Count == 0, "Reset callbacks cannot reenter through a replacement and bypass the outstanding lease.");
        old.Success[0]();
        replacement.Save("new", (_, _) => old.Save("reentrant after actual completion"));
        replacement.Success[0]();
        require(old.Writes.Count == 2, "A confirmed completion releases ownership before notifying reentrant callers.");
        replacement.Success[0]();
        var observer = new Owner(shared);
        observer.Save("blocked by reentrant owner");
        require(observer.Writes.Count == 0, "Duplicate callbacks cannot free a lease acquired from a previous completion callback.");

        var isolatedA = new Owner();
        var isolatedB = new Owner();
        isolatedA.Save("test A");
        isolatedB.Save("test B");
        require(isolatedA.Writes.Count == 1 && isolatedB.Writes.Count == 1, "Existing injected test queues remain isolated unless a coordinator is explicitly shared.");
    }

    private static void CheckReplacementDuringQueuedCompletion(Action<bool, string> require)
    {
        var shared = new ProfileWriteCoordinator();
        var old = new Owner(shared);
        var replacement = new Owner(shared);
        int failed = 0;
        old.Save("active", (_, _) => replacement.Save("new owner"));
        for (int i = 0; i < 4096; i++) old.Save("queued", (ok, _) => { if (!ok) failed++; });
        old.Success[0]();
        require(failed == 4096 && old.Writes.Count == 1 && replacement.Writes.Count == 1,
            "A replacement taking the lease during completion must fail the old queue without recursive stack growth or dispatch.");
        replacement.Success[0]();
        old.Save("later retry");
        require(old.Writes.Count == 2, "Draining the abandoned queue must leave the old owner retryable after confirmation.");
    }
}
