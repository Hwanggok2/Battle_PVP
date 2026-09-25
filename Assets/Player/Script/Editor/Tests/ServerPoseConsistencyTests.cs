using System;
using System.Reflection;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class ServerPoseConsistencyTests
    {
        private GameObject _root;
        private BoxCollider _body;
        private Component _history;
        private Type _historyType;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Pose history test");
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform, false);
            body.transform.localPosition = Vector3.up;
            _body = body.AddComponent<BoxCollider>();
            body.AddComponent<HitBodyPart>();
            _historyType = typeof(PlayerCombat).Assembly.GetType("ServerPoseHistory", true);
            _history = _root.AddComponent(_historyType);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);

        [Test]
        public void NetworkRootAndBodyBoundsUseTheSameAcceptedPosition()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            Assert.That(Validate(10d, new Vector3(10f, 1f, 0f)), Is.True);
            Assert.That(Validate(10d, Vector3.up), Is.False);
            Assert.That(Sample(10d), Is.EqualTo(Vector3.right * 10f));
            Assert.That(_root.transform.position, Is.EqualTo(Vector3.zero), "Recording must not move the rendered object.");
        }

        [Test]
        public void AcceptedRotationRebasesTheBodyOffset()
        {
            _body.transform.localPosition = new Vector3(2f, 1f, 0f);
            _body.size = new Vector3(1f, 0.5f, 0.25f);
            Physics.SyncTransforms();
            Store(10d, Vector3.right * 10f, Quaternion.Euler(0f, 90f, 0f));
            Assert.That(Validate(10d, new Vector3(10f, 1f, -2f)), Is.True);
            Assert.That(Validate(10d, new Vector3(12f, 1f, 0f)), Is.False);
            Assert.That(_root.transform.rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void PeriodicCaptureDoesNotReplaceApprovedRootWithHostInterpolation()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            _root.transform.position = Vector3.right;
            Physics.SyncTransforms();
            Invoke("RecordCurrentPose", 10.1d);
            Assert.That(Sample(10.1d), Is.EqualTo(Vector3.right * 10f));
            Assert.That(Validate(10.1d, new Vector3(10f, 1f, 0f)), Is.True);
            Assert.That(Validate(10.1d, new Vector3(1f, 1f, 0f)), Is.False);
        }

        [Test]
        public void OlderNetworkRecordDoesNotMoveThePeriodicCaptureBackwards()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            Store(9.9d, Vector3.right * 9f, Quaternion.identity);
            Invoke("RecordCurrentPose", 10.1d);
            Assert.That(Sample(10.1d), Is.EqualTo(Vector3.right * 10f));
        }

        [TestCase(0)]
        [TestCase(70)]
        public void AcceptedSampleReplacesPeriodicPoseAtTheSameTimeAcrossRingWrap(int earlierSamples)
        {
            for (int i = 0; i < earlierSamples; i++)
                Store(i * 0.1d, Vector3.right * i, Quaternion.identity);
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            Invoke("RecordCurrentPose", 10.2d);
            Store(10.2d, Vector3.right * 12f, Quaternion.identity);
            Assert.That(Sample(10.2d), Is.EqualTo(Vector3.right * 12f));
            Assert.That(Validate(10.2d, new Vector3(12f, 1f, 0f)), Is.True);
            Assert.That(Validate(10.2d, new Vector3(10f, 1f, 0f)), Is.False);
        }

        [Test]
        public void DelayedPacketAlsoCorrectsLaterPeriodicHoldSamples()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            Invoke("RecordCurrentPose", 10.2d);
            Store(10.1d, Vector3.right * 11f, Quaternion.identity);
            Assert.That(Sample(10.2d), Is.EqualTo(Vector3.right * 11f));
            Assert.That(Validate(10.2d, new Vector3(11f, 1f, 0f)), Is.True);
            Assert.That(Validate(10.2d, new Vector3(10f, 1f, 0f)), Is.False);
        }

        [Test]
        public void DelayedHostPacketDoesNotOverwriteNewerActualLocalPose()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            _root.transform.position = Vector3.right * 12f;
            Physics.SyncTransforms();
            Invoke("RecordPose", 10.2d, _root.transform.position, false);
            Store(10.1d, Vector3.right * 11f, Quaternion.identity);
            Assert.That(Sample(10.2d), Is.EqualTo(Vector3.right * 12f));
            Assert.That(Validate(10.2d, new Vector3(12f, 1f, 0f)), Is.True);
            Assert.That(Validate(10.2d, new Vector3(11f, 1f, 0f)), Is.False);
        }

        [Test]
        public void HistoryResetClearsTheRetainedNetworkPose()
        {
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            _root.transform.position = Vector3.right * 2f;
            Physics.SyncTransforms();
            _historyType.GetMethod("ResetHistory").Invoke(_history, null);
            Invoke("RecordCurrentPose", 11d);
            Assert.That(Sample(11d), Is.EqualTo(Vector3.right * 2f));
            Assert.That(Validate(11d, new Vector3(2f, 1f, 0f)), Is.True);
        }

        [Test]
        public void DisabledBodyColliderDoesNotCreateAnAcceptedHitRegion()
        {
            _body.enabled = false;
            Store(10d, Vector3.right * 10f, Quaternion.identity);
            Assert.That(Validate(10d, new Vector3(10f, 1f, 0f)), Is.False);
        }

        private void Store(double time, Vector3 position, Quaternion rotation)
            => Invoke("StoreNetworkPose", time, position, rotation);

        private void Invoke(string method, params object[] args)
            => _historyType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_history, args);

        private bool Validate(double time, Vector3 point)
        {
            object[] args = { time, BodyPart.Body, point, Vector3.zero, 0f };
            return (bool)_historyType.GetMethod("TryValidateBodyPart").Invoke(_history, args);
        }

        private Vector3 Sample(double time)
        {
            object[] args = { time, Vector3.zero };
            Assert.That((bool)_historyType.GetMethod("TrySample").Invoke(_history, args), Is.True);
            return (Vector3)args[1];
        }
    }
}
