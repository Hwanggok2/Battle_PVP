using System;
using System.Collections.Generic;
using BattlePvp.Networking;
using BattlePvp.Combat;
using NUnit.Framework;
using PlayFab.ClientModels;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class NetworkProfileRegressionTests
    {
        [Test]
        public void ScoresHaveNoClientMutationCommandAndOneClaimPerDeath()
        {
            Assert.That(typeof(ScoreSystem).GetMethod("CmdAddPoint"), Is.Null);
            Assert.That(typeof(ScoreSystem).GetMethod("AddPoint"), Is.Null);
            uint processed = 0;
            Assert.That(ScoreSystem.TryClaimDeathSequence(0, true, ref processed), Is.False);
            Assert.That(ScoreSystem.TryClaimDeathSequence(1, false, ref processed), Is.False);
            Assert.That(ScoreSystem.TryClaimDeathSequence(1, true, ref processed), Is.True);
            Assert.That(ScoreSystem.TryClaimDeathSequence(1, true, ref processed), Is.False);
            Assert.That(ScoreSystem.TryClaimDeathSequence(2, true, ref processed), Is.True);
            Assert.That(ScoreSystem.TryClaimDeathSequence(1, true, ref processed), Is.False);
            Assert.That(processed, Is.EqualTo(2u));
        }

        [Test]
        public void FailedLoadRemainsFailureAndCanRetry()
        {
            int requests = 0;
            var repository = new NetworkProfileRepository((success, failure) =>
            {
                requests++;
                if (requests == 1) failure("temporary outage");
                else success(new Dictionary<string, UserDataRecord>
                {
                    { "STR", new UserDataRecord { Value = "30" } },
                    { "CON", new UserDataRecord { Value = "0" } },
                    { "AGI", new UserDataRecord { Value = "0" } },
                    { "DEF", new UserDataRecord { Value = "0" } },
                    { "LifetimeKills", new UserDataRecord { Value = "42" } }
                });
            }, (data, success, failure) => success());
            PlayerProfileLoadResult result = null;
            repository.Load(value => result = value);
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Failure));
            Assert.That(result.Profile, Is.Null);
            repository.Load(value => result = value);
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Success));
            Assert.That(result.Profile.Stats.STR.Invested, Is.EqualTo(30));
            Assert.That(result.Kills, Is.EqualTo(42));
            Assert.That(requests, Is.EqualTo(2));
        }

        [Test]
        public void NewAccountAndMalformedDocumentAreDifferentResults()
        {
            Assert.That(NetworkProfileRepository.Decode(null).Status, Is.EqualTo(ProfileLoadStatus.Empty));
            var malformed = new Dictionary<string, UserDataRecord>
            {
                { NetworkProfileRepository.ProfileKey, new UserDataRecord { Value = "{\"SchemaVersion\":99}" } }
            };
            Assert.That(NetworkProfileRepository.Decode(malformed).Status, Is.EqualTo(ProfileLoadStatus.Failure));
        }

        [Test]
        public void SaveUsesOneImmutableDocumentAndSerializesOverlappingRequests()
        {
            var writes = new List<Dictionary<string, string>>();
            var successes = new List<Action>();
            var failures = new List<Action<string>>();
            var repository = new NetworkProfileRepository((success, failure) => success(null),
                (data, success, failure) => { writes.Add(data); successes.Add(success); failures.Add(failure); });
            var snapshot = new PlayerProfileSnapshot();
            snapshot.Stats.STR.Invested = snapshot.Slots[0].STR.Invested = 30;
            snapshot.SlotUsed[0] = true;
            int completed = 0;
            bool secondSucceeded = true;
            repository.Save(snapshot, (success, error) => { Assert.That(success, Is.True); completed++; });
            snapshot.Stats.STR.Invested = snapshot.Slots[0].STR.Invested = 20;
            repository.Save(snapshot, (success, error) => { secondSucceeded = success; completed++; });
            snapshot.Stats.STR.Invested = 1;
            Assert.That(writes.Count, Is.EqualTo(1));
            Assert.That(writes[0].Count, Is.EqualTo(1));
            Assert.That(JsonUtility.FromJson<PlayerProfileSnapshot>(writes[0][NetworkProfileRepository.ProfileKey]).Stats.STR.Invested, Is.EqualTo(30));
            successes[0]();
            Assert.That(writes.Count, Is.EqualTo(2));
            Assert.That(JsonUtility.FromJson<PlayerProfileSnapshot>(writes[1][NetworkProfileRepository.ProfileKey]).Stats.STR.Invested, Is.EqualTo(20));
            failures[1]("failed write");
            // Duplicate backend callbacks must never report completion twice or dequeue another save.
            successes[1]();
            Assert.That(completed, Is.EqualTo(2));
            Assert.That(secondSucceeded, Is.False);
            repository.Save(snapshot);
            Assert.That(writes.Count, Is.EqualTo(3));
        }

        [Test]
        public void SessionChangeFailsWaitingLoadAndDropsQueuedWritesBeforeIgnoringOldResponses()
        {
            Action<Dictionary<string, UserDataRecord>> load = null;
            Action finish = null;
            int writes = 0;
            int callbacks = 0;
            var repository = new NetworkProfileRepository((success, failure) => load = success,
                (data, success, failure) => { writes++; finish = success; });
            repository.Load(result => { Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Failure)); callbacks++; });
            repository.Save(new PlayerProfileSnapshot());
            repository.Save(new PlayerProfileSnapshot());
            repository.ResetSession();
            load(null);
            finish();
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(writes, Is.EqualTo(1));
        }

        [Test]
        public void ReliableCapacityAndBatchThresholdAreIndependent()
        {
            var instance = new GameObject("Transport capacity test");
            try
            {
                var transport = instance.AddComponent<UnityRelayTransport>();
                Assert.That(transport.GetMaxPacketSize(), Is.EqualTo(UnityRelayTransport.ReliablePacketCapacity));
                Assert.That(transport.GetBatchThreshold(), Is.EqualTo(UnityRelayTransport.PacketBatchThreshold));
                Assert.That(transport.GetMaxPacketSize(), Is.GreaterThan(4096));
                Assert.That(transport.GetBatchThreshold(), Is.LessThan(transport.GetMaxPacketSize()));
                Assert.That(BattleNetworkManager.PlayerCapacity, Is.EqualTo(8));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
