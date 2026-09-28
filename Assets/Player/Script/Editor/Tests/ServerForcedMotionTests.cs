using System.Reflection;
using BattlePvp.Combat;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class ServerForcedMotionTests
    {
        [Test]
        public void OwnerPoseCannotReplaceAForceBeforeFinalEpochHandoff()
        {
            var motion = new ServerForcedMotion();
            Assert.That(motion.TryBegin(1f, 0f, 1.5f, 0.2f, 10d), Is.True);
            Assert.That(motion.AcceptsOwnerPose(4u, 4u), Is.False);
            Assert.That(motion.TakeStep(11d) * motion.Speed, Is.EqualTo(1.5d).Within(0.00001d));
            Assert.That(motion.AcceptsOwnerPose(4u, 4u), Is.False);
            motion.Cancel();
            Assert.That(motion.AcceptsOwnerPose(4u, 5u), Is.False);
            Assert.That(motion.AcceptsOwnerPose(5u, 5u), Is.True);
        }

        [Test]
        public void ReconnectDoesNotRestartTheForceOrRepeatOldJumpInput()
        {
            var motion = new ServerForcedMotion();
            motion.TryBegin(1f, 0f, 3f, 0.35f, 100d);
            motion.TrySetOwnerInput(1f, 0f, 1u, 100.01d, 100.02d);
            Assert.That(motion.TryConsumeJump(100.03d, true), Is.True);
            motion.TakeStep(100.1d);
            motion.ResetOwnerInput();
            Assert.That(motion.TrySetOwnerInput(0f, 0f, 0u, 100.11d, 100.12d), Is.True);
            Assert.That(motion.TryConsumeJump(100.13d, true), Is.False);
            Assert.That(motion.TakeStep(101d), Is.EqualTo(0.25d).Within(0.00001d));
        }

        [Test]
        public void ServerJumpAndMidairHandoffPreserveFlightHeight()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(Vector3.up * 1.2f, Quaternion.identity,
                10.4d, 10.4d, 5f, 1.4f, 9.81f, false, 45f), Is.True);
            Assert.That(validator.GetServerVerticalVelocity(9.81f), Is.LessThanOrEqualTo(Mathf.Sqrt(2f * 9.81f * 0.2f) + 0.0001f));
            validator.CommitServerMove(new Vector3(1.5f, 1.3f, 0f), 10.6d, false, -1f);
            validator.Rebase(validator.Position, 10.6d, false);
            Assert.That(validator.TryAccept(new Vector3(1.5f, 2.4f, 0f), Quaternion.identity,
                10.7d, 10.7d, 5f, 1.4f, 9.81f, false, 45f), Is.False);
            validator.CommitServerMove(new Vector3(1.5f, 0f, 0f), 11d, true, -0.5f);
            validator.BeginServerJump(1.4f, 11d);
            validator.CommitServerMove(new Vector3(1.5f, 0.5f, 0f), 11.1d, false, 4f);
            Assert.That(validator.TryAccept(new Vector3(1.5f, 1.2f, 0f), Quaternion.identity,
                11.3d, 11.3d, 5f, 1.4f, 9.81f, false, 45f), Is.True);
        }

        [Test]
        public void CollisionResultMayBeZeroWithoutRetryingOrDemandingDistance()
        {
            var motion = new ServerForcedMotion();
            motion.TryBegin(1f, 0f, 1.5f, 0.2f, 10d);
            motion.TakeStep(11d);
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            validator.CommitServerMove(Vector3.zero, 11d, true, 0f);
            Assert.That(motion.HasFinished, Is.True);
            Assert.That(motion.TakeStep(12d), Is.Zero);
            Assert.That(validator.TryAccept(Vector3.zero, Quaternion.identity,
                11.1d, 11.1d, 5f, 1.4f, 9.81f, true, 45f), Is.True);
        }

        [Test]
        public void ReliableMotionStateResetsJumpStreamAndLatchesItsInputEpoch()
        {
            GameObject player = CreatePlayer();
            try
            {
                PlayerManager manager = player.GetComponent<PlayerManager>();
                Set(manager, "_forcedJumpSequence", 5u);
                Invoke(manager, "ReceiveServerMotionState", true, Vector3.zero, Quaternion.identity, 0f, 12u, 10d);
                Assert.That(Get<uint>(manager, "_forcedJumpSequence"), Is.Zero);
                Assert.That(Get<uint>(manager, "_forcedInputEpoch"), Is.EqualTo(12u));
                Set(manager, "_movementEpoch", 13u); // A SyncVar can arrive before its reliable state RPC.
                Assert.That(Get<uint>(manager, "_forcedInputEpoch"), Is.EqualTo(12u));
                Assert.That(Get<bool>(manager, "_ownerServerMotionActive"), Is.True);
                Invoke(manager, "ReceiveServerMotionState", false, Vector3.right, Quaternion.identity, 0f, 14u, 11d);
                Assert.That(Get<bool>(manager, "_ownerServerMotionActive"), Is.False);
                Assert.That(player.transform.position, Is.EqualTo(Vector3.right));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ServerControllerMovesWithoutOwnerPacketsAndHonorsTheWall(bool wall)
        {
            GameObject player = CreatePlayer();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject obstacle = wall ? GameObject.CreatePrimitive(PrimitiveType.Cube) : null;
            try
            {
                ground.transform.position = new Vector3(0f, -1.5f, 0f);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                if (obstacle != null)
                {
                    obstacle.transform.position = new Vector3(1f, 0f, 0f);
                    obstacle.transform.localScale = new Vector3(0.2f, 4f, 4f);
                }
                Physics.SyncTransforms();
                PlayerManager manager = player.GetComponent<PlayerManager>();
                var validator = Get<ServerMovementValidator>(manager, "_serverMovement");
                validator.Reset(Vector3.zero, 10d);
                var motion = Get<ServerForcedMotion>(manager, "_serverForcedMotion");
                motion.TryBegin(1f, 0f, 1.5f, 0.2f, 10d);
                Invoke(manager, "UpdateServerForcedMovement", 11d, true);
                if (wall) Assert.That(player.transform.position.x, Is.LessThan(0.7f));
                else Assert.That(player.transform.position.x, Is.EqualTo(1.5f).Within(0.05f));
                Assert.That(validator.Position, Is.EqualTo(player.transform.position));
                Assert.That(motion.IsActive, Is.False);
                if (!wall)
                    Assert.That(validator.TryAccept(Vector3.zero, Quaternion.identity,
                        11.01d, 11.01d, 5f, 1.4f, 9.81f, true, 45f), Is.False,
                        "The owner cannot return to its pre-kick stationary coordinate after handoff.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(ground);
                if (obstacle != null) Object.DestroyImmediate(obstacle);
            }
        }

        [Test]
        public void ZeroElapsedFrameDoesNotConsumeTheBufferedServerJump()
        {
            GameObject player = CreatePlayer();
            try
            {
                PlayerManager manager = player.GetComponent<PlayerManager>();
                var motion = Get<ServerForcedMotion>(manager, "_serverForcedMotion");
                motion.TryBegin(1f, 0f, 1.5f, 0.2f, 10d);
                Assert.That(motion.TrySetOwnerInput(0f, 0f, 1u, 10d, 10d), Is.True);
                Invoke(manager, "UpdateServerForcedMovement", 10d, true);
                Assert.That(motion.TryConsumeJump(10.01d, true), Is.True,
                    "No physical step means the jump must remain buffered instead of being consumed then regrounded.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void TeleportDuringForcedMovementEndsWithTheNewValidatorOrigin()
        {
            GameObject player = CreatePlayer();
            NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
            PropertyInfo server = typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer));
            try
            {
                server.SetValue(identity, true);
                PlayerManager manager = player.GetComponent<PlayerManager>();
                var motion = Get<ServerForcedMotion>(manager, "_serverForcedMotion");
                motion.TryBegin(1f, 0f, 1.5f, 0.2f, 10d);
                Vector3 destination = new Vector3(10f, 2f, 3f);
                Invoke(manager, "ApplyAuthoritativeTeleport", destination, Quaternion.identity);
                Assert.That(motion.IsActive, Is.False);
                Assert.That(player.transform.position, Is.EqualTo(destination));
                Assert.That(Get<ServerMovementValidator>(manager, "_serverMovement").Position, Is.EqualTo(destination));
            }
            finally
            {
                server.SetValue(identity, false);
                Object.DestroyImmediate(player);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TauntDuringForcedMotionIgnoresOwnerDirectionAndRespectsStopDistanceAndWalls(bool wall)
        {
            GameObject player = CreatePlayer();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject obstacle = wall ? GameObject.CreatePrimitive(PrimitiveType.Cube) : null;
            var identity = player.GetComponent<NetworkIdentity>();
            var server = typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer));
            try
            {
                ground.transform.position = new Vector3(0f, -1.5f, 0f);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                if (obstacle != null)
                {
                    obstacle.transform.position = new Vector3(.7f, 0f, 0f);
                    obstacle.transform.localScale = new Vector3(.2f, 4f, 4f);
                }
                Physics.SyncTransforms();
                server.SetValue(identity, true);
                var manager = player.GetComponent<PlayerManager>();
                Set(manager, "_forcedTauntActive", true);
                Set(manager, "_forcedTauntTargetPosition", Vector3.right * 2f);
                Set(manager, "_forcedTauntStopDistance", 1f);
                Set(manager, "moveSpeed", 5f);
                var motion = Get<ServerForcedMotion>(manager, "_serverForcedMotion");
                motion.TryBegin(0f, 1f, .0001f, 1f, 10d);
                motion.TrySetOwnerInput(-1f, 0f, 1u, 10.99d, 11d); // Attempt to run away and jump.
                Invoke(manager, "UpdateServerForcedMovement", 11d, true);
                if (wall) Assert.That(player.transform.position.x, Is.LessThan(.5f));
                else Assert.That(player.transform.position.x, Is.EqualTo(1f).Within(.05f));
                Assert.That(player.transform.position.y, Is.LessThan(.1f), "Taunt must ignore buffered jump input.");
                Assert.That(Vector3.Dot(player.transform.forward, Vector3.right), Is.GreaterThan(.9f));
                Assert.That(Get<ServerMovementValidator>(manager, "_serverMovement").Position, Is.EqualTo(player.transform.position));
            }
            finally
            {
                server.SetValue(identity, false);
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(ground);
                if (obstacle != null) Object.DestroyImmediate(obstacle);
            }
        }

        private static GameObject CreatePlayer()
        {
            var player = new GameObject("Server force test");
            player.AddComponent<NetworkIdentity>();
            player.AddComponent<PlayerManager>();
            EditorTestLifecycle.BindNetwork(player);
            EditorTestLifecycle.Invoke(player.GetComponent<PlayerManager>(), "Awake");
            return player;
        }
        private static T Get<T>(PlayerManager manager, string name) => (T)typeof(PlayerManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        private static void Set(PlayerManager manager, string name, object value) => typeof(PlayerManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, value);
        private static void Invoke(PlayerManager manager, string name, params object[] args) => typeof(PlayerManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, args);
    }
}
