using System.Reflection;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class MeleeAimPoseTests
    {
        private GameObject _player;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        [SetUp] public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_player); }

        [TestCase(-60f, 0f)] [TestCase(60f, 0f)]
        [TestCase(0f, -60f)] [TestCase(0f, 60f)] [TestCase(-45f, 35f)]
        public void AdditiveAimRotatesTheSwordWithoutRotatingTheLegsOrAccumulating(float pitch, float yaw)
        {
            var animator = _player.GetComponent<Animator>();
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var leg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var originalHand = hand.rotation; var originalLeg = leg.rotation;
            var pose = new MeleeAimPose(_player.transform, animator);
            Vector3 direction = Quaternion.Euler(pitch, yaw, 0) * _player.transform.forward;
            Quaternion delta = Quaternion.FromToRotation(_player.transform.forward, direction);
            for (int i=0; i<20; i++)
            {
                pose.Apply(direction);
                Assert.That(Quaternion.Angle(hand.rotation, delta * originalHand), Is.LessThan(.05f));
                Assert.That(Quaternion.Angle(leg.rotation, originalLeg), Is.LessThan(.05f));
                pose.Restore();
                Assert.That(Quaternion.Angle(hand.rotation, originalHand), Is.LessThan(.05f));
            }
        }

        [Test] public void ServerRejectsStaleWrongAttackFloodAndInvalidAimUpdates()
        {
            var combat = _player.GetComponent<PlayerCombat>();
            Set(combat, "isAttacking", true); Set(combat, "_currentAttackSequence", 7u);
            Assert.That(Accept(combat, 7, 1, Vector3.up, 1), Is.True);
            Assert.That(Accept(combat, 6, 2, Vector3.down, 2), Is.False);
            Assert.That(Accept(combat, 7, 1, Vector3.down, 2), Is.False);
            Assert.That(Accept(combat, 7, 2, Vector3.down, 1.001), Is.False);
            Assert.That(Accept(combat, 7, 2, new Vector3(float.NaN, 0, 1), 2), Is.False);
            Assert.That(Accept(combat, 7, 2, Vector3.one * float.MaxValue, 2), Is.False);
            Assert.That(Accept(combat, 7, 2, Vector3.zero, 2), Is.False);
            Assert.That(combat.MeleeAimDirection, Is.EqualTo(Vector3.up));
            Assert.That(Accept(combat, 7, 2, Vector3.forward, 2), Is.True);
            Set(combat, "isAttacking", false);
            Assert.That(Accept(combat, 7, 3, Vector3.down, 3), Is.False);
        }

        [Test] public void InvalidAimCannotPoisonThePose()
        {
            var pose = new MeleeAimPose(_player.transform, _player.GetComponent<Animator>());
            var hand = _player.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            var original = hand.rotation;
            pose.Apply(new Vector3(float.NaN, 0, 1)); pose.Apply(Vector3.zero);
            pose.Apply(Vector3.one * float.MaxValue);
            Assert.That(Quaternion.Angle(hand.rotation, original), Is.LessThan(.05f));
        }

        [TestCase(-20f, .7f)] [TestCase(0f, .7f)] [TestCase(45f, .7f)]
        [TestCase(-20f, 1f)] [TestCase(0f, 1f)] [TestCase(45f, 1f)]
        [TestCase(-20f, 1.8f)] [TestCase(0f, 1.8f)] [TestCase(45f, 1.8f)]
        public void CalibratedBladePointCrossesCameraRayDespiteHipRotationAndScale(float pitch, float scale)
        {
            _player.transform.localScale = Vector3.one * scale;
            _player.transform.rotation = Quaternion.Euler(0, 135, 0);
            var animator = _player.GetComponent<Animator>();
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var leg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var pose = new MeleeAimPose(_player.transform, animator);
            Vector3 handPoint = new Vector3(.05f, .03f, 1.4f);
            Vector3 reference = spine.parent.InverseTransformVector(hand.TransformPoint(handPoint) - spine.position);
            for (int repeat = 0; repeat < 20; repeat++)
            {
                spine.parent.rotation = Quaternion.Euler(2, 135 + repeat, -3);
                Quaternion legBefore = leg.rotation, handBefore = hand.rotation;
                Quaternion view = Quaternion.Euler(pitch, 135, 0);
                var ray = new Ray(pose.Pivot + view * new Vector3(.3f, .2f, -1f), view * Vector3.forward);
                Vector3 target = MeleeAimPose.ReachablePoint(ray, pose.Pivot, pose.ReferenceVector(reference).magnitude);
                pose.ApplyCalibrated(target - pose.Pivot, reference, .6f, 1);
                Assert.That(Vector3.Distance(hand.TransformPoint(handPoint), target), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(leg.rotation, legBefore), Is.LessThan(.05f));
                pose.Restore();
                Assert.That(Quaternion.Angle(hand.rotation, handBefore), Is.LessThan(.05f));
            }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void RequestedDepthSelectsARealBladePointAndCannotExtendItsReach(int combo)
        {
            var data = AssetDatabase.LoadAssetAtPath<AttackData>($"Assets/Player/AttackData/Atk_{combo}.asset");
            var pose = new MeleeAimPose(_player.transform, _player.GetComponent<Animator>());
            Vector3 segment = data.aimBladeTip - data.aimBladeBase;
            Assert.That(Vector3.Dot(data.aimBladeBase, segment), Is.GreaterThan(0), "The calibrated radius must increase along the blade.");
            foreach (float scale in new[] { .7f, 1f, 1.8f })
            {
                _player.transform.localScale = Vector3.one * scale;
                float near = pose.ReferenceVector(data.aimBladeBase).magnitude;
                float far = pose.ReferenceVector(data.aimBladeTip).magnitude;
                foreach (float requested in new[] { .05f, (near + far) * .5f, 10000f })
                {
                    Vector3 selected = pose.SelectReference(data, requested);
                    float t = Vector3.Dot(selected - data.aimBladeBase, segment) / segment.sqrMagnitude;
                    Assert.That(t, Is.InRange(0f, 1f));
                    Assert.That(Vector3.Distance(selected, data.aimBladeBase + segment * t), Is.LessThan(.0001f));
                    Assert.That(pose.ReferenceVector(selected).magnitude,
                        Is.EqualTo(Mathf.Clamp(requested, near, far)).Within(.0001f));
                }
            }
        }

        [Test] public void ServerPreservesBladeDepthWhenAcceptingAnAimUpdate()
        {
            var combat = _player.GetComponent<PlayerCombat>();
            Set(combat, "isAttacking", true); Set(combat, "_currentAttackSequence", 7u);
            Vector3 requested = new Vector3(.2f, -.3f, 1).normalized * 1.6f;
            Assert.That(Accept(combat, 7, 1, requested, 1), Is.True);
            Assert.That(Vector3.Distance(combat.MeleeAimDirection, requested.normalized), Is.LessThan(.0001f));
            Assert.That((float)typeof(PlayerCombat).GetField("_meleeAimReach", Private).GetValue(combat), Is.EqualTo(1.6f).Within(.0001f));
        }

        [TestCase(-20f)] [TestCase(45f)]
        public void IdleLookBendsTheUpperBodyAndAttackBlendsWithoutDoublePitch(float pitch)
        {
            var animator = _player.GetComponent<Animator>();
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var leg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Quaternion original = spine.rotation, originalLeg = leg.rotation;
            Vector3 direction = Quaternion.AngleAxis(pitch, _player.transform.right) * _player.transform.forward;
            var look = Quaternion.FromToRotation(_player.transform.forward, direction);
            var pose = new MeleeAimPose(_player.transform, animator);
            Vector3 reference = spine.parent.InverseTransformVector(_player.transform.forward);
            for (int repeat = 0; repeat < 10; repeat++)
                foreach (float attackWeight in new[] { 0f, .3f, 1f, .3f, 0f })
                {
                    pose.ApplyCalibrated(direction, reference, .6f, attackWeight, direction);
                    Assert.That(Quaternion.Angle(spine.rotation, look * original), Is.LessThan(.05f));
                    Assert.That(Quaternion.Angle(leg.rotation, originalLeg), Is.LessThan(.05f));
                    pose.Restore();
                    Assert.That(Quaternion.Angle(spine.rotation, original), Is.LessThan(.05f));
                }
        }

        [Test] public void LookPitchRejectsInvalidValuesAndFloodsAndAcceptsIdleUpdates()
        {
            var combat = _player.GetComponent<PlayerCombat>();
            var accept = typeof(PlayerCombat).GetMethod("TryAcceptLookPitch", Private);
            Assert.That((bool)accept.Invoke(combat, new object[] { 45f, 1d }), Is.True);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -61f, 61f })
                Assert.That((bool)accept.Invoke(combat, new object[] { invalid, 2d }), Is.False);
            Assert.That((bool)accept.Invoke(combat, new object[] { -20f, 1.01d }), Is.False);
            Assert.That((bool)accept.Invoke(combat, new object[] { -20f, 2d }), Is.True);
            Assert.That((float)typeof(PlayerCombat).GetField("_networkLookPitch", Private).GetValue(combat), Is.EqualTo(-20f));
        }

        [TestCase("Battle_waiting", true)] [TestCase("Battle", true)]
        [TestCase("Lobby", false)] [TestCase("Login", false)]
        public void CrosshairIsVisibleInBothFpsScenes(string sceneName, bool visible)
        {
            Scene scene = SceneManager.GetActiveScene(); scene.name = sceneName;
            var view = _player.GetComponent<BattlePvp.UI.CombatReticleView>();
            view.InitializeForLocalPlayer(_player.transform);
            var image = (UnityEngine.UI.Image)typeof(BattlePvp.UI.CombatReticleView).GetField("_baseImage", Private).GetValue(view);
            var settings = image.GetComponentInParent<BattlePvp.UI.HudVisibilitySettings>();
            EditorTestLifecycle.Invoke(settings, "Apply");
            Assert.That(image.gameObject.activeSelf, Is.True);
            Assert.That(settings.GetComponent<CanvasGroup>().alpha, Is.EqualTo(visible ? BattlePvp.UI.LocalGameSettings.Current.hudOpacity : 0f));
        }

        [Test] public void RestoringTheWaitingRoomCrosshairDoesNotShowBattleOnlyHud()
        {
            Scene scene = SceneManager.GetActiveScene(); scene.name = "Battle_waiting";
            var battleHud = new GameObject("Battle only HUD", typeof(BattlePvp.UI.HudVisibilitySettings));
            battleHud.transform.SetParent(_player.transform);
            EditorTestLifecycle.Invoke(battleHud.GetComponent<BattlePvp.UI.HudVisibilitySettings>(), "Apply");
            Assert.That(battleHud.GetComponent<CanvasGroup>().alpha, Is.Zero);
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static bool Accept(PlayerCombat combat, uint sequence, uint revision, Vector3 direction, double now) =>
            (bool)typeof(PlayerCombat).GetMethod("TryAcceptMeleeAim", Private).Invoke(combat, new object[] {sequence, revision, direction, now});
    }
}
