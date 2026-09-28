using System.Reflection;
using BattlePvp.Logic;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class CursorRoomMenuTests
    {
        private Scene _scene;
        private string _originalName;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _originalName = _scene.name;
        }
        [TearDown] public void TearDown()
        {
            if (LobbyUIManager.Instance != null) EditorTestLifecycle.Invoke(LobbyUIManager.Instance, "OnDestroy");
            foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            _scene.name = _originalName;
        }

        [TestCase("Battle_waiting", true)]
        [TestCase("Battle", true)]
        [TestCase("Lobby", false)]
        public void TSwitchesOnlyFpsScenesIntoCursorModeAndBack(string scene, bool fps)
        {
            _scene.name = scene;
            var input = Input();
            input.ToggleCursorMode();
            Assert.That(GameInputController.CurrentMode, Is.EqualTo(fps ? GameInputMode.Menu : GameInputMode.Gameplay));
            Assert.That(GameInputController.IsPaused, Is.EqualTo(fps));
            if (fps) Assert.That(Cursor.visible, Is.True);
            input.ToggleCursorMode();
            Assert.That(GameInputController.CurrentMode, Is.EqualTo(GameInputMode.Gameplay));
            Assert.That(GameInputController.IsPaused, Is.False);
        }

        [Test] public void TDoesNotInterruptChat()
        {
            _scene.name = "Battle_waiting";
            var input = Input(); GameInputController.SetTextInputActive(true);
            input.ToggleCursorMode();
            Assert.That(GameInputController.IsTextInputActive, Is.True);
            Assert.That(GameInputController.CurrentMode, Is.EqualTo(GameInputMode.TextInput));
        }

        [Test] public void ReturningFromCursorModeClosesRoomAndStatPanels()
        {
            _scene.name = "Battle_waiting";
            var input = Input();
            var manager = new GameObject("Lobby manager").AddComponent<LobbyUIManager>();
            typeof(LobbyUIManager).GetProperty("Instance").SetValue(null, manager);
            var room = new GameObject("Room"); Set(manager, "_room_UI", room);
            var stats = new GameObject("Stats"); Set(manager, "_canvas_Customizer", stats);
            input.ToggleCursorMode();
            Assert.That(room.activeSelf, Is.False);
            Assert.That(stats.activeSelf, Is.False);
            Assert.That(GameInputController.IsPaused, Is.False);
            Assert.That(GameInputController.CurrentMode, Is.EqualTo(GameInputMode.Gameplay));
        }

        [TestCase("Battle_waiting", true)]
        [TestCase("Lobby", false)]
        public void SharedBrowserExposesOnlyActionsAppropriateToTheScene(string scene, bool waiting)
        {
            _scene.name = scene;
            Input();
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Lobby_UI.prefab"));
            var room = ui.transform.Find("Battle_Panel/Room");
            Assert.That(room.gameObject.activeSelf, Is.False, "The browser must start closed.");
            room.gameObject.SetActive(true);
            var panel = room.GetComponent<WaitingRoomPanel>();
            EditorTestLifecycle.Invoke(panel, "OnEnable");
            Assert.That(room.Find("Leave Room").gameObject.activeSelf, Is.EqualTo(waiting));
            Assert.That(room.Find("CreateRoom").gameObject.activeSelf, Is.EqualTo(!waiting));
            Assert.That(room.Find("EnterRoom").gameObject.activeSelf, Is.EqualTo(!waiting));
            var list = room.GetComponentInChildren<RoomListManager>(true);
            Assert.That((bool)typeof(RoomListManager).GetField("_browseOnly", Private).GetValue(list), Is.EqualTo(waiting));
            EditorTestLifecycle.Invoke(panel, "OnDisable");
        }

        [Test] public void BrowseOnlyRowsCannotSelectAnotherRoom()
        {
            var root = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            var item = root.AddComponent<RoomListItem>();
            var button = root.GetComponent<Button>(); Set(item, "_selectButton", button);
            item.SetBrowseOnly(true); Assert.That(button.interactable, Is.False);
            item.SetBrowseOnly(false); Assert.That(button.interactable, Is.True);
        }

        private static GameInputController Input()
        {
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake"); return input;
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    }
}
