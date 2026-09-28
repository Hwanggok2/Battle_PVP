using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class RetainedPlayerStateTests
    {
        [Test]
        public void AirborneReconnectDoesNotGrantAnotherJumpBudget()
        {
            var validator = new ServerMovementValidator();
            validator.Reset(Vector3.zero, 10d);
            Assert.That(validator.TryAccept(new Vector3(0f, 1.2f, 0f), Quaternion.identity, 10.4d, 10.4d,
                5f, 1.4f, 9.81f, false, 45f), Is.True);
            validator.Rebase(new Vector3(0f, 1.2f, 0f), 10.45d, false);
            Assert.That(validator.TryAccept(new Vector3(0f, 2.4f, 0f), Quaternion.identity, 10.5d, 10.5d,
                5f, 1.4f, 9.81f, false, 45f), Is.False, "Reconnect cannot reset the original ground height.");
            validator.Rebase(Vector3.zero, 11d, true);
            Assert.That(validator.TryAccept(new Vector3(0f, 1.2f, 0f), Quaternion.identity, 11.4d, 11.4d,
                5f, 1.4f, 9.81f, false, 45f), Is.True, "Landing restores the normal jump budget.");
        }

        [Test]
        public void MovementSnapshotUsesOriginalExpiryAfterReconnectLatency()
        {
            var server = new MovementEffects();
            server.Set(1, 0.5f, 10f, 100d);
            server.Set(2, 2f, 2f, 100d);
            var client = new MovementEffects();
            client.Restore(server.Capture(101d), 103d);
            Assert.That(client.Evaluate(103d), Is.EqualTo(0.5f));
            Assert.That(client.Evaluate(109.99d), Is.EqualTo(0.5f));
            Assert.That(client.Evaluate(110d), Is.EqualTo(1f));
        }

        [Test]
        public void ReconnectDoesNotCopyOldOwnerPredictionsOrOverwriteNewCastLock()
        {
            var server = new InputLockEffects();
            server.Set(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move, float.PositiveInfinity, 100d);
            server.Set(CombatEffectSources.PredictedCastMovement, SkillInputLockFlags.Move, float.PositiveInfinity, 100d);
            server.Set(CombatEffectSources.AdvancedSkillInput, SkillInputLockFlags.Attack, 8f, 100d);
            server.Set(999, SkillInputLockFlags.Jump, 5f, 100d);
            InputLockSnapshot[] snapshot = server.CaptureForReconnect(101d);
            Assert.That(snapshot.Length, Is.EqualTo(1));
            var client = new InputLockEffects();
            client.Set(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move, 1f, 102d);
            client.Restore(snapshot, 102d);
            Assert.That(client.Evaluate(102d), Is.EqualTo(SkillInputLockFlags.Move | SkillInputLockFlags.Jump));
            Assert.That(client.Evaluate(104d), Is.EqualTo(SkillInputLockFlags.Jump));
            Assert.That(client.Evaluate(105d), Is.EqualTo(SkillInputLockFlags.None));
        }

        [Test]
        public void HostHitchFinishesOnceAndDoesNotAccumulateDrift()
        {
            var clock = new MatchClock();
            clock.Start(100d, 180d);
            Assert.That(clock.Remaining(230.25d), Is.EqualTo(49.75d));
            Assert.That(clock.TryFinish(290d), Is.True);
            Assert.That(clock.TryFinish(291d), Is.False);
        }

        [Test]
        public void ReconnectingAfterRespawnDeadlineDoesNotRestartFiveSecondWait()
        {
            PlayerRespawnCountdown countdown = PlayerRespawnCountdown.FromServerDeadline(30d, 120d, 105d);
            Assert.That(countdown.IsReady(30d), Is.True);
            Assert.That(countdown.SecondsRemaining(30d), Is.Zero);
            countdown = PlayerRespawnCountdown.FromServerDeadline(30d, 0d, 105d, 120d);
            Assert.That(countdown.IsReady(30d), Is.True, "Reliable spawn can arrive before the first time snapshot.");
        }

        [Test]
        public void ConnectionHookRemovesRetainedBodyFromRosterAndReaddsItOnlyOnce()
        {
            var previous = ScoreSystem.ActiveScores.ToArray();
            ScoreSystem.ActiveScores.Clear();
            GameObject player = new GameObject("Retained roster test");
            int notifications = 0;
            System.Action<ScoreSystem> observer = _ => notifications++;
            try
            {
                EditorTestLifecycle.AddNetwork<PlayerManager>(player);
                ScoreSystem score = EditorTestLifecycle.AddNetwork<ScoreSystem>(player);
                score.OnStartClient();
                Assert.That(ScoreSystem.ActiveScores.Count, Is.EqualTo(1));
                ScoreSystem.OnScoreUpdated += observer;
                FieldInfo field = typeof(ScoreSystem).GetField("_isConnected", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo hook = typeof(ScoreSystem).GetMethod("OnConnectionChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                field.SetValue(score, false);
                hook.Invoke(score, new object[] { true, false });
                Assert.That(ScoreSystem.ActiveScores.Count, Is.Zero);
                Assert.That(score.gameObject.activeSelf, Is.True, "Roster removal must not destroy or disable the combat target.");
                field.SetValue(score, true);
                hook.Invoke(score, new object[] { false, true });
                hook.Invoke(score, new object[] { false, true });
                Assert.That(ScoreSystem.ActiveScores.Count, Is.EqualTo(1));
                Assert.That(notifications, Is.EqualTo(2), "One departure and one return must not duplicate host roster notifications.");
            }
            finally
            {
                ScoreSystem.OnScoreUpdated -= observer;
                Object.DestroyImmediate(player);
                ScoreSystem.ActiveScores.Clear();
                ScoreSystem.ActiveScores.AddRange(previous);
            }
        }
    }
}
