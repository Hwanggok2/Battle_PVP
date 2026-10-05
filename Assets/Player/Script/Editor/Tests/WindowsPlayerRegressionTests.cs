using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Lobby;
using BattlePvp.Networking;
using BattlePvp.UI;
using NUnit.Framework;
using PlayFab.ClientModels;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class WindowsPlayerRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void LobbyFallbackSpawnsAboveTheRemodeledGround()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Lobby_UI.prefab");
            var activator = prefab.GetComponentInChildren<LobbyPlayerActivator>(true);
            var position = new SerializedObject(activator).FindProperty("_fallbackSpawnPosition").vector3Value;
            Assert.That(position.y, Is.EqualTo(.2f).Within(.001f));
            Assert.That(Mathf.Abs(position.x), Is.LessThan(10));
            Assert.That(Mathf.Abs(position.z), Is.LessThan(8));
        }

        [Test]
        public void SelectingAnotherRoomMovesThePersistentVisualFeedback()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/_Room.prefab");
            var a = Object.Instantiate(prefab); var b = Object.Instantiate(prefab);
            try
            {
                var first = a.GetComponent<RoomListItem>(); var second = b.GetComponent<RoomListItem>();
                EditorTestLifecycle.Invoke(first, "Awake"); EditorTestLifecycle.Invoke(second, "Awake");
                Color normal = a.GetComponent<Graphic>().color;
                first.SetInfo("first", "First", "Host", 1, null, null, false);
                EditorTestLifecycle.Invoke(first, "OnItemClicked");
                Assert.That(a.GetComponent<Graphic>().color.r, Is.LessThan(normal.r));
                Assert.That(a.GetComponent<Outline>().enabled, Is.True);
                first.SetInfo("first", "Updated", "Host", 2, null, null, false);
                Assert.That(first.IsSelected, Is.True);
                Assert.That(a.GetComponent<Graphic>().color.r, Is.LessThan(normal.r));
                EditorTestLifecycle.Invoke(second, "OnItemClicked");
                Assert.That(first.IsSelected, Is.False);
                Assert.That(a.GetComponent<Graphic>().color, Is.EqualTo(normal));
                Assert.That(b.GetComponent<Outline>().enabled, Is.True);
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [TestCase("Bow_AimHold", 1)]
        [TestCase("Crouch Walk", 2)]
        public void DeathOverridesOtherLayersAndReviveRestoresThem(string state, int layer)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            try
            {
                var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0f);
                animator.Play(state, layer, .5f); animator.Update(.1f);
                var presentation = player.AddComponent<PlayerLifePresentation>();
                presentation.Initialize(player.transform, animator, "Revive", Color.red);
                var weights = new[] { animator.GetLayerWeight(1), animator.GetLayerWeight(2) };
                animator.speed = .3f;
                presentation.PlayDeathAnimation(); animator.Update(.2f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Die"), Is.True);
                Assert.That(animator.GetLayerWeight(1), Is.Zero);
                Assert.That(animator.GetLayerWeight(2), Is.Zero);
                Assert.That(animator.speed, Is.EqualTo(1f));
                presentation.PlayDeathAnimation();
                presentation.PlayReviveAnimation();
                Assert.That(animator.GetBool("IsDead"), Is.False);
                Assert.That(animator.GetLayerWeight(1), Is.EqualTo(weights[0]));
                Assert.That(animator.GetLayerWeight(2), Is.EqualTo(weights[1]));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void DrawingBowKeepsTheSameCameraAimRay()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var camera = new GameObject("Aim camera").AddComponent<FollowCamera>();
            try
            {
                EditorTestLifecycle.BindNetwork(player);
                camera.SetTarget(player.transform);
                var bow = player.GetComponent<BowAttackController>();
                typeof(BowAttackController).GetField("_followCamera", Private).SetValue(bow, camera);
                Ray before = camera.GetAimRay();
                var toggle = typeof(BowAttackController).GetMethod("SetBowCameraOffsetActive", Private);
                toggle.Invoke(bow, new object[] { true });
                Assert.That(Vector3.Distance(camera.GetAimRay().origin, before.origin), Is.LessThan(.0001f));
                Assert.That(Vector3.Angle(camera.GetAimRay().direction, before.direction), Is.LessThan(.001f));
                toggle.Invoke(bow, new object[] { false });
                Assert.That(Vector3.Distance(camera.GetAimRay().origin, before.origin), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(player); Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void BowAimIncludesDamageTriggersButIgnoresInteractionTriggers()
        {
            var player = new GameObject("Bow aim test");
            var interaction = new GameObject("Interaction trigger");
            var target = new GameObject("Damage trigger");
            try
            {
                var origin = new Vector3(7000, 7000, 7000);
                player.transform.position = origin;
                var bow = EditorTestLifecycle.AddNetwork<BowAttackController>(player);
                interaction.transform.position = origin + Vector3.forward * 2;
                interaction.AddComponent<BoxCollider>().isTrigger = true;
                target.transform.position = origin + Vector3.forward * 5;
                target.AddComponent<BoxCollider>().isTrigger = true;
                EditorTestLifecycle.AddNetwork<BattlePvp.Stats.StatManager>(target);
                target.AddComponent<BowProjectileRecordingReceiver>();
                Physics.SyncTransforms();
                var point = (Vector3)typeof(BowAttackController).GetMethod("ResolveAimPoint", Private)
                    .Invoke(bow, new object[] { new Ray(origin, Vector3.forward) });
                Assert.That(point.z - origin.z, Is.EqualTo(4.5f).Within(.01f));
            }
            finally { Object.DestroyImmediate(player); Object.DestroyImmediate(interaction); Object.DestroyImmediate(target); }
        }

        [TestCase("Room is closed or its host lease has expired. Create a new room.", "room_closed")]
        [TestCase("ROOM_PASSWORD_INVALID", "password_invalid")]
        [TestCase("Connection approval is absent, expired or does not match.", "proof_not_ready")]
        [TestCase("The authenticated player must have joined the room.", "membership_missing")]
        public void ServiceFailuresKeepActionableSafeCodes(string message, string expected)
        {
            var result = new ExecuteCloudScriptResult { Error = new ScriptExecutionError { Message = message } };
            Assert.That(RoomServiceErrors.Classify(result), Is.EqualTo(expected));
        }

        [Test]
        public void ProofRetriesAreBoundedAndNeverRetryAnInvalidSuccessResponse()
        {
            Assert.That(RoomServiceErrors.CanRetryProof("proof_not_ready", 1), Is.True);
            Assert.That(RoomServiceErrors.CanRetryProof("proof_not_ready", 3), Is.False);
            Assert.That(RoomServiceErrors.CanRetryProof("service_unavailable", 2), Is.True);
            Assert.That(RoomServiceErrors.CanRetryProof("room_closed", 1), Is.False);
            Assert.That(RoomServiceErrors.CanRetryProof("response_mismatch", 1), Is.False);
        }
    }
}
