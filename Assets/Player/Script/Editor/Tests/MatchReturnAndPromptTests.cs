using System.Collections;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class MatchReturnAndPromptTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene _scene;
        private NetworkManager _previousManager;
        private NetworkIdentity _previousPlayer;
        private object _inputManager;
        private PropertyInfo _runPlayerUpdates;
        private bool _previousPlayerUpdates;
        private readonly System.Collections.Generic.Dictionary<FieldInfo, object> _inputState = new();

        [SetUp] public void Setup()
        {
            foreach (string name in new[] { "_paused", "_webPointerMissing", "_textInputActive", "_textInputConsumedFrame", "<CurrentMode>k__BackingField" })
            {
                var field=typeof(GameInputController).GetField(name,BindingFlags.Static|BindingFlags.NonPublic);
                _inputState[field]=field.GetValue(null);
            }
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _scene.name = "Battle";
            _previousManager = NetworkManager.singleton;
            _previousPlayer = NetworkClient.localPlayer;
            typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, null);
            // InputSystem's supported internal editor-preview mode uses the same
            // player buffers/edge counters as Play Mode and ignores Game View focus.
            _inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _runPlayerUpdates = _inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode");
            _previousPlayerUpdates = (bool)_runPlayerUpdates.GetValue(_inputManager);
            _runPlayerUpdates.SetValue(_inputManager, true);
        }

        [TearDown] public void Cleanup()
        {
            typeof(NetworkClient).GetProperty(nameof(NetworkClient.localPlayer)).SetValue(null, _previousPlayer);
            foreach (GameObject root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, _previousManager);
            _runPlayerUpdates?.SetValue(_inputManager, _previousPlayerUpdates);
            // Result/cursor tests must not leave later combat tests globally paused.
            foreach (var saved in _inputState) saved.Key.SetValue(null,saved.Value);
            _inputState.Clear();
        }

        [TestCase(Key.J, true)]
        [TestCase(Key.Enter, false)]
        [TestCase(Key.Space, false)]
        public void OnlyJRequestsReturnFromAVisibleResult(Key key, bool requestsReturn)
        {
            var state = EditorTestLifecycle.AddNetwork<BattleStateMachine>(new GameObject("Finished match"));
            EditorTestLifecycle.Invoke(state, "Awake");
            state.CurrentState = BattleState.MatchEnded;
            Set(state, "_resultPanelVisible", true);
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake");
            Keyboard previousKeyboard = Keyboard.current;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Press(keyboard, key);
                EditorTestLifecycle.Invoke(input, "Update");
                Assert.That((bool)Get(state, "_restartRequested"), Is.EqualTo(requestsReturn));
            }
            finally { InputSystem.RemoveDevice(keyboard); previousKeyboard?.MakeCurrent(); }
        }

        [TestCase(Key.J, true)]
        [TestCase(Key.Enter, false)]
        [TestCase(Key.Space, false)]
        public void OnlyJRequestsRespawnAfterCountdown(Key key, bool requestsRevive)
        {
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake");
            var player = EditorTestLifecycle.AddNetwork<PlayerManager>(new GameObject("Dead player"));
            Set(player, "isDead", true);
            Set(player, "_respawnCountdown", new PlayerRespawnCountdown(Time.timeAsDouble - 6d));
            var routine = (IEnumerator)typeof(PlayerManager).GetMethod("RespawnRoutine", Private).Invoke(player, null);
            routine.MoveNext();
            Keyboard previousKeyboard = Keyboard.current;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Press(keyboard, key);
                Assert.That(routine.MoveNext(), Is.True);
                Assert.That(routine.Current is WaitForSeconds, Is.EqualTo(requestsRevive));
            }
            finally { InputSystem.RemoveDevice(keyboard); previousKeyboard?.MakeCurrent(); }
        }

        [TestCase(true, GameInputMode.Gameplay)]
        [TestCase(false, GameInputMode.Results)]
        public void VisibleResultsDoNotPauseTheWinner(bool winner, GameInputMode expected)
        {
            var state = EditorTestLifecycle.AddNetwork<BattleStateMachine>(new GameObject("Finished match"));
            EditorTestLifecycle.Invoke(state, "Awake");
            state.CurrentState = BattleState.MatchEnded;
            Set(state, "_resultPanelVisible", true);
            var player = EditorTestLifecycle.AddNetwork<PlayerManager>(new GameObject("Local participant"));
            Set(player, "_hasMatchEndPresentation", true);
            Set(player, "_isMatchWinner", winner);
            Set(player, "_matchEndLocked", !winner);
            typeof(NetworkClient).GetProperty(nameof(NetworkClient.localPlayer)).SetValue(null, player.netIdentity);
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake");
            GameInputController.RefreshCursorState();
            Assert.That(GameInputController.CurrentMode, Is.EqualTo(expected));
            Assert.That(GameInputController.IsPaused, Is.EqualTo(!winner));
        }

        [Test] public void PromptIsWhiteOnOneBlackEightyPercentBackplate()
        {
            var panel = new GameObject("Death overlay", typeof(RectTransform));
            var label = new GameObject("Respawn prompt", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(panel.transform, false);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.color = Color.black;
            text.rectTransform.sizeDelta = new Vector2(600f, 60f);
            BattleActionPrompt.Style(text);
            BattleActionPrompt.Style(text);
            Assert.That(text.color, Is.EqualTo(Color.white));
            var backgrounds = panel.GetComponentsInChildren<Image>();
            Assert.That(backgrounds, Has.Length.EqualTo(1));
            Assert.That(backgrounds[0].color, Is.EqualTo(new Color(0f, 0f, 0f, .8f)));
            Assert.That(backgrounds[0].raycastTarget, Is.False);
            Assert.That(backgrounds[0].transform.GetSiblingIndex(), Is.LessThan(text.transform.GetSiblingIndex()));
        }

        [Test] public void ReturnRequestRequiresAuthenticatedFinishedMatchParticipant()
        {
            var manager = new GameObject("Network manager").AddComponent<BattleNetworkManager>();
            var state = EditorTestLifecycle.AddNetwork<BattleStateMachine>(new GameObject("State"));
            EditorTestLifecycle.Invoke(state,"Awake");
            var player = new GameObject("Participant").AddComponent<NetworkIdentity>();
            var connection = new NetworkConnectionToClient(101);
            typeof(NetworkConnection).GetProperty(nameof(NetworkConnection.identity)).SetValue(connection, player);
            var active = typeof(NetworkServer).GetProperty(nameof(NetworkServer.active));
            var canReturn = typeof(BattleNetworkManager).GetMethod("CanReturnToWaiting", Private);
            try
            {
                active.SetValue(null,true);
                state.CurrentState = BattleState.MatchEnded;
                Assert.That(canReturn.Invoke(manager,new object[]{connection}),Is.False);
                connection.isAuthenticated=true;
                Assert.That(canReturn.Invoke(manager,new object[]{connection}),Is.True);
                state.CurrentState=BattleState.InBattle;
                Assert.That(canReturn.Invoke(manager,new object[]{connection}),Is.False);
                state.CurrentState=BattleState.MatchEnded;
                ((System.Collections.Generic.HashSet<int>)Get(manager,"_pendingReturns")).Add(101);
                Assert.That(canReturn.Invoke(manager,new object[]{connection}),Is.False,"A repeated request cannot teleport twice.");
            }
            finally { active.SetValue(null,false); }
        }

        [Test] public void RespawnCountReplacesZeroWithReadyAndUsesJ()
        {
            Assert.That(BattleActionPrompt.Respawn,Is.EqualTo("J를 눌러 부활하기"));
            Assert.That(BattleActionPrompt.RespawnStatus(3),Is.EqualTo("부활까지 3초 남았습니다."));
            Assert.That(BattleActionPrompt.RespawnStatus(0),Is.EqualTo("부활할 준비가 되었습니다."));
            Assert.That(BattleActionPrompt.RespawnStatus(-1),Is.EqualTo("부활할 준비가 되었습니다."));
        }

        [Test] public void NormalSceneTransitionAllowsReturnAgainInTheNextMatch()
        {
            var manager = new GameObject("Network manager").AddComponent<BattleNetworkManager>();
            Set(manager, "_returningFromMatch", true);
            ((System.Collections.Generic.HashSet<int>)Get(manager,"_pendingReturns")).Add(101);
            var previousConnection = NetworkClient.connection;
            var connectionProperty = typeof(NetworkClient).GetProperty(nameof(NetworkClient.connection));
            try
            {
                connectionProperty.SetValue(null, new NetworkConnectionToServer());
                manager.OnClientSceneChanged();
            }
            finally { connectionProperty.SetValue(null, previousConnection); }
            Assert.That(Get(manager,"_returningFromMatch"),Is.False);
            Assert.That((System.Collections.Generic.HashSet<int>)Get(manager,"_pendingReturns"),Is.Empty);
            Assert.That(manager.CanStartNextRound,Is.True);
        }

        [Test] public void ResultDesignHasCenteredJAndNoStayButton()
        {
            var view=new BattleResultView(new BattleResultBindings(),new BattleResultLabels());
            try
            {
                view.SetStandings(new[]{new BattleResultRow{NetId=1,Rank=1,PlayerName="<b>Winner</b>",CharacterName="바바리안",Kills=8,Damage=1234}},1,180);
                view.Show(new PersonalBattleResult("<b>Winner</b>",1,"<b>Winner</b>",400,1234,"Rival",1,"Opponent",3));
                var canvas=GameObject.Find("Battle Result Design");
                Assert.That(canvas,Is.Not.Null);
                var buttons=canvas.GetComponentsInChildren<Button>();
                Assert.That(buttons,Has.Length.EqualTo(1));
                var rect=buttons[0].GetComponent<RectTransform>();
                Assert.That(rect.anchoredPosition+new Vector2(rect.rect.width/2,-rect.rect.height/2),Is.EqualTo(new Vector2(960,-670)));
                var name=canvas.transform.Find("Result composition/Player name").GetComponent<TMP_Text>();
                Assert.That(name.richText,Is.False);
                Assert.That(name.text,Does.Contain("Winner"));
                Assert.That(canvas.transform.Find("Result composition/Final standings/Standings rows").childCount,Is.EqualTo(1));
            }
            finally
            {
                var canvas=GameObject.Find("Battle Result Design");
                if(canvas!=null) Object.DestroyImmediate(canvas);
            }
        }

        [Test] public void ActiveWaitingSceneCannotUnlockRemoteBattleSkillsOrStats()
        {
            _scene.name = "Battle_waiting";
            var player = new GameObject("Retained battle participant");
            var loadout = EditorTestLifecycle.AddNetwork<SkillLoadout>(player);
            var stats = EditorTestLifecycle.AddNetwork<StatManager>(player);
            EditorTestLifecycle.Invoke(loadout, "Awake");
            int[] before = SkillLoadout.Defaults();
            foreach (int choice in before) loadout.Choices.Add(choice);
            Set(loadout, "_initialized", true);
            Set(loadout, "_nextRequest", double.NegativeInfinity);
            Set(stats, "_serverStatsInitialized", true);
            Set(stats, "_nextStatRequestAt", double.NegativeInfinity);
            Set(stats, "_stats", new StatContainer { STR = new StatSlot { Invested = 30f } });
            Scene battle = EditorSceneManager.NewPreviewScene();
            battle.name = "Battle";
            try
            {
                SceneManager.MoveGameObjectToScene(player, battle);
                int[] requested = (int[])before.Clone();
                (requested[0], requested[1]) = (requested[1], requested[0]);
                MethodInfo command = System.Array.Find(typeof(SkillLoadout).GetMethods(Private | BindingFlags.Public),
                    method => method.Name.StartsWith("UserCode_CmdSet__"));
                Assert.That(command, Is.Not.Null, "Invoke the woven server command body, not its client transport wrapper.");
                command.Invoke(loadout, new object[] { requested });
                Assert.That(loadout.Snapshot(), Is.EqualTo(before));
                var nextStats = new StatContainer { AGI = new StatSlot { Invested = 30f } };
                Assert.That(typeof(StatManager).GetMethod("TryAcceptClientStats", Private).Invoke(stats, new object[] { nextStats }), Is.False);
                Assert.That(stats.GetStatsCopy().STR.Invested, Is.EqualTo(30f));
            }
            finally { SceneManager.MoveGameObjectToScene(player, _scene); EditorSceneManager.ClosePreviewScene(battle); }
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Press(Keyboard keyboard, Key key)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            // ButtonControl starts frame-edge tracking lazily on its first query.
            _ = keyboard.jKey.wasPressedThisFrame;
            _ = keyboard.enterKey.wasPressedThisFrame;
            _ = keyboard.spaceKey.wasPressedThisFrame;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            Assert.That(keyboard[key].wasPressedThisFrame, Is.True, "The synthetic keyboard must generate a press edge before polling gameplay.");
        }
    }
}
