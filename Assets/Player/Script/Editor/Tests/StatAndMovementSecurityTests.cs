using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class StatAndMovementSecurityTests
    {
        private static StatContainer Balanced() => new StatContainer
        {
            STR = new StatSlot { Invested = 8f }, CON = new StatSlot { Invested = 7f },
            AGI = new StatSlot { Invested = 8f }, DEF = new StatSlot { Invested = 7f }
        };

        [Test]
        public void AcceptsBudgetBoundaryAndPreservesServerItems()
        {
            StatContainer server = Balanced();
            server.STR.Item = 2f;
            StatContainer request = server;
            request.STR.Invested = 30f;
            request.CON.Invested = request.AGI.Invested = request.DEF.Invested = 0f;
            Assert.That(StatValidation.TryValidateClientStats(request, server, out var accepted), Is.True);
            Assert.That(accepted.STR.Item, Is.EqualTo(2f));
            Assert.That(accepted.STR.Invested, Is.EqualTo(30f));
        }

        [TestCase(-1f)]
        [TestCase(31f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void RejectsInvalidInvestmentsWithoutChangingSnapshot(float value)
        {
            StatContainer original = Balanced();
            StatContainer request = original;
            request.STR.Invested = value;
            Assert.That(StatValidation.TryValidateClientStats(request, original, out var result), Is.False);
            Assert.That(result.STR.Invested, Is.EqualTo(original.STR.Invested));
        }

        [Test]
        public void RejectsCombinedOverspendingAndForgedItemBonus()
        {
            StatContainer server = Balanced();
            StatContainer request = server;
            request.DEF.Invested += 1f;
            Assert.That(StatValidation.TryValidateClientStats(request, server, out _), Is.False);
            request = server;
            request.STR.Item = 10f;
            Assert.That(StatValidation.TryValidateClientStats(request, server, out _), Is.False);
        }

        [Test]
        public void ClientCannotFreelySwapPresetDuringBattle()
        {
            Assert.That(StatValidation.CanChangeClientPreset(true, true, false, IdentityType.Strategist), Is.False);
            Assert.That(StatValidation.CanChangeClientPreset(true, true, true, IdentityType.Monostat), Is.True);
            Assert.That(StatValidation.CanChangeClientPreset(true, true, false, IdentityType.Monostat), Is.False);
            Assert.That(StatValidation.CanChangeClientPreset(true, true, true, IdentityType.Strategist), Is.True);
            Assert.That(StatValidation.CanChangeClientPreset(true, false, false, IdentityType.Monostat), Is.True);
            Assert.That(StatValidation.CanChangeClientPreset(false, true, false, IdentityType.Monostat), Is.True);
        }

        [Test]
        public void NormalMovementAcceptedButTeleportAndReplayRejected()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            Assert.That(verifier.TryAccept(new Vector3(0.4f, 0f, 0f), Quaternion.identity,
                0.1d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.True);
            Assert.That(verifier.TryAccept(new Vector3(100f, 0f, 0f), Quaternion.identity,
                0.2d, 0.2d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.Position.x, Is.EqualTo(0.4f));
            Assert.That(verifier.TryAccept(new Vector3(0.4f, 0f, 0f), Quaternion.identity,
                0.1d, 0.2d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
        }

        [Test]
        public void FloodingPacketsDoesNotMultiplyMovementTolerance()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            for (int i = 1; i <= 500; i++)
                verifier.TryAccept(new Vector3(i * 0.01f, 0f, 0f), Quaternion.identity,
                    0.1d + i * 0.00001d, 0.1d, 5f, 1.4f, 9.81f, true, 45f);
            Assert.That(verifier.Position.x, Is.LessThanOrEqualTo(1.001f));
        }

        [Test]
        public void ForcedMoveOnlyAllowsAuthorizedDirectionAndDistance()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            verifier.AuthorizeForcedMove(Vector3.right, 5f, 0.2f, 0d);
            Assert.That(verifier.TryAccept(Vector3.left * 5f, Quaternion.identity,
                0.1d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.TryAccept(Vector3.right * 5f, Quaternion.identity,
                0.1d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.True);
            Assert.That(verifier.TryAccept(Vector3.right * 10f, Quaternion.identity,
                0.2d, 0.2d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
        }

        [Test]
        public void JumpEnvelopeAcceptsBallisticJumpAndRejectsHovering()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            float launchSpeed = Mathf.Sqrt(2f * 1.4f * 9.81f);
            for (int i = 1; i <= 10; i++)
            {
                float t = i * 0.1f;
                float y = launchSpeed * t - 0.5f * 9.81f * t * t;
                Assert.That(verifier.TryAccept(new Vector3(0f, y, 0f), Quaternion.identity,
                    t, t, 5f, 1.4f, 9.81f, false, 45f), Is.True, "jump t=" + t);
            }
            Assert.That(verifier.TryAccept(Vector3.up, Quaternion.identity,
                2d, 2d, 5f, 1.4f, 9.81f, false, 45f), Is.False);
        }

        [Test]
        public void InvalidCoordinatesAndTimesNeverEnterHistory()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            Assert.That(verifier.TryAccept(new Vector3(float.NaN, 0f, 0f), Quaternion.identity,
                0.1d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.TryAccept(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f),
                0.1d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.TryAccept(Vector3.zero, Quaternion.identity,
                double.NaN, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.TryAccept(Vector3.zero, Quaternion.identity,
                2d, 0.1d, 5f, 1.4f, 9.81f, true, 45f), Is.False);
            Assert.That(verifier.Position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void StandingIdleDoesNotCountAsJumpFlightTime()
        {
            var verifier = new ServerMovementValidator();
            verifier.Reset(Vector3.zero, 0d);
            Assert.That(verifier.TryAccept(new Vector3(0f, 0.4f, 0f), Quaternion.identity,
                5.1d, 5.1d, 5f, 1.4f, 9.81f, false, 45f), Is.True);
            Assert.That(verifier.TryAccept(Vector3.up, Quaternion.identity,
                7d, 7d, 5f, 1.4f, 9.81f, false, 45f), Is.False);
        }

        [Test]
        public void EndingChargeDoesNotRemoveIndependentSlowOrBuff()
        {
            var effects = new MovementEffects();
            effects.Set(101, 0.5f, 30f, 0d);
            effects.Set(102, 1.2f, 10f, 0d);
            effects.Set(104, 0.8f, 5f, 0d);
            Assert.That(effects.Evaluate(1d), Is.EqualTo(0.48f).Within(0.0001f));
            effects.Remove(101);
            Assert.That(effects.Evaluate(1d), Is.EqualTo(0.96f).Within(0.0001f));
            Assert.That(effects.Evaluate(6d), Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(effects.Evaluate(11d), Is.EqualTo(1f));
        }
    }
}
