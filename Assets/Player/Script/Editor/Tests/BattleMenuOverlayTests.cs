using System.Linq;
using System.Reflection;
using BattlePvp.Logic;
using BattlePvp.Networking;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class BattleMenuOverlayTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static FieldInfo StaticField<T>(string name) => typeof(T).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);

        [Test] public void SettingsDialogStaysAboveEveryTopMenuWhenReopened()
        {
            var previous = StaticField<GameSettingsPanel>("<Instance>k__BackingField").GetValue(null);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Lobby.unity");
            try
            {
                var settings = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameSettingsPanel>(true)).Single();
                EditorTestLifecycle.Invoke(settings, "Awake");
                for (int i = 0; i < 2; i++)
                {
                    settings.Open();
                    var panel = (GameObject)typeof(GameSettingsPanel).GetField("_panel", Private).GetValue(settings);
                    Assert.That(panel.GetComponent<Canvas>().overrideSorting, Is.True);
                    Assert.That(panel.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(200));
                    Assert.That(panel.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                    Assert.That(GameInputController.IsPaused, Is.True);
                    for (int column = -1; column <= 4; column++)
                        Assert.That(TopMenuLayout.IsVisible(column, false, false, false, GameSettingsPanel.IsOpen), Is.False);
                    settings.Cancel();
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                StaticField<GameSettingsPanel>("<Instance>k__BackingField").SetValue(null, previous);
            }
        }

        [TestCase(false, "나가기")]
        [TestCase(true, "연습 종료")]
        public void ExitIsAboveRankingAndConfirmationCanBeCancelled(bool practice, string label)
        {
            var previousManager = NetworkManager.singleton;
            var previousPanel = StaticField<BattleExitPanel>("<Instance>k__BackingField").GetValue(null);
            var root = new GameObject("Exit test", typeof(RectTransform), typeof(Canvas));
            var managerRoot = new GameObject("Test manager");
            try
            {
                var manager = managerRoot.AddComponent<BattleNetworkManager>();
                typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, manager);
                typeof(BattleNetworkManager).GetProperty(nameof(BattleNetworkManager.IsPractice)).SetValue(manager, practice);
                var panel = root.AddComponent<BattleExitPanel>(); EditorTestLifecycle.Invoke(panel, "Awake");
                var exit = (RectTransform)root.transform.Find("Exit battle");
                Assert.That(exit.GetComponentInChildren<TMP_Text>().text, Is.EqualTo(label));
                Assert.That(exit.anchoredPosition.y - exit.rect.height / 2, Is.GreaterThan(-80));
                panel.Open();
                Assert.That(BattleExitPanel.IsOpen, Is.True);
                Assert.That(GameInputController.IsPaused, Is.True);
                var modal = root.transform.Find("Exit confirmation");
                Assert.That(modal.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(1000));
                Assert.That(modal.Find("Dialog/Question").GetComponent<TMP_Text>().text, Is.EqualTo("나가시겠습니까?"));
                modal.Find("Dialog/Cancel").GetComponent<Button>().onClick.Invoke();
                Assert.That(BattleExitPanel.IsOpen, Is.False);
                Assert.That(manager.IsReturningFromBattle, Is.False);
                Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(managerRoot);
                typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, previousManager);
                StaticField<BattleExitPanel>("<Instance>k__BackingField").SetValue(null, previousPanel);
            }
        }
    }
}
