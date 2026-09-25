using System;
using System.Collections.Generic;

namespace BattlePvp.Networking
{
    /// <summary>Single-threaded profile request lifecycle, independent of Unity and the service SDK.</summary>
    public sealed class ProfileRequestQueue<TLoad>
    {
        public const double TimeoutSeconds = 15d;
        private const string SessionChanged = "Player session changed.";
        private const string SaveUnconfirmed = "The previous profile save has not been confirmed. Wait for its response before saving again.";
        private readonly Action<Action<TLoad>, Action<string>> _read;
        private readonly Action<Dictionary<string, string>, Action, Action<string>, Action<string>> _write;
        private readonly Func<string, TLoad> _loadFailure;
        private readonly Func<double> _clock;
        private readonly Func<string> _accountKey;
        private readonly Action<Exception> _callbackError;
        private readonly List<Action<TLoad>> _loads = new List<Action<TLoad>>();
        private readonly Queue<PendingSave> _saves = new Queue<PendingSave>();
        private readonly ProfileWriteCoordinator _writes;
        private string _account;
        private long _session;
        private long _loadRequest;
        private bool _loading;
        private double _loadDeadline;
        private PendingSave _activeSave;

        private sealed class PendingSave
        {
            public string Account;
            public long Session;
            public Dictionary<string, string> Data;
            public Action<bool, string> Completed;
            public double Deadline;
            public bool CallbackCompleted;
            public bool Terminal;
            public ProfileWriteLease WriteLease;
        }

        public ProfileRequestQueue(Action<Action<TLoad>, Action<string>> read,
            Action<Dictionary<string, string>, Action, Action<string>> write,
            Func<string, TLoad> loadFailure, Func<double> clock, Func<string> accountKey,
            Action<Exception> callbackError = null,
            Action<Dictionary<string, string>, Action, Action<string>, Action<string>> writeWithUnconfirmed = null,
            ProfileWriteCoordinator writeCoordinator = null)
        {
            _read = read;
            _write = writeWithUnconfirmed ?? ((data, success, failure, _) => write(data, success, failure));
            _loadFailure = loadFailure;
            _clock = clock;
            _accountKey = accountKey;
            _callbackError = callbackError;
            _writes = writeCoordinator ?? new ProfileWriteCoordinator();
            // MonoBehaviour field initializers may run on Unity's loading thread.
            // Only wire dependencies here; resolve the account from a runtime operation.
        }

        public void Load(Action<TLoad> completed)
        {
            EnsureAccount();
            if (completed != null) _loads.Add(completed);
            if (_loading) return;
            _loading = true;
            long session = _session;
            long request = ++_loadRequest;
            _loadDeadline = _clock() + TimeoutSeconds;
            try
            {
                _read(result => CompleteLoad(session, request, result),
                    error => CompleteLoad(session, request, _loadFailure(error)));
            }
            catch (Exception error) { CompleteLoad(session, request, _loadFailure(error.Message)); }
        }

        public void Save(Dictionary<string, string> data, Action<bool, string> completed)
        {
            EnsureAccount();
            var save = new PendingSave
            {
                Account = _account, Session = _session,
                Data = new Dictionary<string, string>(data), Completed = completed
            };
            if (_writes.IsBlocked(_account, _activeSave?.WriteLease))
            {
                CompleteSave(save, false, SaveUnconfirmed);
                return;
            }
            _saves.Enqueue(save);
            StartNextSave();
        }

        public void Tick()
        {
            EnsureAccount();
            double now = _clock();
            if (_loading && now >= _loadDeadline)
                CompleteLoad(_session, _loadRequest, _loadFailure("Player profile load timed out."));
            ExpireSave(now);
        }

        private void ExpireSave(double now)
        {
            PendingSave expired = _activeSave;
            if (expired == null || now < expired.Deadline) return;
            FailUnconfirmedWrite(expired);
        }

        private void FailUnconfirmedWrite(PendingSave current)
        {
            EnsureAccount();
            if (current.Terminal || !ReferenceEquals(_activeSave, current)) return;
            _activeSave = null;
            PendingSave[] abandoned = _saves.ToArray();
            _saves.Clear();
            // The shared coordinator keeps the actual write lease even if this queue is replaced.
            CompleteSave(current, false, SaveUnconfirmed);
            foreach (PendingSave save in abandoned) CompleteSave(save, false, SaveUnconfirmed);
        }

        public void ResetSession()
        {
            _account = _accountKey() ?? string.Empty;
            _session++;
            _loadRequest++;
            _loading = false;
            Action<TLoad>[] loads = _loads.ToArray();
            _loads.Clear();
            PendingSave active = _activeSave;
            _activeSave = null;
            PendingSave[] saves = _saves.ToArray();
            _saves.Clear();
            TLoad failure = _loadFailure(SessionChanged);
            foreach (Action<TLoad> callback in loads) InvokeSafely(() => callback(failure));
            if (active != null) CompleteSave(active, false, SessionChanged);
            foreach (PendingSave save in saves) CompleteSave(save, false, SessionChanged);
        }

        private void EnsureAccount()
        {
            string account = _accountKey() ?? string.Empty;
            if (_account == null)
            {
                _account = account;
                return;
            }
            if (!string.Equals(_account, account, StringComparison.OrdinalIgnoreCase)) ResetSession();
        }

        private void CompleteLoad(long session, long request, TLoad result)
        {
            EnsureAccount();
            if (!_loading || session != _session || request != _loadRequest) return;
            if (_clock() >= _loadDeadline) result = _loadFailure("Player profile load timed out.");
            _loading = false;
            Action<TLoad>[] callbacks = _loads.ToArray();
            _loads.Clear();
            foreach (Action<TLoad> callback in callbacks)
            {
                EnsureAccount();
                TLoad value = session == _session ? result : _loadFailure(SessionChanged);
                InvokeSafely(() => callback(value));
            }
        }

        private void StartNextSave()
        {
            while (true)
            {
                EnsureAccount();
                if (_activeSave != null || _saves.Count == 0) return;
                PendingSave current = _saves.Dequeue();
                if (current.Session != _session || !string.Equals(current.Account, _account, StringComparison.OrdinalIgnoreCase) ||
                    !_writes.TryAcquire(current.Account, out current.WriteLease))
                {
                    // A replacement may acquire the lease inside the previous completion callback.
                    // Drain abandoned local work without recursion proportional to queue length.
                    CompleteSave(current, false, SaveUnconfirmed);
                    continue;
                }
                _activeSave = current;
                current.Deadline = _clock() + TimeoutSeconds;
                try
                {
                    _write(current.Data, () => FinishWrite(current, true, null),
                        error => FinishWrite(current, false, error), _ => FailUnconfirmedWrite(current));
                }
                catch (Exception) { FailUnconfirmedWrite(current); }
                return;
            }
        }

        private void FinishWrite(PendingSave current, bool success, string error)
        {
            EnsureAccount();
            ExpireSave(_clock());
            if (current.Terminal) return;
            current.Terminal = true;
            _writes.Confirm(current.WriteLease);
            if (ReferenceEquals(_activeSave, current)) _activeSave = null;
            CompleteSave(current, success, error);
            StartNextSave();
        }

        private void CompleteSave(PendingSave save, bool success, string error)
        {
            if (save.CallbackCompleted) return;
            save.CallbackCompleted = true;
            InvokeSafely(() => save.Completed?.Invoke(success, error));
            save.Completed = null;
        }

        private void InvokeSafely(Action callback)
        {
            try { callback(); }
            catch (Exception error)
            {
                try { _callbackError?.Invoke(error); }
                catch (Exception) { } // Error reporting must not abandon the remaining completions.
            }
        }
    }
}
