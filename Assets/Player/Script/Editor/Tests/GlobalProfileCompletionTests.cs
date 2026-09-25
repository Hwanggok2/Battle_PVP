using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BattlePvp.Managers;
using BattlePvp.Networking;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class GlobalProfileCompletionTests
    {
        private GameObject _object;
        private GlobalDataManager _profile;
        private readonly List<Action<PlayerProfileLoadResult>> _responses = new List<Action<PlayerProfileLoadResult>>();
        private static readonly MethodInfo BeginLoad = typeof(GlobalDataManager).GetMethod("BeginProfileLoad", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            // Keep scene/player bindings out of this load boundary fixture.
            _object = new GameObject("Global profile completion fixture");
            _object.SetActive(false);
            _profile = _object.AddComponent<GlobalDataManager>();
            _responses.Clear();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_object);

        [Test]
        public void FailedDecodePreservesExistingDataAndAllowsRetry()
        {
            StatContainer original = default;
            original.STR.Invested = 30;
            _profile.SaveStatPresetSlot(0, original, true, false);
            var completed = new List<bool>();
            Load(completed.Add);
            var malformed = new Dictionary<string, PlayFab.ClientModels.UserDataRecord>
                { { "AGI", new PlayFab.ClientModels.UserDataRecord { Value = "12" } } };
            _responses[0](NetworkProfileRepository.Decode(malformed));
            Assert.That(_profile.SavedStats.STR.Invested, Is.EqualTo(30));
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(_profile.IsPlayerStatsLoadInFlight, Is.False);
            CollectionAssert.AreEqual(new[] { false }, completed);
            Load(completed.Add);
            _responses[1](Success(20, 42));
            CollectionAssert.AreEqual(new[] { false, true }, completed);
            Assert.That(_profile.SavedStats.STR.Invested, Is.EqualTo(20));
            Assert.That(_profile.CumulativeKills, Is.EqualTo(42));
        }

        [Test]
        public void ThrowingStateListenerAndWaiterDoNotBlockOtherCompletions()
        {
            int stateNotifications = 0;
            int completed = 0;
            bool allStateCommitted = true;
            _profile.OnStatPresetSlotChanged += (_, __, ___) => throw new InvalidOperationException("consumer");
            _profile.OnStatPresetSlotChanged += (_, __, ___) =>
            {
                stateNotifications++;
                allStateCommitted &= _profile.HasLoadedPlayerStats && _profile.HasLoadedCombatRecord;
            };
            Load(_ => throw new InvalidOperationException("consumer"));
            Load(ok => { if (ok) completed++; });
            ExpectConsumerException();
            ExpectConsumerException();
            _responses[0](Success(30));
            Assert.That(stateNotifications, Is.EqualTo(1));
            Assert.That(allStateCommitted, Is.True);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void SessionResetFailsEveryWaiterAndKeepsReentrantRequestSeparate()
        {
            var results = new List<bool>();
            Load(_ => throw new InvalidOperationException("consumer"));
            Load(ok => { results.Add(ok); Load(results.Add); });
            Load(results.Add);
            ExpectConsumerException();
            _profile.BeginPlayerSession();
            Assert.That(_responses.Count, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { false, false }, results);
            _responses[0](Success(30));
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            _responses[1](Success(20));
            CollectionAssert.AreEqual(new[] { false, false, true }, results);
            Assert.That(_profile.SavedStats.STR.Invested, Is.EqualTo(20));
        }

        [Test]
        public void ResetInsideStateEventStopsOldNotificationsAndFailsOldCompletion()
        {
            var results = new List<bool>();
            bool reset = false;
            _profile.OnStatPresetSlotChanged += (_, __, ___) =>
            {
                if (reset) return;
                reset = true;
                _profile.BeginPlayerSession();
                Load(results.Add);
            };
            Load(results.Add);
            _responses[0](Success(30, 42));
            CollectionAssert.AreEqual(new[] { false }, results);
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(_profile.HasLoadedCombatRecord, Is.False);
            Assert.That(_profile.CumulativeKills, Is.Zero);
            _responses[1](Success(10, 3));
            CollectionAssert.AreEqual(new[] { false, true }, results);
            Assert.That(_profile.CumulativeKills, Is.EqualTo(3));
        }

        [Test]
        public void SynchronousReloadInsideResetEventDoesNotPublishOldZeroRecordAfterward()
        {
            var records = new List<int>();
            bool loaded = false;
            _profile.OnCombatRecordUpdated += (kills, _) => records.Add(kills);
            _profile.OnStatPresetSlotChanged += (_, __, ___) =>
            {
                if (loaded) return;
                loaded = true;
                Load(null);
                _responses[0](Success(30, 42));
            };
            _profile.BeginPlayerSession();
            CollectionAssert.AreEqual(new[] { 42 }, records);
            Assert.That(_profile.CumulativeKills, Is.EqualTo(42));
        }

        [Test]
        public void SameSessionDuplicateResponseCannotCompleteTheNextLoad()
        {
            var results = new List<bool>();
            Load(results.Add);
            _responses[0](new PlayerProfileLoadResult(ProfileLoadStatus.Failure));
            Load(results.Add);
            _responses[0](Success(30));
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(_profile.IsPlayerStatsLoadInFlight, Is.True);
            Assert.That(results.Count, Is.EqualTo(1));
            _responses[1](Success(20));
            CollectionAssert.AreEqual(new[] { false, true }, results);
        }

        [Test]
        public void DisableCancellationRejectsReentrantPublicLoadAndIgnoresLateResult()
        {
            var results = new List<bool>();
            Load(ok => { results.Add(ok); _profile.LoadProfileForCurrentAccount(results.Add); });
            typeof(GlobalDataManager).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_profile, null);
            CollectionAssert.AreEqual(new[] { false, false }, results);
            _responses[0](Success(30));
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(_profile.IsPlayerStatsLoadInFlight, Is.False);
        }

        private void Load(Action<bool> completed)
        {
            Action<Action<PlayerProfileLoadResult>> read = response => _responses.Add(response);
            BeginLoad.Invoke(_profile, new object[] { read, completed });
        }

        private static PlayerProfileLoadResult Success(float strength, int kills = 0)
        {
            var profile = new PlayerProfileSnapshot();
            profile.Stats.STR.Invested = profile.Slots[0].STR.Invested = strength;
            profile.SlotUsed[0] = true;
            return new PlayerProfileLoadResult(ProfileLoadStatus.Success, profile, kills);
        }

        private static void ExpectConsumerException() =>
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: consumer"));
    }
}
