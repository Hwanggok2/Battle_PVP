using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;
using System.Reflection;

namespace BattlePvp.EditorTests
{
    public sealed class CombatAuthorityTests
    {
        [Test]
        public void BowRequiresChargeAndConsumesEachApprovedShotOnce()
        {
            var authority = new BowShotAuthority();
            Assert.That(authority.TryConsume(0d, out _), Is.False);
            Assert.That(authority.TryRelease(0d, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.False);
            Assert.That(authority.TryBegin(10d), Is.True);
            Assert.That(authority.TryBegin(10.1d), Is.False);
            Assert.That(authority.TryRelease(11.2d, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.True);
            Assert.That(authority.TryConsume(11.3d, out float damage), Is.True);
            Assert.That(damage, Is.EqualTo(3f).Within(0.001f));
            Assert.That(authority.TryConsume(11.3d, out _), Is.False);
            Assert.That(authority.TryBegin(11.4d), Is.False);
            Assert.That(authority.TryBegin(11.7d), Is.True);
        }

        [Test]
        public void BowPreservesMinimumDamageAndRejectsInvalidClock()
        {
            var authority = new BowShotAuthority();
            Assert.That(authority.TryBegin(double.NaN), Is.False);
            Assert.That(authority.TryBegin(double.PositiveInfinity), Is.False);
            Assert.That(authority.TryBegin(1d), Is.True);
            Assert.That(authority.TryRelease(0.9d, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.False);
            Assert.That(authority.TryRelease(double.NaN, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.False);
            Assert.That(authority.TryRelease(1.1d, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.True);
            Assert.That(authority.TryConsume(double.NaN, out _), Is.False);
            Assert.That(authority.TryConsume(1.2d, out float damage), Is.True);
            Assert.That(damage, Is.EqualTo(1f));
        }

        [Test]
        public void BowCancellationInvalidatesChargeAndReservedShot()
        {
            var authority = new BowShotAuthority();
            authority.TryBegin(1d);
            authority.Cancel();
            Assert.That(authority.TryRelease(2d, 0.2f, 1.2f, 1f, 3f, 0.5f), Is.False);
            Assert.That(authority.TryBegin(2d), Is.True);
            authority.TryRelease(3d, 0.2f, 1.2f, 1f, 3f, 0.5f);
            authority.Cancel();
            Assert.That(authority.TryConsume(3.1d, out _), Is.False);
            Assert.That(authority.TryBegin(3.2d), Is.False, "Cancellation must not bypass the release interval.");
            Assert.That(authority.TryBegin(3.6d), Is.True);
        }

        [Test]
        public void KickRequiresApprovedSequenceAndCannotReopenAnEndedWindow()
        {
            var window = new KickHitWindowAuthority();
            Assert.That(window.TryOpen(1, 1d), Is.False);
            window.Begin(7, 10d, 11d);
            Assert.That(window.TryOpen(8, 10.2d), Is.False);
            Assert.That(window.TryOpen(7, 9.9d), Is.False);
            Assert.That(window.TryOpen(7, double.NaN), Is.False);
            Assert.That(window.TryOpen(7, 10.2d), Is.True);
            Assert.That(window.CanHit(7, 10.5d), Is.True);
            Assert.That(window.TryOpen(7, 10.5d), Is.False);
            window.Close();
            Assert.That(window.CanHit(7, 10.6d), Is.False);
            Assert.That(window.TryOpen(7, 10.6d), Is.False);
            window.Begin(8, 12d, 13d);
            Assert.That(window.TryOpen(8, 12.1d), Is.True);
            Assert.That(window.CanHit(8, 13.1d), Is.False);
            window.Cancel();
            Assert.That(window.CanHit(8, 12.2d), Is.False);
        }

        [Test]
        public void MeleeHistoryRejectsRearPointsExpiredSamplesAndNonFinitePoints()
        {
            var history = new CombatHitPoseHistory();
            history.Record(10d, Vector3.forward * 2f, Vector3.one * 0.5f, Quaternion.identity);
            Assert.That(history.Contains(10.1d, Vector3.forward * 2f), Is.True);
            Assert.That(history.Contains(10.1d, Vector3.back * 2f), Is.False);
            Assert.That(history.Contains(10.5d, Vector3.forward * 2f), Is.False);
            Assert.That(history.Contains(10d, new Vector3(float.NaN, 0f, 2f)), Is.False);
            history.Clear();
            Assert.That(history.Contains(10d, Vector3.forward * 2f), Is.False);
        }

        [Test]
        public void MeleeGeometryUsesAuthoredRotationAndCannotExpandFromClientInput()
        {
            Vector3 extents = new Vector3(0.1f, 0.1f, 2f);
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);
            Assert.That(CombatValidation.ContainsPoint(Vector3.zero, extents, rotation, Vector3.right, 0f), Is.True);
            Assert.That(CombatValidation.ContainsPoint(Vector3.zero, extents, rotation, Vector3.forward, 100f), Is.False);
            Assert.That(CombatValidation.ContainsPoint(Vector3.zero, extents, rotation,
                new Vector3(float.PositiveInfinity, 0f, 0f), 0f), Is.False);
        }

        [Test]
        public void SolidWallBlocksServerHitWhileClearPathRemainsValid()
        {
            var attacker = new GameObject("Combat test attacker");
            var target = new GameObject("Combat test target");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Vector3 origin = new Vector3(12000f, 12000f, 12000f);
                attacker.transform.position = origin;
                target.transform.position = origin + Vector3.forward * 4f;
                wall.transform.position = origin + Vector3.forward * 2f;
                Physics.SyncTransforms();
                Assert.That(CombatValidation.HasClearPath(origin, target.transform.position,
                    attacker.transform, target.transform), Is.False);
                wall.SetActive(false);
                Physics.SyncTransforms();
                Assert.That(CombatValidation.HasClearPath(origin, target.transform.position,
                    attacker.transform, target.transform), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attacker);
            }
        }

        [Test]
        public void ServerBodyHistoryRejectsForgedHeadAndOldLifeSamples()
        {
            var target = new GameObject("Combat body history test");
            try
            {
                var head = new GameObject("Head");
                head.transform.SetParent(target.transform);
                head.transform.localPosition = Vector3.up * 2f;
                head.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
                HitBodyPart part = head.AddComponent<HitBodyPart>();
                typeof(HitBodyPart).GetField("_bodyPart", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(part, BodyPart.Head);
                Physics.SyncTransforms();
                System.Type historyType = typeof(PlayerCombat).Assembly.GetType("ServerPoseHistory", true);
                Component history = target.AddComponent(historyType);
                historyType.GetMethod("RefreshBodyParts").Invoke(history, null);
                historyType.GetMethod("RecordPose", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(history, new object[] { 10d, target.transform.position, true });
                MethodInfo validate = historyType.GetMethod("TryValidateBodyPart");
                object[] actualHead = { 10d, BodyPart.Head, head.transform.position, Vector3.zero, 0f };
                Assert.That((bool)validate.Invoke(history, actualHead), Is.True);
                object[] forgedHead = { 10d, BodyPart.Head, Vector3.up, Vector3.zero, 0f };
                Assert.That((bool)validate.Invoke(history, forgedHead), Is.False);
                object[] invalidHead = { 10d, BodyPart.Head, new Vector3(float.NaN, 2f, 0f), Vector3.zero, 0f };
                Assert.That((bool)validate.Invoke(history, invalidHead), Is.False);
                historyType.GetMethod("ResetHistory").Invoke(history, null);
                Assert.That((bool)validate.Invoke(history, actualHead), Is.False);
            }
            finally { Object.DestroyImmediate(target); }
        }
    }
}
