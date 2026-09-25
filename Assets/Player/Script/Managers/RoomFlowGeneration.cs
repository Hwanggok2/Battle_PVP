using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BattlePvp.Networking
{
    public sealed class RoomFlowTicket
    {
        public long Generation { get; }
        public string AccountId { get; }
        public string RoomId { get; }
        public string MembershipKey { get; }
        internal RoomFlowTicket(long generation, string accountId, string roomId)
        { Generation = generation; AccountId = accountId; RoomId = roomId; MembershipKey = accountId + "/" + roomId; }
    }

    public sealed class RoomFlowGeneration
    {
        private long _generation;
        private readonly Dictionary<string, long> _preservedThrough = new Dictionary<string, long>(StringComparer.Ordinal);
        public RoomFlowTicket Current { get; private set; }

        public RoomFlowTicket Begin(string accountId, string roomId)
        {
            if (!RoomIdentity.TryNormalizePlayerId(accountId, out string normalized) || !RoomIdentity.IsValid(roomId))
                throw new ArgumentException("A valid account and room are required.");
            Current = new RoomFlowTicket(++_generation, normalized, roomId);
            return Current;
        }

        public void Invalidate() { _generation++; Current = null; }

        public bool IsCurrent(RoomFlowTicket ticket, string accountId) => ticket != null &&
            ReferenceEquals(Current, ticket) && RoomIdentity.TryNormalizePlayerId(accountId, out string normalized) &&
            ticket.AccountId == normalized;

        public void PreserveMembership(RoomFlowTicket ticket)
        {
            if (ticket == null) return;
            _preservedThrough.TryGetValue(ticket.MembershipKey, out long previous);
            _preservedThrough[ticket.MembershipKey] = Math.Max(previous, ticket.Generation);
        }

        public bool ShouldCompensate(RoomFlowTicket ticket, string currentAccountId) => ticket != null &&
            !IsMembershipPreserved(ticket) &&
            (Current == null || !IsCurrent(Current, currentAccountId) || Current.MembershipKey != ticket.MembershipKey);

        public bool IsMembershipPreserved(RoomFlowTicket ticket) => ticket != null &&
            _preservedThrough.TryGetValue(ticket.MembershipKey, out long preserved) && ticket.Generation <= preserved;
    }

    /// <summary>Serializes mutations of the same membership, while different rooms remain independent.</summary>
    public sealed class RoomOperationQueue
    {
        private readonly Dictionary<string, Task> _tails = new Dictionary<string, Task>(StringComparer.Ordinal);
        public int PendingKeys => _tails.Count;

        public Task<T> Enqueue<T>(string key, Func<Task<T>> operation)
        {
            _tails.TryGetValue(key, out Task previous);
            var completion = new TaskCompletionSource<bool>();
            _tails[key] = completion.Task;
            return Run();

            async Task<T> Run()
            {
                try
                {
                    if (previous != null) await previous;
                    return await operation();
                }
                finally
                {
                    if (_tails.TryGetValue(key, out Task current) && ReferenceEquals(current, completion.Task))
                        _tails.Remove(key);
                    completion.TrySetResult(true);
                }
            }
        }
    }
}
