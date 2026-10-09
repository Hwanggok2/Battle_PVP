#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.EditorDiagnostics
{
    public sealed class BattleWithdrawalProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        { public bool passed; public List<string> checks = new(); public string error; }
        private readonly Report _report = new();
        private bool _hostLeaves;
        private BattleNetworkManager _manager;
        public static void Begin(bool hostLeaves)
        {
            if (!EditorApplication.isPlaying || NetworkServer.active || NetworkClient.active ||
                !string.IsNullOrEmpty(PlayFabBattleManager.Instance?.CurrentRoomId))
                throw new InvalidOperationException("Run only in idle Play Mode outside a room.");
            var runner = new GameObject("Battle withdrawal probe").AddComponent<BattleWithdrawalProbe>();
            runner._hostLeaves = hostLeaves; DontDestroyOnLoad(runner.gameObject);
            runner.StartCoroutine(runner.Capture());
        }
        private IEnumerator Capture()
        {
            var routine = Run();
            while (true)
            {
                bool next;
                try { next = routine.MoveNext(); }
                catch (Exception error) { _report.error = error.ToString(); break; }
                if (!next) { _report.passed = true; break; }
                yield return routine.Current;
            }
            Directory.CreateDirectory("Reports/MatchReturn");
            File.WriteAllText("Reports/MatchReturn/withdrawal-" + (_hostLeaves ? "host-first" : "host-winner") + ".json", JsonUtility.ToJson(_report, true));
            Debug.Log("[BattleWithdrawalProbe] " + (_report.passed ? "PASS" : _report.error));
            if (_manager != null && NetworkServer.active) _manager.StopHost();
            Destroy(gameObject);
        }
        private IEnumerator Run()
        {
            var battle = SceneManager.CreateScene("Battle"); SceneManager.SetActiveScene(battle);
            var old = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i) != battle) old.Add(SceneManager.GetSceneAt(i));
            foreach (var scene in old) yield return SceneManager.UnloadSceneAsync(scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.localScale = new Vector3(80, 1, 80); floor.transform.position = new Vector3(0, -1, 0);
            _manager = NetworkManager.singleton as BattleNetworkManager;
            if (_manager == null) _manager = new GameObject("Probe manager", typeof(FinishedMatchProbeTransport)).AddComponent<BattleNetworkManager>();
            var transport = _manager.gameObject.AddComponent<FinishedMatchProbeTransport>(); _manager.transport = transport; Transport.active = transport;
            _manager.authenticator = null; _manager.onlineScene = string.Empty; _manager.offlineScene = "Assets/Scenes/Lobby.unity"; _manager.autoCreatePlayer = false;
            _manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            _manager.GetComponent<RoomNetworkAuthenticator>().enabled = false;
            _manager.StartHost(); yield return null; yield return null;
            RoomIdentity.TryCreate("a", Guid.NewGuid(), out string room);
            var connections = new[] { NetworkServer.localConnection, new NetworkConnectionToClient(701), new NetworkConnectionToClient(702) };
            for (int i = 0; i < connections.Length; i++)
            {
                var connection = connections[i]; connection.isAuthenticated = connection.isReady = true;
                connection.authenticationData = new AuthenticatedRoomPlayer((i + 1).ToString("x"), room);
                if (i > 0) NetworkServer.AddConnection(connection);
                var player = Instantiate(_manager.playerPrefab, new Vector3(i * 4, 1, 0), Quaternion.identity);
                NetworkServer.AddPlayerForConnection(connection, player);
                player.GetComponent<BattlePvp.Stats.StatManager>().TryApplyServerPreset(BattleNetworkManager.DefaultPracticeStats());
            }
            // Spawn the real network state without starting its timed matchmaking bootstrap.
            var setup = SceneManager.CreateScene("Probe setup"); SceneManager.SetActiveScene(setup);
            var state = new GameObject("Active match").AddComponent<BattleStateMachine>();
            typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state.GetComponent<NetworkIdentity>(), null);
            SceneManager.MoveGameObjectToScene(state.gameObject, battle);
            NetworkServer.Spawn(state.gameObject);
            SceneManager.SetActiveScene(battle); yield return SceneManager.UnloadSceneAsync(setup);
            var ledger = new MatchLedger(); ledger.Begin("withdrawal-probe", room);
            Set(state, "_matchLedger", ledger); Set(state, "_matchRoomId", room);
            foreach (var connection in connections) connection.identity.GetComponent<ScoreSystem>().AttachToMatch(ledger, room);
            state.ResultDelaySeconds = .1f; state.CurrentState = BattleState.InBattle;
            var local = connections[0].identity; var winner = _hostLeaves ? connections[2].identity : local;
            uint departedLeader = (_hostLeaves ? local : connections[1].identity).netId;
            for (uint i = 1; i <= 4; i++) ledger.RecordKill(departedLeader, winner.netId, i, true);
            yield return null;

            if (_hostLeaves)
            {
                local.GetComponent<HealthSystem>().ApplyDamage(100000, DamageSource.Fixed, local.transform.position);
                Check(_manager.RequestLeaveBattle(), "Host can request a personal return during combat.");
                double deadline = Time.realtimeSinceStartupAsDouble + 25;
                while (SceneManager.GetActiveScene().name != BattleNetworkManager.WaitingScene && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Check(SceneManager.GetActiveScene().name == BattleNetworkManager.WaitingScene, "Host sees the same room waiting area.");
                Check(local.gameObject.scene.name == BattleNetworkManager.WaitingScene, "Only the host avatar moved.");
                Check(!local.GetComponent<HealthSystem>().IsDead, "A dead participant is revived on entering the waiting room.");
                Check(state.CurrentState == BattleState.InBattle && connections[1].identity.gameObject.scene == battle && winner.gameObject.scene == battle,
                    "Two remote participants remain in combat after the host returns.");
            }
            else
            {
                _manager.ServerRequestWaitingReturn(connections[1]);
                double deadline = Time.realtimeSinceStartupAsDouble + 25;
                while (!_manager.HasSplitResultRoom && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                _manager.ServerEnterWaiting(connections[1]);
                Check(state.CurrentState == BattleState.InBattle, "Leaving three participants down to two does not end the match.");
            }
            var lastLeaver = _hostLeaves ? connections[1] : connections[2];
            _manager.ServerRequestWaitingReturn(lastLeaver);
            yield return null;
            // The sole participant is allowed to be waiting for respawn when someone exits.
            winner.GetComponent<HealthSystem>().ApplyDamage(100000, DamageSource.Fixed, winner.transform.position);
            _manager.ServerEnterWaiting(lastLeaver);
            Check(state.CurrentState == BattleState.MatchEnded, "The final departure immediately ends the match.");
            Check(state.LastCompletedMatch.Participants.Single(p => p.Rank == 1).LastNetId == winner.netId, "The remaining participant wins regardless of the departed leader's points.");
            Check(!winner.GetComponent<HealthSystem>().IsDead, "The winner is revived for the result presentation.");
            Check(NetworkServer.active && NetworkClient.active && NetworkServer.connections.Count == 3, "Returning does not stop the host or disconnect room participants.");
            yield return new WaitForSecondsRealtime(.5f);
            if (_hostLeaves)
            {
                Check(!state.IsResultPanelVisible && !local.GetComponent<PlayerManager>().IsMatchEndLocked, "Later match results do not capture the returned host's screen or movement.");
                Check(SceneManager.GetActiveScene().name == BattleNetworkManager.WaitingScene, "Host remains in the waiting room after the remote winner is decided.");
            }
            else
            {
                Check(state.IsResultPanelVisible, "The remaining local player sees the real result panel.");
                Check(local.GetComponent<PlayerManager>().CanMoveAfterMatch, "The winning player retains movement.");
            }
            Check(!_manager.CanStartNextRound, "A new match cannot start until remaining participants return.");
        }
        private void Check(bool passed, string label)
        { if (!passed) throw new InvalidOperationException(label); _report.checks.Add(label); }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
#endif
