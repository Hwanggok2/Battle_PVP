using System.Reflection;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class BowPresentationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;

        [SetUp] public void SetUp() { _root = new GameObject("Bow regression fixture"); _root.SetActive(false); }
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void EarlyReleaseRetainsServerApprovalWithoutChargingDamageDuringAnimationWait()
        {
            var authority = new BowShotAuthority();
            Assert.That(authority.TryBegin(10), Is.True);
            Assert.That(authority.TryRelease(10.05, .25f, 1, .4f, .75f, .48f, 1.15f), Is.True);
            Assert.That(authority.TryBegin(10.6), Is.False, "Pending shot cannot be replaced by another draw.");
            Assert.That(authority.TryConsume(11.2, out float damage), Is.True);
            Assert.That(damage, Is.EqualTo(.4f).Within(.001f));
            Assert.That(authority.TryConsume(11.21, out _), Is.False);
            Assert.That(authority.TryBegin(11.21), Is.True);
        }

        [Test]
        public void AnimationReservationStillExpiresAndRejectsInvalidDuration()
        {
            var authority = new BowShotAuthority();
            authority.TryBegin(10);
            Assert.That(authority.TryRelease(10.1, .25f, 1, .4f, .75f, .48f, float.NaN), Is.False);
            Assert.That(authority.TryRelease(10.1, .25f, 1, .4f, .75f, .48f, 1000), Is.True);
            Assert.That(authority.TryConsume(17, out _), Is.False);
        }

        [TestCase(.05f,.4f)]
        [TestCase(.625f,.575f)]
        [TestCase(1f,.75f)]
        public void ReleaseIntentSchedulesArrowBeforeDrawReadyAndKeepsChargeDamage(float charge,float damage)
        {
            var bow=EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            var data=AssetDatabase.LoadAssetAtPath<JobSkillData>("Assets/Player/skill/Poly/Poly_WeaponSwap.asset");
            _root.SetActive(true);
            Set(bow,"_showCrosshair",false); Set(bow,"_isVisuallyCharging",true);
            typeof(BowAttackController).GetMethod("QueueShot",Private).Invoke(bow,new object[]{data,charge,Vector3.forward});
            Assert.That(Get<bool>(bow,"_isAimHoldReady"),Is.False,"The draw animation has not reached aim hold.");
            Assert.That(Get<bool>(bow,"_releaseArrowEventPending"),Is.True,"Arrow must be ready for this frame's LateUpdate.");
            Assert.That(Get<float>(bow,"_offlineShotMultiplier"),Is.EqualTo(damage).Within(.001f));
            Assert.That(Get<bool>(bow,"_isReleaseLocked"),Is.True,"Recovery still prevents a new draw.");
        }

        [Test] public void ImmediateServerReleaseConsumesOnceAndPreservesRecovery()
        {
            var authority=new BowShotAuthority(); authority.TryBegin(10);
            Assert.That(authority.TryRelease(10.05,.25f,1,.4f,.75f,.48f),Is.True);
            Assert.That(authority.TryConsume(10.05,out float damage),Is.True);
            Assert.That(damage,Is.EqualTo(.4f));
            Assert.That(authority.TryConsume(10.05,out _),Is.False);
            Assert.That(authority.TryBegin(10.1),Is.False);
            Assert.That(authority.TryBegin(10.54),Is.True);
        }

        [Test]
        public void EndEventCannotDiscardAnArrowWhenTheReleaseEventWasMissed()
        {
            var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            Set(bow, "_isReleaseLocked", true); Set(bow, "_hasPendingShot", true);
            bow.OnBowReleaseFinished();
            Assert.That(Get<bool>(bow, "_releaseArrowEventPending"), Is.True);
            Assert.That(Get<bool>(bow, "_releaseFinishedPending"), Is.True);
            Assert.That(bow.IsBusy, Is.True, "Keep the pose until LateUpdate consumes the shot.");
        }

        [Test]
        public void CancelAndStaleAnimationEventsDoNotResurrectAShot()
        {
            var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            Set(bow, "_hasPendingShot", true); Set(bow, "_isReleaseLocked", true);
            Set(bow, "_showCrosshair", false);
            bow.CancelCharge(); bow.OnBowReleaseArrow(); bow.OnBowReleaseFinished(); bow.OnBowDrawReady();
            Assert.That(Get<bool>(bow, "_hasPendingShot"), Is.False);
            Assert.That(Get<bool>(bow, "_releaseArrowEventPending"), Is.False);
            Assert.That(bow.IsBusy, Is.False);
        }

        [Test]
        public void StaleReleaseEventDuringDrawCannotFireWithoutReleaseIntent()
        {
            var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            Set(bow, "_hasPendingShot", true); Set(bow, "_isVisuallyCharging", true);
            bow.OnBowReleaseArrow();
            Assert.That(Get<bool>(bow, "_releaseArrowEventPending"), Is.False);
            Assert.That(Get<bool>(bow, "_hasPendingShot"), Is.True);
        }

        [Test]
        public void ServerRecoveryUsesTheSamePlaybackSpeedAsTheAnimator()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            var settings = AssetDatabase.LoadAssetAtPath<BowAttackSettings>("Assets/Player/Bow/BowAttackSettings.asset");
            Assert.That(settings, Is.Not.Null);
            var parameter = System.Array.Find(controller.parameters, x => x.name == "BowPlaybackSpeed");
            Assert.That(parameter, Is.Not.Null);
            Assert.That(parameter.defaultFloat, Is.EqualTo(settings.AnimationPlaybackSpeed));
            foreach (var state in controller.layers[settings.AnimationLayer].stateMachine.states)
                if (state.state.name == settings.DrawAnimationStateName || state.state.name == settings.ReleaseAnimationStateName)
                {
                    Assert.That(state.state.speed, Is.EqualTo(1));
                    Assert.That(state.state.speedParameterActive, Is.True);
                    Assert.That(state.state.speedParameter, Is.EqualTo("BowPlaybackSpeed"));
                }
            var animator = _root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
            var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            Set(bow, "_animator", animator); Set(bow, "_settings", settings);
            float recovery = (float)typeof(BowAttackController).GetMethod("ResolveServerReleaseLockSeconds", Private).Invoke(bow, null);
            Assert.That(recovery, Is.EqualTo(1.4333333f / 3f).Within(.001f));
        }

        [TestCase(-35f)]
        [TestCase(0f)]
        [TestCase(35f)]
        public void BowPoseFollowsPitchWithoutAccumulatingOrTiltingTheFeet(float pitch)
        {
            var hips = new GameObject("hips").transform; hips.SetParent(_root.transform, false);
            var spine = new GameObject("spine").transform; spine.SetParent(hips, false);
            var hand = new GameObject("bow hand reference").transform; hand.SetParent(spine, false);
            Vector3 authored = new Vector3(-.97425f, -.12326f, -.18881f).normalized;
            hand.localPosition = authored;
            var pose = EditorTestLifecycle.AddNetwork<BowAimRigTarget>(_root);
            Set(pose, "_hips", hips); Set(pose, "_origin", spine); Set(pose, "_weight", 1f);
            Vector3 direction = Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
            pose.SetNetworkAimDirection(direction); pose.SetYawOffsetActive(true);
            for (int i = 0; i < 20; i++)
            {
                EditorTestLifecycle.Invoke(pose, "Update"); EditorTestLifecycle.Invoke(pose, "LateUpdate");
                Assert.That(Vector3.Angle(hand.position - spine.position, direction), Is.LessThan(.1f));
                Assert.That(Vector3.Angle(hips.up, Vector3.up), Is.LessThan(.1f));
            }
            EditorTestLifecycle.Invoke(pose, "OnDisable");
            Assert.That(Quaternion.Angle(hips.localRotation, Quaternion.identity), Is.LessThan(.1f));
            Assert.That(Quaternion.Angle(spine.localRotation, Quaternion.identity), Is.LessThan(.1f));
        }

        [Test]
        public void NockedArrowTailStaysInStringHandAndSpawnFacesTheBowHand()
        {
            var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            var left = new GameObject("bow hand").transform; left.SetParent(_root.transform, false);
            var right = new GameObject("string hand").transform; right.SetParent(_root.transform, false);
            left.localPosition = new Vector3(0, 1.3f, .8f); right.localPosition = Vector3.up;
            var arrow = new GameObject("hand arrow"); arrow.transform.SetParent(_root.transform, false);
            arrow.transform.localScale = new Vector3(1, 1, 1.43f);
            var spawn = new GameObject("spawn").transform; spawn.SetParent(_root.transform, false);
            Set(bow, "_bowHand", left); Set(bow, "_stringHand", right);
            Set(bow, "_handArrowVisual", arrow); Set(bow, "_arrowSpawnPoint", spawn);
            EditorTestLifecycle.Invoke(bow, "UpdateNockedArrowPose");
            Assert.That(Vector3.Distance(arrow.transform.TransformPoint(Vector3.forward * .29609093f), right.position), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(-arrow.transform.forward, left.position - right.position), Is.LessThan(.05f));
            Assert.That(Vector3.Distance(spawn.position, arrow.transform.position), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(spawn.forward, left.position - right.position), Is.LessThan(.05f));
        }

        [Test]
        public void ProjectileMeshArrowheadFacesItsFlightDirection()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ArrowProjectile Variant.prefab");
            var mesh = prefab.GetComponentInChildren<MeshFilter>(true);
            Vector3 tip = prefab.transform.InverseTransformPoint(mesh.transform.TransformPoint(Vector3.back * .347883f));
            Vector3 tail = prefab.transform.InverseTransformPoint(mesh.transform.TransformPoint(Vector3.forward * .29609093f));
            Assert.That(tip.z, Is.GreaterThan(tail.z));
        }

        [TestCase(11.5f)]
        [TestCase(0f)]
        public void TrailOnlyExtendsToTravelledPositionsAndPreservesItsOrigin(float impactDistance)
        {
            var trail = _root.AddComponent<ArrowFlightTrail>();
            trail.Initialize(AssetDatabase.LoadAssetAtPath<Material>("Assets/Remodel/Materials/BladeSweep.mat"), Color.red, Vector3.zero);
            var line = _root.GetComponent<LineRenderer>();
            Assert.That(line.positionCount, Is.EqualTo(1));
            for (int i = 1; i <= 120; i++) trail.Sample(Vector3.forward * i * .1f);
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(line.GetPosition(0), Is.EqualTo(Vector3.zero));
            Assert.That(line.GetPosition(1).z, Is.EqualTo(12f).Within(.001f));
            trail.Finish(Vector3.forward * impactDistance);
            trail.Sample(Vector3.forward * 100);
            Assert.That(line.GetPosition(1).z, Is.EqualTo(impactDistance).Within(.001f), "Trim an observer's overshoot to the authoritative impact.");
            Assert.That(trail.IsFinished, Is.True);
        }

        [TestCase(0f, 1f)]
        [TestCase(.25f, .5f)]
        [TestCase(.5f, 0f)]
        [TestCase(.6f, 0f)]
        public void WholeTrailOpacityFadesInHalfASecond(float elapsed, float opacity)
            => Assert.That(ArrowFlightTrail.OpacityAfter(elapsed), Is.EqualTo(opacity).Within(.0001f));

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    }
}
