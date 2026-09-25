using System;
using System.Collections.Generic;
using BattlePvp.Networking;

internal static class ProfileRequestQueueRegression
{
    internal static void Run(Action<bool, string> require)
    {
        CheckDeferredInitialization(require);
        CheckLoadDeadlinesAndDuplicates(require);
        CheckSaveTimeoutAndLateResponse(require);
        CheckResetAndAccountBarriers(require);
        CheckCallbackIsolationAndReentry(require);
        CheckNormalFailureAndSnapshots(require);
        CheckUnconfirmedServiceFailure(require);
    }

    private static void CheckDeferredInitialization(Action<bool, string> require)
    {
        foreach (string firstOperation in new[] { "load", "save", "tick", "reset" })
        {
            bool runtimeReady = false;
            int externalCalls = 0;
            int reads = 0;
            int writes = 0;
            int failures = 0;
            string account = "before-runtime";
            var coordinator = new ProfileWriteCoordinator();
            void ExternalCall()
            {
                externalCalls++;
                if (!runtimeReady) throw new InvalidOperationException("SDK/Unity access during construction.");
            }

            var queue = new ProfileRequestQueue<string>(
                (success, _) => { ExternalCall(); reads++; success("loaded"); },
                (_, success, _) => { ExternalCall(); writes++; success(); },
                error => { ExternalCall(); failures++; return error; },
                () => { ExternalCall(); return 0d; },
                () => { ExternalCall(); return account; },
                _ => ExternalCall(), writeCoordinator: coordinator);
            require(externalCalls == 0, "Constructing the queue must not invoke SDK, clock or callback delegates.");

            runtimeReady = true;
            account = "runtime-account";
            string loaded = null;
            bool? saved = null;
            var data = new Dictionary<string, string> { { "profile", "value" } };
            switch (firstOperation)
            {
                case "load": queue.Load(value => loaded = value); break;
                case "save": queue.Save(data, (ok, _) => saved = ok); break;
                case "tick": queue.Tick(); break;
                case "reset": queue.ResetSession(); break;
            }
            require(externalCalls > 0, "The first runtime operation must resolve the current account.");
            require(firstOperation == "reset" || failures == 0,
                "Initial account binding must not report a fictitious session-change failure.");
            require(reads == (firstOperation == "load" ? 1 : 0) && writes == (firstOperation == "save" ? 1 : 0),
                "Initialization must not dispatch unrelated profile reads or writes.");
            if (firstOperation == "load") require(loaded == "loaded", "The first load must complete normally.");
            if (firstOperation == "save") require(saved == true, "The first save must complete normally.");

            require(coordinator.TryAcquire(account, out ProfileWriteLease lease), "The runtime account lease must be available.");
            int priorWrites = writes;
            saved = null;
            queue.Save(data, (ok, _) => saved = ok);
            require(saved == false && writes == priorWrites,
                "Deferred initialization must use the current account and respect its existing write lease.");
            coordinator.Confirm(lease);
            queue.Save(data, (ok, _) => saved = ok);
            require(saved == true && writes == priorWrites + 1,
                "Resolving the runtime account's lease must allow a new save.");
        }
    }

    private sealed class Fixture
    {
        public double Now;
        public string Account = "a";
        public int CallbackErrors;
        public readonly List<Action<string>> Reads = new List<Action<string>>();
        public readonly List<Action<string>> ReadFailures = new List<Action<string>>();
        public readonly List<Dictionary<string, string>> Writes = new List<Dictionary<string, string>>();
        public readonly List<Action> Successes = new List<Action>();
        public readonly List<Action<string>> Failures = new List<Action<string>>();
        public readonly List<Action<string>> Unconfirmed = new List<Action<string>>();
        public readonly ProfileRequestQueue<string> Queue;

        public Fixture()
        {
            Queue = new ProfileRequestQueue<string>((success, failure) => { Reads.Add(success); ReadFailures.Add(failure); },
                null, error => "failure:" + error, () => Now, () => Account, _ => CallbackErrors++,
                writeWithUnconfirmed: (data, success, failure, unconfirmed) =>
                { Writes.Add(data); Successes.Add(success); Failures.Add(failure); Unconfirmed.Add(unconfirmed); });
        }

        public void Save(string value, Action<bool, string> completed = null) =>
            Queue.Save(new Dictionary<string, string> { { "profile", value } }, completed);
    }

    private static void CheckLoadDeadlinesAndDuplicates(Action<bool, string> require)
    {
        var f = new Fixture();
        var values = new List<string>();
        f.Queue.Load(values.Add);
        f.Queue.Load(values.Add);
        require(f.Reads.Count == 1, "Concurrent load subscribers must share one request.");
        f.Now = ProfileRequestQueue<string>.TimeoutSeconds;
        f.Queue.Tick();
        require(values.Count == 2 && values[0].StartsWith("failure:") && values[1].StartsWith("failure:"),
            "Every load subscriber must receive a failure at the deadline.");
        f.Queue.Load(values.Add);
        require(f.Reads.Count == 2, "A timed-out load must be retryable in the same session.");
        f.Reads[0]("stale");
        f.ReadFailures[0]("stale failure");
        require(values.Count == 2, "Old same-session callbacks must not complete a replacement load.");
        f.Reads[1]("new");
        require(values.Count == 3 && values[2] == "new", "Only the replacement request may complete its subscriber.");
        f.Queue.Load(values.Add);
        f.Reads[1]("duplicate");
        require(values.Count == 3, "A duplicate completed response must not complete the next load.");
        f.Now += ProfileRequestQueue<string>.TimeoutSeconds;
        f.Reads[2]("too late without a tick");
        require(values.Count == 4 && values[3].StartsWith("failure:"), "Response callbacks must enforce the load deadline too.");
    }

    private static void CheckSaveTimeoutAndLateResponse(Action<bool, string> require)
    {
        var f = new Fixture();
        var completions = new List<bool>();
        f.Save("first", (ok, _) => completions.Add(ok));
        f.Save("queued", (ok, _) => completions.Add(ok));
        f.Now = ProfileRequestQueue<string>.TimeoutSeconds;
        f.Queue.Tick();
        require(completions.Count == 2 && !completions[0] && !completions[1],
            "A missing save response must fail both active and queued callers once.");
        f.Save("must not overlap", (ok, _) => completions.Add(ok));
        require(f.Writes.Count == 1 && completions.Count == 3 && !completions[2],
            "Timeout must not dispatch another write to the same profile.");
        f.Successes[0]();
        require(completions.Count == 3 && f.Writes.Count == 1,
            "A late acknowledgement must release quarantine without re-reporting success or replaying abandoned saves.");
        f.Save("retry", (ok, _) => completions.Add(ok));
        f.Save("next", (ok, _) => completions.Add(ok));
        require(f.Writes.Count == 2, "A confirmed old request must allow a fresh serialized save.");
        f.Failures[0]("duplicate old failure");
        require(f.Writes.Count == 2, "An old duplicate callback must not free the next write's slot.");
        f.Successes[1]();
        require(f.Writes.Count == 3 && completions.Count == 4 && completions[3], "Normal completion must resume FIFO writes.");
        f.Successes[2]();
        require(completions.Count == 5 && completions[4], "Each resumed save must complete once.");

        f.Save("late callback", (ok, _) => completions.Add(ok));
        f.Save("abandon", (ok, _) => completions.Add(ok));
        f.Now += ProfileRequestQueue<string>.TimeoutSeconds;
        f.Successes[3]();
        require(f.Writes.Count == 4 && completions.Count == 7 && !completions[5] && !completions[6],
            "A callback after the deadline must expire the active queue even before Tick runs.");
    }

    private static void CheckResetAndAccountBarriers(Action<bool, string> require)
    {
        var f = new Fixture();
        var loads = new List<string>();
        var saves = new List<bool>();
        f.Queue.Load(loads.Add);
        f.Save("a first", (ok, _) => saves.Add(ok));
        f.Save("a queued", (ok, _) => saves.Add(ok));
        f.Queue.ResetSession();
        require(loads.Count == 1 && loads[0].StartsWith("failure:") && saves.Count == 2 && !saves[0] && !saves[1],
            "Reset must complete all old load and save waiters with failure.");
        f.Reads[0]("old account");
        require(loads.Count == 1, "A reset load's response must stay ignored.");
        f.Save("a reset retry", (ok, _) => saves.Add(ok));
        require(f.Writes.Count == 1 && saves.Count == 3 && !saves[2],
            "Resetting the same account must not erase its unresolved write barrier.");
        f.Account = "b";
        f.Save("b independent", (ok, _) => saves.Add(ok));
        require(f.Writes.Count == 2 && f.Writes[1]["profile"] == "b independent",
            "Another account may save without waiting for account A's response.");
        f.Account = "a";
        f.Queue.Tick();
        f.Save("a still unresolved");
        require(f.Writes.Count == 2, "Switching away and back must not bypass the original account's barrier.");
        f.Successes[0]();
        f.Save("a recovered");
        require(f.Writes.Count == 3 && f.Writes[2]["profile"] == "a recovered", "A late old-session response must release only its own account.");
        f.Successes[1]();
        f.Save("a queued again");
        require(f.Writes.Count == 3, "Account B's late response must not free account A's current write.");
        f.Successes[2]();
        require(f.Writes.Count == 4, "The actual current write must release its own queued save.");
    }

    private static void CheckCallbackIsolationAndReentry(Action<bool, string> require)
    {
        var f = new Fixture();
        int notified = 0;
        f.Queue.Load(_ => throw new InvalidOperationException("consumer"));
        f.Queue.Load(_ => notified++);
        f.Reads[0]("ok");
        require(notified == 1 && f.CallbackErrors == 1, "One throwing load subscriber must not block another.");
        f.Save("first", (_, _) => throw new InvalidOperationException("save consumer"));
        f.Save("second", (_, _) => notified++);
        f.Successes[0]();
        f.Successes[1]();
        require(notified == 2 && f.CallbackErrors == 2, "A throwing save completion must not abandon the next save.");

        f.Queue.Load(_ => { f.Account = "new"; });
        string afterAccountChange = null;
        f.Queue.Load(value => afterAccountChange = value);
        f.Reads[1]("old account data");
        require(afterAccountChange.StartsWith("failure:"), "A session change inside one callback must invalidate later subscribers.");
        f.Save("old active", (_, _) => f.Account = "third");
        f.Save("must not send under new credentials", (_, _) => notified++);
        f.Successes[2]();
        require(f.Writes.Count == 3 && notified == 3,
            "A callback account switch must abandon queued old data before it uses the new credentials.");
    }

    private static void CheckNormalFailureAndSnapshots(Action<bool, string> require)
    {
        var f = new Fixture();
        var data = new Dictionary<string, string> { { "profile", "before" } };
        f.Queue.Save(data, null);
        data["profile"] = "after";
        require(f.Writes[0]["profile"] == "before", "Queued data must be snapshotted at submission.");
        f.Save("second");
        f.Failures[0]("explicit failure");
        require(f.Writes.Count == 2, "An explicit terminal failure must preserve the ordinary retry path.");
        f.Successes[1]();
        f.Save("third");
        require(f.Writes.Count == 3, "A new save must remain possible after an explicit failure.");
    }

    private static void CheckUnconfirmedServiceFailure(Action<bool, string> require)
    {
        var f = new Fixture();
        var results = new List<bool>();
        f.Save("uncertain first", (ok, _) => results.Add(ok));
        f.Save("must not follow", (ok, _) => results.Add(ok));
        f.Unconfirmed[0]("Connection lost after sending the request");
        require(results.Count == 2 && !results[0] && !results[1] && f.Writes.Count == 1,
            "An unconfirmed transport failure must abandon waiting callers without sending the next write.");
        f.Save("retry before acknowledgement", (ok, _) => results.Add(ok));
        require(results.Count == 3 && !results[2] && f.Writes.Count == 1,
            "Unknown service failure must quarantine the account immediately, without waiting for the timer.");
        f.Queue.ResetSession();
        f.Save("reset is not a cancellation");
        require(f.Writes.Count == 1, "Reset must not turn an unknown write outcome into permission to overwrite it.");
        f.Successes[0]();
        f.Save("confirmed retry");
        require(f.Writes.Count == 2 && results.Count == 3,
            "An actual late acknowledgement must release the barrier without changing earlier failed callbacks.");
    }
}
