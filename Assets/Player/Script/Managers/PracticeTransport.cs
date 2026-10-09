using System;
using Mirror;

namespace BattlePvp.Networking
{
    /// <summary>Mirror's in-process host connections need no socket or Relay allocation.</summary>
    public sealed class PracticeTransport : Transport
    {
        private bool _active;
        public override bool Available() => true;
        public override bool ClientConnected() => false;
        public override void ClientConnect(string address) => OnClientError?.Invoke(TransportError.InvalidReceive, "Practice only accepts the local host.");
        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ClientDisconnect() { }
        public override Uri ServerUri() => new Uri("practice://localhost");
        public override bool ServerActive() => _active;
        public override void ServerStart() => _active = true;
        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ServerDisconnect(int connectionId) { }
        public override string ServerGetClientAddress(int connectionId) => "local";
        public override void ServerStop() => _active = false;
        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 65535;
        public override void Shutdown() => _active = false;
    }
}
