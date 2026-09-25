using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BattlePvp.Networking
{
    public sealed class RoomServiceUnconfirmedException : TimeoutException
    {
        public RoomServiceUnconfirmedException() : base("The room mutation has not been confirmed by the service.") { }
    }

    /// <summary>
    /// Used inside the membership queue. A timeout releases callers, but never authorizes a
    /// second mutation while the first might still execute. Only an actual response clears it.
    /// </summary>
    public sealed class RoomServiceResponseGate
    {
        private readonly Dictionary<string, Task> _unconfirmed = new Dictionary<string, Task>(StringComparer.Ordinal);
        public int UnconfirmedKeys => _unconfirmed.Count;
        public bool IsUnconfirmed(string key) => _unconfirmed.ContainsKey(key);

        // Heartbeat writes only the independent lease key. It cannot erase the CLOSED tombstone
        // or alter membership, so an unconfirmed refresh may be retried after the wait ends.
        public static Task<T> WaitForLeaseRefreshAsync<T>(Func<Task<T>> requestFactory, CancellationToken deadline)
        {
            deadline.ThrowIfCancellationRequested();
            return ServiceTaskDeadline.WaitAsync(requestFactory(), deadline);
        }

        public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> requestFactory,
            CancellationToken deadline, Action<T> onLateResponse = null)
        {
            if (IsUnconfirmed(key)) throw new RoomServiceUnconfirmedException();
            deadline.ThrowIfCancellationRequested();
            Task<T> request = requestFactory();
            try { return await ServiceTaskDeadline.WaitAsync(request, deadline); }
            catch (Exception)
            {
                // A transport failure also cannot prove that CloudScript did not run.
                _unconfirmed[key] = request;
                _ = ObserveLateResponseAsync(key, request, onLateResponse);
                throw new RoomServiceUnconfirmedException();
            }
        }

        private async Task ObserveLateResponseAsync<T>(string key, Task<T> request, Action<T> callback)
        {
            T result;
            try { result = await request; }
            catch (Exception) { return; } // No server acknowledgement: keep this key quarantined.
            if (!_unconfirmed.TryGetValue(key, out Task pending) || !ReferenceEquals(pending, request)) return;
            _unconfirmed.Remove(key);
            callback?.Invoke(result);
        }
    }

    /// <summary>Stops waiting without letting the detached task commit state or hide a later fault.</summary>
    public static class ServiceTaskDeadline
    {
        public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                _ = ObserveCompletionAsync(task);
                throw new OperationCanceledException(cancellation);
            }
            if (!task.IsCompleted)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellation.Register(() => cancelled.TrySetResult(true)))
                {
                    await Task.WhenAny(task, cancelled.Task);
                    if (!task.IsCompleted)
                    {
                        _ = ObserveCompletionAsync(task);
                        throw new OperationCanceledException(cancellation);
                    }
                }
            }
            if (cancellation.IsCancellationRequested)
            {
                _ = ObserveCompletionAsync(task);
                throw new OperationCanceledException(cancellation);
            }
            return await task;
        }

        public static async Task WaitAsync(Task task, CancellationToken cancellation)
        {
            await WaitAsync(ObserveCompletionAsync(task, propagateFailure: true), cancellation);
        }

        private static async Task<bool> ObserveCompletionAsync(Task task, bool propagateFailure = false)
        {
            try { await task; }
            catch (Exception) when (!propagateFailure) { }
            return true;
        }
    }
}
