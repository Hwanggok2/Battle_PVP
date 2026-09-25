using System;

namespace BattlePvp.Networking
{
    /// <summary>Owns local membership cleanup responsibility, independently of SDK callbacks and Unity objects.</summary>
    public sealed class RoomSessionState
    {
        public RoomFlowTicket Ticket { get; }
        public bool MembershipPossible { get; private set; }
        public bool CleanupQueued { get; private set; }
        public bool RelayQueued { get; private set; }

        public RoomSessionState(RoomFlowTicket ticket) => Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));

        public void MarkMembershipPossible() => MembershipPossible = true;

        public bool TryQueueRelay()
        {
            if (RelayQueued) return false;
            RelayQueued = true;
            return true;
        }

        public void CompleteRelayPreparation() => RelayQueued = false;

        public bool TryQueueCleanup()
        {
            if (!MembershipPossible || CleanupQueued) return false;
            CleanupQueued = true;
            return true;
        }

        // Evaluate at queue execution time: a newer same-room attempt may now own this membership.
        public bool AuthorizeCleanup(RoomFlowGeneration flows, string accountId, RoomSessionState current)
        {
            if (flows.ShouldCompensate(Ticket, accountId)) return true;
            if (!flows.IsMembershipPreserved(Ticket) && current != null && !ReferenceEquals(current, this) &&
                flows.IsCurrent(current.Ticket, accountId) && current.Ticket.MembershipKey == Ticket.MembershipKey)
            {
                if (MembershipPossible) current.MarkMembershipPossible();
                // Retain the old request's possibility: its late ACK may be the only event that can
                // retry cleanup after the renewed attempt was rejected by the unconfirmed-write gate.
                // Every retry is authorized again against the current flow and preservation cutoff.
            }
            return false;
        }

        public void ReleaseCleanupQueue() => CleanupQueued = false;

        public void ConfirmCleanup()
        {
            MembershipPossible = false;
            CleanupQueued = false;
        }
    }
}
