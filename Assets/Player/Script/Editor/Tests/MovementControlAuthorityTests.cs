using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class MovementControlAuthorityTests
    {
        [Test]
        public void MoveLockDiscardsOldSpeedCreditButPreservesAuthorizedForcedDisplacement()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 0d);
            Assert.That(validator.TryAccept(Vector3.right, Quaternion.identity, 1d, 1d,
                5f, 1.4f, 9.81f, true, 45f), Is.True);
            Assert.That(validator.TryAccept(Vector3.right * 1.2f, Quaternion.identity, 1.1d, 1.1d,
                0f, 1.4f, 9.81f, true, 45f, true, true, 1d), Is.False);
            validator.AuthorizeForcedMove(Vector3.right, 3f, 0.2f, 1d);
            Assert.That(validator.TryAccept(Vector3.right * 4f, Quaternion.identity, 1.1d, 1.1d,
                0f, 1.4f, 9.81f, true, 45f, true, true, 1d), Is.True);
            Assert.That(validator.TryAccept(new Vector3(4f, 0f, 0.2f), Quaternion.identity, 1.2d, 1.2d,
                0f, 1.4f, 9.81f, true, 45f, true, true, 1d), Is.False);
        }

        [Test]
        public void FirstPostLockPacketKeepsOnlyTheMovementBeforeTheLock()
        {
            var history = new MovementControlHistory();
            history.Record(10d, new MovementControlState(5f, false, false, false));
            history.Record(10.15d, new MovementControlState(0f, true, true, true));
            Assert.That(history.TrySample(10.2d, out MovementControlState locked), Is.True);
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(Vector3.right * 0.5f, Quaternion.identity, 10.1d, 10.1d,
                5f, 1.4f, 9.81f, true, 45f), Is.True);
            Assert.That(validator.TryAccept(Vector3.right * 0.75f, Quaternion.identity, 10.2d, 10.3d,
                0f, 1.4f, 9.81f, true, 45f, true, true, locked.JumpLockedSince,
                locked.DistanceBeforeMoveLock(validator.LastPositionTime, 10.2d)), Is.True);
            Assert.That(validator.TryAccept(Vector3.right * 0.82f, Quaternion.identity, 10.3d, 10.4d,
                0f, 1.4f, 9.81f, true, 45f, true, true, locked.JumpLockedSince,
                locked.DistanceBeforeMoveLock(validator.LastPositionTime, 10.3d)), Is.False,
                "The pre-lock interval cannot be reused by the next packet.");
            validator.Rebase(validator.Position, 10.5d, true);
            Assert.That(locked.DistanceBeforeMoveLock(validator.LastPositionTime, 10.6d), Is.Zero,
                "Reconnect grace must not replay movement from before the retained pose.");
        }

        [Test]
        public void LockedPositionToleranceCannotBeMultipliedByPacketSpamOrReconnection()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 0d);
            for (int i = 1; i <= 100; i++)
                validator.TryAccept(Vector3.right * (i * 0.001f), Quaternion.identity, i * 0.001d, 0.1d,
                    0f, 1.4f, 9.81f, true, 45f, true, true, 0d);
            Assert.That(validator.Position.x, Is.LessThanOrEqualTo(0.0501f));
            validator.Rebase(validator.Position, 0.2d, true);
            Assert.That(validator.TryAccept(validator.Position + Vector3.right * 0.2f, Quaternion.identity, 0.3d, 0.3d,
                0f, 1.4f, 9.81f, true, 45f, true, true, 0d), Is.False);
        }

        [Test]
        public void JumpLockRejectsNewLaunchButDoesNotCutOffAnAlreadyAcceptedJump()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(new Vector3(0f, 0.5f, 0f), Quaternion.identity, 10.1d, 10.1d,
                0f, 1.4f, 9.81f, false, 45f, true, true, 10d), Is.False);
            Assert.That(validator.TryAccept(new Vector3(0f, 0.5f, 0f), Quaternion.identity, 10.1d, 10.1d,
                5f, 1.4f, 9.81f, false, 45f), Is.True);
            Assert.That(validator.TryAccept(new Vector3(0f, 1.2f, 0f), Quaternion.identity, 10.3d, 10.3d,
                0f, 1.4f, 9.81f, false, 45f, true, true, 10.2d), Is.True);
            validator.Rebase(new Vector3(0f, 1.2f, 0f), 10.35d, false);
            Assert.That(validator.TryAccept(new Vector3(0f, 1.35f, 0f), Quaternion.identity, 10.4d, 10.4d,
                0f, 1.4f, 9.81f, false, 45f, true, true, 10.2d), Is.True);
            Assert.That(validator.TryAccept(new Vector3(0f, 2.4f, 0f), Quaternion.identity, 10.5d, 10.5d,
                0f, 1.4f, 9.81f, false, 45f, true, true, 10.2d), Is.False);
        }

        [Test]
        public void DelayedPreLockLaunchAndWalkingOffALedgeRemainValid()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(Vector3.zero, Quaternion.identity, 10d, 10d,
                5f, 1.4f, 9.81f, true, 45f), Is.True);
            float y = Mathf.Sqrt(2f * 1.4f * 9.81f) * 0.2f - 0.5f * 9.81f * 0.2f * 0.2f;
            Assert.That(validator.TryAccept(new Vector3(0f, y, 0f), Quaternion.identity, 10.2d, 10.3d,
                0f, 1.4f, 9.81f, false, 45f, true, true, 10.1d), Is.True);
            validator.Reset(Vector3.zero, 20d);
            Assert.That(validator.TryAccept(new Vector3(0f, -0.1f, 0f), Quaternion.identity, 20.1d, 20.1d,
                5f, 1.4f, 9.81f, false, 45f, false, true, 20d), Is.True);
            Assert.That(validator.TryAccept(new Vector3(0f, 0.6f, 0f), Quaternion.identity, 20.2d, 20.2d,
                5f, 1.4f, 9.81f, false, 45f, false, false), Is.False,
                "Unlocking in the air does not create a second jump.");
        }

        [Test]
        public void LandingSlopeAndLateUnlockedPacketUseTheirOriginalPermissionState()
        {
            var history = new MovementControlHistory();
            history.Record(10d, new MovementControlState(5f, false, false, false));
            history.Record(10.2d, new MovementControlState(0f, true, true, true));
            Assert.That(history.TrySample(10.1d, out MovementControlState previous), Is.True);
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(new Vector3(0.4f, 0.2f, 0f), Quaternion.identity, 10.1d, 10.3d,
                previous.Speed, 1.4f, 9.81f, true, 45f, previous.MoveLocked, previous.JumpLocked), Is.True);
            validator.Rebase(Vector3.zero, 11d, true);
            Assert.That(validator.TryAccept(new Vector3(0f, 0.5f, 0f), Quaternion.identity, 11.1d, 11.1d,
                5f, 1.4f, 9.81f, false, 45f), Is.True);
        }

        [Test]
        public void CrouchSpeedCannotSustainFullStandingSpeed()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 0d);
            bool rejected = false;
            for (int i = 1; i <= 20; i++)
                if (!validator.TryAccept(Vector3.right * (i * 0.5f), Quaternion.identity, i * 0.1d, i * 0.1d,
                    5f * 0.7f * 1.05f, 1.4f, 9.81f, true, 45f)) { rejected = true; break; }
            Assert.That(rejected, Is.True);
        }

        [Test]
        public void ForcedReconnectDurationUsesFirstReliableBatchTime()
        {
            Assert.That(MovementReconnectTiming.Remaining(0d, 100d, 100.1d), Is.EqualTo(0.1d).Within(0.00001d));
            Assert.That(MovementReconnectTiming.Remaining(0d, 100d, 99.9d), Is.Zero);
            Assert.That(MovementReconnectTiming.Remaining(100.05d, 100d, 100.1d), Is.EqualTo(0.05d).Within(0.00001d));
            Assert.That(MovementReconnectTiming.Remaining(100d, 100d, double.NaN), Is.Zero);
        }
    }
}
