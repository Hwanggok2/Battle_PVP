using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>Exchanges only connection challenges and account IDs; PlayFab credentials stay on each device.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomNetworkAuthenticator : NetworkAuthenticator
    {
        private const string ApproveFunction = "ApproveRoomConnection";
        private const string VerifyFunction = "VerifyRoomConnection";
        private enum ResponseCode : byte { Rejected, Accepted, DuplicateAccount }
        public struct ChallengeMessage : NetworkMessage { public string RoomId; public string Challenge; }
        public struct ProofMessage : NetworkMessage { public string PlayerId; public string Challenge; }
        public struct ResultMessage : NetworkMessage { public byte Code; }

        private sealed class Pending
        {
            public string RoomId;
            public string Challenge;
            public double Deadline;
            public bool Verifying;
        }

        private readonly Dictionary<NetworkConnectionToClient, Pending> _pending = new Dictionary<NetworkConnectionToClient, Pending>();
        private readonly RoomAuthenticationReservations<NetworkConnectionToClient> _accounts = new RoomAuthenticationReservations<NetworkConnectionToClient>();
        private readonly List<NetworkConnectionToClient> _expired = new List<NetworkConnectionToClient>(8);
        private int _serverEpoch;
        private int _clientEpoch;
        private bool _clientApproving;
        private bool _clientProofSent;
        private bool _clientWaiting;
        private double _clientDeadline;
        private string _clientRoomId;
        public bool PreserveRoomMembershipOnDisconnect { get; private set; }

        public override void OnStartServer()
        {
            _serverEpoch++;
            _pending.Clear();
            _accounts.Clear();
            NetworkServer.RegisterHandler<ProofMessage>(OnProof, false);
        }

        public override void OnStopServer()
        {
            _serverEpoch++;
            _pending.Clear();
            _accounts.Clear();
            NetworkServer.UnregisterHandler<ProofMessage>();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient connection)
        {
            string roomId = PlayFabBattleManager.Instance != null ? PlayFabBattleManager.Instance.CurrentRoomId : null;
            string localId = PlayFabSettings.staticPlayer.PlayFabId;
            if (!PlayFabClientAPI.IsClientLoggedIn() || !RoomAuthenticationRules.IsLocalOwner(roomId, localId))
            {
                Reject(connection, ResponseCode.Rejected);
                return;
            }

            // A local host has no remote peer to authenticate. Never use a client message or connectionId alone for this exception.
            if (connection is LocalConnectionToClient && ReferenceEquals(connection, NetworkServer.localConnection))
            {
                Accept(connection, new AuthenticatedRoomPlayer(localId, roomId));
                return;
            }

            // StartHost may still be loading its scene before the local connection is added. Reserve that eighth slot now.
            int remoteConnections = 0;
            foreach (NetworkConnectionToClient connected in NetworkServer.connections.Values)
                if (!(connected is LocalConnectionToClient)) remoteConnections++;
            int capacity = NetworkManager.singleton is BattleNetworkManager manager ? manager.RoomCapacity : BattleNetworkManager.PlayerCapacity;
            if (remoteConnections > capacity - 1)
            {
                Reject(connection, ResponseCode.Rejected);
                return;
            }

            var pending = new Pending
            {
                RoomId = roomId, Challenge = Guid.NewGuid().ToString("N"),
                Deadline = Time.realtimeSinceStartupAsDouble + RoomAuthenticationRules.TimeoutSeconds
            };
            _pending.Add(connection, pending);
            connection.Send(new ChallengeMessage { RoomId = roomId, Challenge = pending.Challenge });
        }

        private void OnProof(NetworkConnectionToClient connection, ProofMessage message)
        {
            if (!_pending.TryGetValue(connection, out Pending pending) || pending.Verifying) return;
            if (!IsCurrent(connection, pending) || message.Challenge != pending.Challenge ||
                !RoomAuthenticationRules.TryNormalizeAccountId(message.PlayerId, out string playerId))
            {
                Reject(connection, ResponseCode.Rejected);
                return;
            }
            pending.Verifying = true;
            VerifyProof(connection, pending, playerId, 1);
        }

        private void VerifyProof(NetworkConnectionToClient connection, Pending pending, string playerId, int attempt)
        {
            if (!IsCurrent(connection, pending)) return;
            int epoch = _serverEpoch;
            try
            {
                PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest
                {
                    FunctionName = VerifyFunction, GeneratePlayStreamEvent = false,
                    FunctionParameter = new Dictionary<string, object>
                    {
                        { "roomId", pending.RoomId }, { "challenge", pending.Challenge }, { "playerId", playerId }
                    }
                }, result =>
                {
                    if (epoch != _serverEpoch || !IsCurrent(connection, pending)) return;
                    if (!IsMatchingResult(result, pending.RoomId, pending.Challenge, playerId))
                    {
                        string code = RoomServiceErrors.Classify(result);
                        RecordAuthenticationFailure("verify", RoomServiceErrors.Diagnostic(result) + " attempt=" + attempt);
                        if (RoomServiceErrors.CanRetryProof(code, attempt))
                        {
                            StartCoroutine(RetryProof(connection, pending, playerId, attempt, epoch));
                            return;
                        }
                        Reject(connection, ResponseCode.Rejected);
                        return;
                    }
                    // Reserve only after PlayFab proves the claimed account. A forged ID cannot reserve another player's slot.
                    Accept(connection, new AuthenticatedRoomPlayer(playerId, pending.RoomId));
                }, _ =>
                {
                    if (epoch != _serverEpoch || !IsCurrent(connection, pending)) return;
                    RecordAuthenticationFailure("verify", "service_unavailable");
                    if (RoomServiceErrors.CanRetryProof("service_unavailable", attempt))
                        StartCoroutine(RetryProof(connection, pending, playerId, attempt, epoch));
                    else Reject(connection, ResponseCode.Rejected);
                });
            }
            catch (Exception)
            {
                RecordAuthenticationFailure("verify", "request_exception");
                Reject(connection, ResponseCode.Rejected);
            }
        }

        private IEnumerator RetryProof(NetworkConnectionToClient connection, Pending pending, string playerId, int attempt, int epoch)
        {
            // Re-read the same proof; never extend its challenge, reserve an account, or accept without verification.
            yield return new WaitForSecondsRealtime(.35f * attempt);
            if (epoch == _serverEpoch && IsCurrent(connection, pending)) VerifyProof(connection, pending, playerId, attempt + 1);
        }

        private static void RecordAuthenticationFailure(string stage, string code)
        {
            RoomConnectionDiagnostics.Record("authentication_" + stage + "_" + code);
            Debug.LogWarning("[RoomAuthentication] " + stage + ": " + code);
        }

        private bool IsCurrent(NetworkConnectionToClient connection, Pending pending) =>
            NetworkServer.active && NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) &&
            ReferenceEquals(current, connection) && _pending.TryGetValue(connection, out Pending actual) && ReferenceEquals(actual, pending) &&
            !connection.isAuthenticated && PlayFabBattleManager.Instance != null &&
            pending.RoomId == PlayFabBattleManager.Instance.CurrentRoomId &&
            RoomAuthenticationRules.IsBeforeDeadline(Time.realtimeSinceStartupAsDouble, pending.Deadline);

        private void Accept(NetworkConnectionToClient connection, AuthenticatedRoomPlayer identity)
        {
            var manager = NetworkManager.singleton as BattleNetworkManager;
            if (manager != null && manager.IsKicked(identity.PlayFabId))
            {
                Reject(connection, ResponseCode.Rejected);
                return;
            }
            if (!_accounts.TryReserve(identity.PlayFabId, connection))
            {
                Reject(connection, ResponseCode.DuplicateAccount);
                return;
            }
            if (manager != null && _accounts.Count > manager.RoomCapacity)
            {
                _accounts.Release(identity.PlayFabId, connection);
                Reject(connection, ResponseCode.Rejected);
                return;
            }
            _pending.Remove(connection);
            connection.authenticationData = identity;
            connection.Send(new ResultMessage { Code = (byte)ResponseCode.Accepted });
            ServerAccept(connection);
        }

        private void Reject(NetworkConnectionToClient connection, ResponseCode code)
        {
            _pending.Remove(connection);
            connection.Send(new ResultMessage { Code = (byte)code });
            // Give the rejection one network flush so the client can preserve a duplicate account's existing membership.
            StartCoroutine(DisconnectAfterResponse(connection, _serverEpoch));
        }

        private IEnumerator DisconnectAfterResponse(NetworkConnectionToClient connection, int epoch)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (epoch == _serverEpoch && NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) &&
                ReferenceEquals(current, connection)) ServerReject(connection);
        }

        public void OnServerDisconnected(NetworkConnectionToClient connection)
        {
            _pending.Remove(connection);
            if (connection.authenticationData is AuthenticatedRoomPlayer identity) _accounts.Release(identity.PlayFabId, connection);
        }

        public override void OnStartClient()
        {
            _clientEpoch++;
            _clientApproving = _clientProofSent = _clientWaiting = false;
            PreserveRoomMembershipOnDisconnect = false;
            NetworkClient.RegisterHandler<ChallengeMessage>(OnChallenge, false);
            NetworkClient.RegisterHandler<ResultMessage>(OnResult, false);
        }

        public override void OnStopClient()
        {
            _clientEpoch++;
            _clientWaiting = false;
            NetworkClient.UnregisterHandler<ChallengeMessage>();
            NetworkClient.UnregisterHandler<ResultMessage>();
        }

        public override void OnClientAuthenticate()
        {
            RoomConnectionDiagnostics.Stage("room_authentication_started");
            _clientWaiting = true;
            _clientRoomId = PlayFabBattleManager.Instance != null ? PlayFabBattleManager.Instance.CurrentRoomId : null;
            _clientDeadline = Time.realtimeSinceStartupAsDouble + RoomAuthenticationRules.TimeoutSeconds + 2d;
        }

        private void OnChallenge(ChallengeMessage message)
        {
            if (!_clientWaiting || _clientApproving || _clientProofSent) return;
            if (PlayFabBattleManager.Instance == null || message.RoomId != _clientRoomId ||
                message.RoomId != PlayFabBattleManager.Instance.CurrentRoomId ||
                !RoomIdentity.IsValid(message.RoomId) || !RoomAuthenticationRules.IsChallenge(message.Challenge) ||
                !PlayFabClientAPI.IsClientLoggedIn() ||
                !RoomAuthenticationRules.TryNormalizeAccountId(PlayFabSettings.staticPlayer.PlayFabId, out string playerId))
            {
                RejectClient();
                return;
            }
            _clientApproving = true;
            int epoch = _clientEpoch;
            NetworkConnectionToServer connection = NetworkClient.connection;
            try
            {
                PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest
                {
                    FunctionName = ApproveFunction, GeneratePlayStreamEvent = false,
                    FunctionParameter = new Dictionary<string, object> { { "roomId", message.RoomId }, { "challenge", message.Challenge } }
                }, result =>
                {
                    if (!IsCurrentClient(epoch, connection, message.RoomId)) return;
                    if (!IsMatchingResult(result, message.RoomId, message.Challenge, playerId))
                    {
                        string code = RoomServiceErrors.Classify(result);
                        RecordAuthenticationFailure("approve", RoomServiceErrors.Diagnostic(result));
                        PlayFabBattleManager.Instance?.NotifyRoomAuthenticationFailed(RoomServiceErrors.Message(code));
                        RejectClient(); return;
                    }
                    _clientProofSent = true;
                    NetworkClient.Send(new ProofMessage { PlayerId = playerId, Challenge = message.Challenge });
                }, _ =>
                {
                    if (!IsCurrentClient(epoch, connection, message.RoomId)) return;
                    RecordAuthenticationFailure("approve", "service_unavailable");
                    RejectClient();
                });
            }
            catch (Exception) { RejectClient(); }
        }

        private bool IsCurrentClient(int epoch, NetworkConnectionToServer connection, string roomId) =>
            epoch == _clientEpoch && _clientWaiting && ReferenceEquals(connection, NetworkClient.connection) &&
            PlayFabBattleManager.Instance != null && PlayFabBattleManager.Instance.CurrentRoomId == roomId &&
            RoomAuthenticationRules.IsBeforeDeadline(Time.realtimeSinceStartupAsDouble, _clientDeadline);

        private static bool IsMatchingResult(ExecuteCloudScriptResult result, string roomId, string challenge, string playerId)
        {
            if (result == null || result.Error != null || !(result.FunctionResult is IDictionary<string, object> data) ||
                !data.TryGetValue("ok", out object ok) || !(ok is bool accepted) || !accepted) return false;
            return data.TryGetValue("roomId", out object actualRoom) && data.TryGetValue("challenge", out object actualChallenge) &&
                data.TryGetValue("playerId", out object actualPlayer) &&
                RoomAuthenticationRules.MatchesProof(roomId, challenge, playerId, actualRoom as string, actualChallenge as string, actualPlayer as string);
        }

        private void OnResult(ResultMessage message)
        {
            if (!_clientWaiting) return;
            if (PlayFabBattleManager.Instance == null || _clientRoomId != PlayFabBattleManager.Instance.CurrentRoomId ||
                !RoomAuthenticationRules.IsBeforeDeadline(Time.realtimeSinceStartupAsDouble, _clientDeadline))
            {
                RejectClient();
                return;
            }
            if (message.Code == (byte)ResponseCode.Accepted &&
                (_clientProofSent || (NetworkClient.connection is LocalConnectionToServer && NetworkServer.active)))
            {
                _clientWaiting = false;
                RoomConnectionDiagnostics.Stage("room_authentication_accepted");
                ClientAccept();
                return;
            }
            PreserveRoomMembershipOnDisconnect = message.Code == (byte)ResponseCode.DuplicateAccount;
            RejectClient();
        }

        private void RejectClient()
        {
            RoomConnectionDiagnostics.Record(PreserveRoomMembershipOnDisconnect ? "authentication_duplicate_account" : "authentication_rejected");
            _clientWaiting = false;
            if (!PreserveRoomMembershipOnDisconnect) PlayFabBattleManager.Instance?.NotifyRoomAuthenticationFailed();
            if (NetworkClient.connection != null) ClientReject();
        }

        private void Update()
        {
            _expired.Clear();
            foreach (KeyValuePair<NetworkConnectionToClient, Pending> item in _pending)
                if (!IsCurrent(item.Key, item.Value)) _expired.Add(item.Key);
            foreach (NetworkConnectionToClient connection in _expired) Reject(connection, ResponseCode.Rejected);
            if (_clientWaiting && !RoomAuthenticationRules.IsBeforeDeadline(Time.realtimeSinceStartupAsDouble, _clientDeadline)) RejectClient();
        }
    }
}
