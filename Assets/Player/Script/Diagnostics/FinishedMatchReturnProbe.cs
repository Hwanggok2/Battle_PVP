#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Networking;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.EditorDiagnostics
{
    public sealed class FinishedMatchReturnProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        { public bool passed; public List<string> checks=new(); public List<string> failures=new(); }
        private readonly Report _report=new();
        private bool _hostFirst;
        public static void Begin(bool hostFirst = false)
        {
            if(!EditorApplication.isPlaying || NetworkServer.active || NetworkClient.active || !string.IsNullOrEmpty(PlayFabBattleManager.Instance?.CurrentRoomId))
                throw new InvalidOperationException("Run only in idle Play Mode outside a room.");
            var runner=new GameObject("Same room return probe").AddComponent<FinishedMatchReturnProbe>();
            runner._hostFirst=hostFirst;DontDestroyOnLoad(runner.gameObject);runner.StartCoroutine(runner.Run());
        }
        private IEnumerator Run()
        {
            var battle=SceneManager.CreateScene("Battle");SceneManager.SetActiveScene(battle);
            var old=new List<Scene>();for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene!=battle)old.Add(scene);}
            foreach(var scene in old)yield return SceneManager.UnloadSceneAsync(scene);
            var manager=NetworkManager.singleton as BattleNetworkManager;
            if(manager==null)manager=new GameObject("Probe manager").AddComponent<BattleNetworkManager>();
            var transport=manager.gameObject.AddComponent<FinishedMatchProbeTransport>();manager.transport=transport;Transport.active=transport;
            manager.authenticator=null;manager.onlineScene=string.Empty;manager.offlineScene="Assets/Scenes/Lobby.unity";manager.autoCreatePlayer=false;
            manager.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var auth=manager.GetComponent<RoomNetworkAuthenticator>();if(auth!=null)auth.enabled=false;
            manager.StartHost();yield return null;yield return null;
            var host=Instantiate(manager.playerPrefab);host.name="Host participant";
            NetworkServer.AddPlayerForConnection(NetworkServer.localConnection,host);
            var remote=Instantiate(manager.playerPrefab);remote.name="Remote participant";
            var peer=new NetworkConnectionToClient(701){isAuthenticated=true,isReady=true};NetworkServer.AddConnection(peer);NetworkServer.AddPlayerForConnection(peer,remote);
            var state=new GameObject("Completed match").AddComponent<BattleStateMachine>();state.CurrentState=BattleState.MatchEnded;
            var originalLocal=NetworkClient.localPlayer;var originalRemote=peer.identity;
            yield return null;
            if (_hostFirst) manager.ReturnToLobbyAfterMatch();
            else manager.ServerRequestWaitingReturn(peer);
            double deadline=Time.realtimeSinceStartupAsDouble+30;
            while(!manager.HasSplitResultRoom&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Check(manager.HasSplitResultRoom,"Existing waiting room is loaded alongside the completed battle.");
            if (!_hostFirst)
            {
                Check(SceneManager.GetActiveScene()==battle,"Remote return does not change the host's displayed battle scene.");
                Check(BattleStateMachine.Instance==state,"Loading a waiting room does not replace the battle result state.");
                manager.ServerEnterWaiting(peer);
                Check(peer.identity==originalRemote&&remote.scene.name==BattleNetworkManager.WaitingScene,"Only the requesting remote avatar moves into the waiting room with the same identity.");
                Check(host.scene==battle,"The host remains in the battle until pressing J.");
                manager.ReturnToLobbyAfterMatch();
            }
            Check(!manager.CanStartNextRound,"Another round cannot start while a participant is still viewing results.");
            deadline=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<deadline && (SceneManager.GetActiveScene().name!=BattleNetworkManager.WaitingScene ||
                !((HashSet<int>)typeof(BattleNetworkManager).GetField("_presentedReturns",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager)).Contains(NetworkServer.localConnection.connectionId)))yield return null;
            if (_hostFirst)
            {
                Check(remote.scene==battle&&peer.identity==originalRemote,"Host-first return leaves the remote participant in the completed battle.");
                manager.ServerRequestWaitingReturn(peer);yield return null;manager.ServerEnterWaiting(peer);
            }
            Check(NetworkClient.active&&NetworkServer.active&&manager.mode==NetworkManagerMode.Host,"The host returns without stopping its local client or server.");
            Check(NetworkClient.localPlayer==originalLocal,"The same host player and authority survive the return.");
            Check(NetworkServer.connections.ContainsKey(701)&&peer.identity==originalRemote,"Room participants stay connected.");
            Check(SceneManager.GetActiveScene().name==BattleNetworkManager.WaitingScene,"The host now sees the existing room's waiting scene.");
            var movement=host.GetComponent<PlayerManager>();
            Check(!movement.IsMatchEndLocked,"Waiting room movement is unlocked for the returned player.");
            var camera=FindFirstObjectByType<FollowCamera>();
            Check(camera!=null&&camera.Target==host.transform,"Waiting camera follows the returned player.");
            Check(Vector3.Distance(host.transform.position,remote.transform.position)<30,"Returned participants share the same waiting room space.");
            ((HashSet<int>)typeof(BattleNetworkManager).GetField("_presentedReturns",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager)).Add(701);
            typeof(BattleNetworkManager).GetMethod("TryFinishWaitingReturn",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);
            deadline=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<deadline&&(battle.IsValid()&&battle.isLoaded || !manager.CanStartNextRound))yield return null;
            Check(NetworkServer.active&&NetworkClient.active&&NetworkServer.connections.ContainsKey(701),"All returns preserve the room and both connections.");
            Check(!battle.IsValid()||!battle.isLoaded,"The completed battlefield is released after everyone returns.");
            Check(manager.CanStartNextRound&&SceneManager.GetActiveScene().name==BattleNetworkManager.WaitingScene,"The normal waiting room is ready for the next match.");
            _report.passed=_report.failures.Count==0;Directory.CreateDirectory("Reports/MatchReturn");
            File.WriteAllText(_hostFirst?"Reports/MatchReturn/host-first-waiting-probe.json":"Reports/MatchReturn/same-room-return-probe.json",JsonUtility.ToJson(_report,true));
            Debug.Log("[SameRoomReturnProbe] "+(_report.passed?"PASS":"FAIL")+" checks="+_report.checks.Count+" failures="+_report.failures.Count);
            manager.StopHost();Destroy(gameObject);
        }
        private void Check(bool passed,string label){if(passed)_report.checks.Add(label);else _report.failures.Add(label);}
    }
}
#endif
