namespace BattlePvp.Networking
{
    // Membership writes outlive a Unity manager. Its replacement must share both
    // serialization and unconfirmed-response quarantine with the old callbacks.
    public sealed class RoomServiceLifetime
    {
        public readonly RoomFlowGeneration Flows = new RoomFlowGeneration();
        public readonly RoomOperationQueue Mutations = new RoomOperationQueue();
        public readonly RoomServiceResponseGate Responses = new RoomServiceResponseGate();
        public RoomSessionState Current { get; private set; }

        public RoomSessionState Begin(string account, string room)
        {
            Current = new RoomSessionState(Flows.Begin(account, room));
            return Current;
        }

        public void End(RoomSessionState session)
        {
            if (session == null || !ReferenceEquals(Current, session)) return;
            Flows.Invalidate();
            Current = null;
        }

        public bool AuthorizeCleanup(RoomSessionState session, string account) =>
            session.AuthorizeCleanup(Flows, account, Current);
    }
}
