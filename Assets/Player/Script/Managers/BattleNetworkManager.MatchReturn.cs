using System.Collections;
using System.Collections.Generic;
using BattlePvp.Logic;
using BattlePvp.UI;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Networking
{
    public partial class BattleNetworkManager
    {
        public const string WaitingScene = "Battle_waiting";
        public static readonly Vector3 ReturnWaitingOffset = new Vector3(0f, -5000f, 0f);
        public struct RequestWaitingReturn : NetworkMessage { }
        public struct LoadReturnWaiting : NetworkMessage { }
        public struct ReturnWaitingLoaded : NetworkMessage { }
        public struct EnterReturnWaiting : NetworkMessage { }
        public struct ReturnWaitingPresented : NetworkMessage { }
        private readonly HashSet<int> _pendingReturns = new();
        private readonly HashSet<int> _movedReturns = new();
        private readonly HashSet<int> _presentedReturns = new();
        private bool _returningFromMatch, _loadingReturnWaiting, _normalizingWaiting;
        private Scene _returnWaitingScene;
        public bool IsPreparingReturnWaiting { get; private set; }
        public bool HasSplitResultRoom => _returnWaitingScene.IsValid() && _returnWaitingScene.isLoaded;
        public bool CanStartNextRound => !HasSplitResultRoom && !_normalizingWaiting;

        private void RegisterReturnServerHandlers()
        {
            NetworkServer.RegisterHandler<RequestWaitingReturn>((connection, _) => ServerRequestWaitingReturn(connection));
            NetworkServer.RegisterHandler<ReturnWaitingLoaded>((connection, _) => ServerEnterWaiting(connection));
            NetworkServer.RegisterHandler<ReturnWaitingPresented>((connection, _) =>
            {
                if (_movedReturns.Contains(connection.connectionId)) _presentedReturns.Add(connection.connectionId);
                TryFinishWaitingReturn();
            });
        }
        private void RegisterReturnClientHandlers()
        {
            NetworkClient.RegisterHandler<LoadReturnWaiting>(_ => StartCoroutine(LoadWaitingForLocalPlayer()));
            NetworkClient.RegisterHandler<EnterReturnWaiting>(_ => StartCoroutine(PresentWaitingForLocalPlayer()));
        }
        public void ReturnToLobbyAfterMatch()
        {
            if (_returningFromMatch || !NetworkClient.isConnected || BattleStateMachine.Instance == null ||
                BattleStateMachine.Instance.CurrentState != BattleState.MatchEnded) return;
            _returningFromMatch = true;
            NetworkClient.Send(new RequestWaitingReturn());
        }
        internal bool CanReturnToWaiting(NetworkConnectionToClient connection) => NetworkServer.active &&
            connection != null && connection.isAuthenticated && connection.identity != null &&
            BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState == BattleState.MatchEnded &&
            connection.identity.gameObject.scene == BattleStateMachine.Instance.gameObject.scene &&
            !_pendingReturns.Contains(connection.connectionId);
        internal void ServerRequestWaitingReturn(NetworkConnectionToClient connection)
        {
            if (!CanReturnToWaiting(connection)) return;
            _pendingReturns.Add(connection.connectionId);
            StartCoroutine(PrepareWaitingForConnection(connection));
        }
        private IEnumerator PrepareWaitingForConnection(NetworkConnectionToClient connection)
        {
            if (!HasSplitResultRoom && !_loadingReturnWaiting)
            {
                _loadingReturnWaiting = IsPreparingReturnWaiting = true;
                yield return SceneManager.LoadSceneAsync(WaitingScene, LoadSceneMode.Additive);
                _returnWaitingScene = SceneManager.GetSceneByName(WaitingScene);
                IsPreparingReturnWaiting = _loadingReturnWaiting = false;
            }
            while (_loadingReturnWaiting) yield return null;
            if (HasSplitResultRoom && IsCurrentConnection(connection)) connection.Send(new LoadReturnWaiting());
        }
        private static bool IsCurrentConnection(NetworkConnectionToClient connection) => connection != null &&
            NetworkServer.connections.TryGetValue(connection.connectionId, out var current) && ReferenceEquals(current, connection);
        private IEnumerator LoadWaitingForLocalPlayer()
        {
            if (!NetworkClient.isConnected) yield break;
            var scene = SceneManager.GetSceneByName(WaitingScene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                IsPreparingReturnWaiting = true;
                yield return SceneManager.LoadSceneAsync(WaitingScene, LoadSceneMode.Additive);
                IsPreparingReturnWaiting = false;
            }
            NetworkClient.PrepareToSpawnSceneObjects();
            _returnWaitingScene = SceneManager.GetSceneByName(WaitingScene);
            if (NetworkClient.isConnected) NetworkClient.Send(new ReturnWaitingLoaded());
        }
        internal void ServerEnterWaiting(NetworkConnectionToClient connection)
        {
            if (!IsCurrentConnection(connection) || !_pendingReturns.Contains(connection.connectionId) ||
                _movedReturns.Contains(connection.connectionId) || !HasSplitResultRoom || connection.identity == null) return;
            Transform spawn = null;
            foreach (var candidate in startPositions)
                if (candidate != null && candidate.gameObject.scene == _returnWaitingScene) { spawn = candidate; break; }
            if (spawn == null) { Debug.LogError("[MatchReturn] Waiting room has no spawn."); return; }
            var player = connection.identity;
            SceneManager.MoveGameObjectToScene(player.gameObject, _returnWaitingScene);
            var movement = player.GetComponent<PlayerManager>();
            movement?.LeaveMatchEndMode();
            movement?.ServerTeleport(spawn.position + Vector3.right * (_movedReturns.Count % 4) * 1.5f, spawn.rotation);
            var health = player.GetComponent<BattlePvp.Combat.HealthSystem>();
            if (health != null) { health.isInvincible = false; health.RefillHealth(); }
            _movedReturns.Add(connection.connectionId);
            connection.Send(new EnterReturnWaiting());
            foreach (var identity in NetworkServer.spawned.Values) NetworkServer.RebuildObservers(identity, false);
        }
        private IEnumerator PresentWaitingForLocalPlayer()
        {
            var scene = SceneManager.GetSceneByName(WaitingScene);
            if (!scene.IsValid() || !scene.isLoaded || NetworkClient.localPlayer == null) yield break;
            var battle = SceneManager.GetActiveScene();
            BattleStateMachine.Instance?.DismissLocalResult();
            HideBattlePresentation(battle);
            yield return null;
            if (NetworkClient.localPlayer == null) yield break;
            if (!NetworkServer.active) SceneManager.MoveGameObjectToScene(NetworkClient.localPlayer.gameObject, scene);
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects()) root.GetComponent<ReturnWaitingSceneGate>()?.Present();
            yield return null;
            var player = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerManager>() : null;
            if (player == null) yield break;
            player.LeaveMatchEndMode();
            player.BindLocalCamera();
            PlayerHUD.BindToPlayer(player.GetComponent<BattlePvp.Stats.StatManager>());
            GameInputController.Instance?.ResetToPlayMode();
            NetworkClient.Send(new ReturnWaitingPresented());
        }
        private static void HideBattlePresentation(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.GetComponentInParent<NetworkIdentity>() == null) renderer.forceRenderingOff = true;
                foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
                foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var volume in root.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true)) volume.enabled = false;
                foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) audio.Stop();
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                foreach (var events in root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true)) events.gameObject.SetActive(false);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour is NetworkBehaviour ||
                        (!(behaviour is GameInputController) && behaviour.GetComponentInParent<NetworkIdentity>() != null)) continue;
                    if (behaviour is GameInputController || behaviour.GetType().Namespace == "BattlePvp.UI") Destroy(behaviour);
                }
            }
        }
        private void TryFinishWaitingReturn()
        {
            if (!NetworkServer.active || !HasSplitResultRoom || _normalizingWaiting || NetworkServer.connections.Count == 0) return;
            foreach (var connection in NetworkServer.connections.Values)
                if (connection != null && !_presentedReturns.Contains(connection.connectionId)) return;
            _normalizingWaiting = true;
            StartCoroutine(FinishWaitingReturn());
        }
        private IEnumerator FinishWaitingReturn()
        {
            yield return null;
            // Every participant has accepted their result. Keep the same room/host,
            // then restore the ordinary waiting scene for the next match.
            if (NetworkServer.active) ServerChangeScene(WaitingScene);
        }
        private void ResetMatchReturn()
        {
            _pendingReturns.Clear(); _movedReturns.Clear(); _presentedReturns.Clear();
            _returningFromMatch = _loadingReturnWaiting = _normalizingWaiting = IsPreparingReturnWaiting = false;
            _returnWaitingScene = default;
        }
    }
}
