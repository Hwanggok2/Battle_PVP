using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CombatInputRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(1, -45)] [TestCase(1, 0)] [TestCase(1, 45)]
        [TestCase(2, -45)] [TestCase(2, 0)] [TestCase(2, 45)]
        [TestCase(3, -45)] [TestCase(3, 0)] [TestCase(3, 45)]
        public void BakedMeleeTrackMatchesTheVisibleAimedWeapon(int index, float pitch)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            try
            {
                EditorTestLifecycle.BindNetwork(player);
                var attack = AssetDatabase.LoadAssetAtPath<AttackData>($"Assets/Player/AttackData/Atk_{index}.asset");
                Assert.That(attack.motionSamples, Has.Length.EqualTo(241), "Rebake motion after changing the weapon or animation.");
                player.transform.SetPositionAndRotation(new Vector3(10, 2, 3), Quaternion.Euler(0, 37, 0));
                var animator = player.GetComponent<Animator>();
                animator.fireEvents = false; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Update(0); animator.Play("Movement", 0, 0);
                animator.Play(attack.animationName, 1, 0); animator.Update(0);
                animator.Play(attack.animationName, 1, .6f); animator.Update(0);
                var blade = player.GetComponentInChildren<MeleeHitBox>(true).transform;
                var aim = new MeleeAimPose(player.transform, animator);
                Vector3 direction = Quaternion.AngleAxis(pitch, player.transform.right) * player.transform.forward;
                aim.ApplyCalibrated(direction, aim.SelectReference(attack, 1.4f), .6f, 1f, direction);
                Assert.That(MeleeMotionSample.TryEvaluate(attack, player.transform, .6f, direction * 1.4f, direction, 1f, out Pose sample), Is.True);
                Assert.That(Vector3.Distance(sample.position, blade.position), Is.LessThan(.003f));
                Assert.That(Quaternion.Angle(sample.rotation, blade.rotation), Is.LessThan(.2f));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void ReconstructedArcPreservesBothVisibleEndpoints()
        {
            var expectedFrom = new Pose(Vector3.right, Quaternion.Euler(0, 30, 0));
            var expectedTo = new Pose(Vector3.left, Quaternion.Euler(0, 210, 0));
            var actualFrom = new Pose(Vector3.up, Quaternion.Euler(20, 50, 10));
            var actualTo = new Pose(Vector3.down, Quaternion.Euler(40, 220, 5));
            Pose first = MeleeMotionSample.MatchEndpoints(expectedFrom, expectedFrom, expectedTo, actualFrom, actualTo, 0);
            Pose last = MeleeMotionSample.MatchEndpoints(expectedTo, expectedFrom, expectedTo, actualFrom, actualTo, 1);
            Assert.That(Vector3.Distance(first.position, actualFrom.position), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(last.position, actualTo.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(first.rotation, actualFrom.rotation), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(last.rotation, actualTo.rotation), Is.LessThan(.05f));
        }

        [Test]
        public void InactiveSliderAcceptsTheSavedAllocationBeforeAwake()
        {
            var root = new GameObject("Inactive stat row"); root.SetActive(false);
            try
            {
                var slider = root.AddComponent<Slider>();
                var row = root.AddComponent<StatSlider>();
                typeof(StatSlider).GetField("_slider", Private).SetValue(row, slider);
                Assert.That(slider.maxValue, Is.EqualTo(1));
                row.SetInvestedWithoutNotify(30);
                Assert.That(row.Invested, Is.EqualTo(30));
                EditorTestLifecycle.Invoke(row, "OnEnable");
                Assert.That(row.Invested, Is.EqualTo(30));
                EditorTestLifecycle.Invoke(row, "OnDisable");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(.45f, .75f, true, .2666667f, .7333333f)]
        [TestCase(.1f, .4f, false, 0, 0)]
        [TestCase(.8f, .9f, false, 0, 0)]
        [TestCase(.6f, .6f, false, 0, 0)]
        public void FastAnimationSegmentsAreClippedToTheAuthoredHitWindow(float previous, float current,
            bool hits, float expectedFrom, float expectedTo)
        {
            Assert.That(new AnimationHitWindow(.53f, .67f).Clip(previous, current, out float from, out float to), Is.EqualTo(hits));
            Assert.That(from, Is.EqualTo(expectedFrom).Within(.0001f));
            Assert.That(to, Is.EqualTo(expectedTo).Within(.0001f));
        }

        [TestCase(1f)] [TestCase(2f)]
        public void KickWindowUsesTheActualAnimatorPlaybackRate(float speed)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            try
            {
                EditorTestLifecycle.BindNetwork(player);
                var animator = player.GetComponent<Animator>(); animator.Rebind();
                animator.speed = speed; animator.Play("Kick", 0, 0); animator.Update(0);
                var combat = player.GetComponent<PlayerCombat>();
                typeof(PlayerCombat).GetField("animator", Private).SetValue(combat, animator);
                var data = typeof(PlayerCombat).GetField("_monostatConSkillData", Private).GetValue(combat);
                var args = new object[] { data, 0f, 0f };
                typeof(PlayerCombat).GetMethod("ResolveKickAnimationWindow", Private).Invoke(combat, args);
                Assert.That((float)args[1], Is.EqualTo(.4f / speed).Within(.001f));
                Assert.That((float)args[2], Is.EqualTo((2f / 3f) / speed + 1f / 30f).Within(.001f));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void EscapeTogglesCursorWithoutOpeningTheSettingsPanel()
        {
            var scene = SceneManager.GetActiveScene();
            string originalName = scene.name; scene.name = "Battle";
            var root = new GameObject("Input test"); root.SetActive(false);
            var settingsRoot = new GameObject("Settings test"); settingsRoot.SetActive(false);
            var panel = new GameObject("Settings panel"); panel.SetActive(false);
            var instance = typeof(GameSettingsPanel).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = instance.GetValue(null);
            try
            {
                var settings = settingsRoot.AddComponent<GameSettingsPanel>();
                typeof(GameSettingsPanel).GetField("_panel", Private).SetValue(settings, panel); instance.SetValue(null, settings);
                var input = root.AddComponent<GameInputController>(); EditorTestLifecycle.Invoke(input, "Awake");
                var gate = (FrameInputGate)typeof(GameInputController).GetField("InputGate", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                GameInputController.HandleEscape();
                Assert.That(GameInputController.CurrentMode, Is.EqualTo(GameInputMode.Menu));
                Assert.That(panel.activeSelf, Is.False);
                gate.Clear(); GameInputController.HandleEscape();
                Assert.That(GameInputController.CurrentMode, Is.EqualTo(GameInputMode.Gameplay));
                Assert.That(panel.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(settingsRoot); Object.DestroyImmediate(panel);
                instance.SetValue(null, previous); scene.name = originalName;
            }
        }
    }
}
