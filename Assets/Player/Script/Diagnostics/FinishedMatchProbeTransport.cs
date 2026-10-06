#if UNITY_EDITOR
using System;
using Mirror;

namespace BattlePvp.EditorDiagnostics
{
    public sealed class FinishedMatchProbeTransport : Transport
    {
        private bool _server;
        public override bool Available() => true;
        public override bool ClientConnected() => false;
        public override void ClientConnect(string address) => throw new NotSupportedException();
        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ClientDisconnect() { }
        public override Uri ServerUri() => new Uri("memory://finished-match-probe");
        public override bool ServerActive() => _server;
        public override void ServerStart() => _server = true;
        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ServerDisconnect(int connectionId) => OnServerDisconnected?.Invoke(connectionId);
        public override string ServerGetClientAddress(int connectionId) => "memory";
        public override void ServerStop() => _server = false;
        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 65535;
        public override void Shutdown() => _server = false;
    }
}
#endif
