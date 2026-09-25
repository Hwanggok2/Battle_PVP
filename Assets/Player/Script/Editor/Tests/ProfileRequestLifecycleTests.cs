using System;
using System.Collections.Generic;
using System.Threading;
using BattlePvp.Networking;
using NUnit.Framework;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class ProfileRequestLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void QueueConstructorOnlyStoresDelegatesAndBindsTheAccountAtFirstUse(bool saveFirst)
        {
            var calls = new int[6]; // read, write, load failure, clock, account, callback error
            string account = "account-at-construction";
            Action acknowledge = null;
            var coordinator = new ProfileWriteCoordinator();
            var queue = new ProfileRequestQueue<string>(
                (success, _) => { calls[0]++; success("loaded"); },
                (_, success, _) => { calls[1]++; acknowledge = success; },
                error => { calls[2]++; return error; },
                () => { calls[3]++; return 1d; },
                () => { calls[4]++; return account; },
                _ => calls[5]++, writeCoordinator: coordinator);

            CollectionAssert.AreEqual(new int[6], calls,
                "Construction must not read SDK/account state, time, or invoke a callback.");
            account = "account-at-first-runtime-use";
            string loaded = null;
            bool? saved = null;
            Action load = () => queue.Load(value => loaded = value);
            Action save = () => queue.Save(new Dictionary<string, string> { { "preference", "value" } },
                (success, _) => saved = success);
            if (saveFirst) { save(); load(); }
            else { load(); save(); }

            Assert.That(loaded, Is.EqualTo("loaded"));
            Assert.That(saved, Is.Null, "The write waits for its actual response.");
            Assert.That(calls[0], Is.EqualTo(1));
            Assert.That(calls[1], Is.EqualTo(1));
            Assert.That(calls[2], Is.Zero, "Initial binding is not a session reset or failed load.");
            Assert.That(calls[3], Is.GreaterThan(0));
            Assert.That(calls[4], Is.GreaterThan(0));
            Assert.That(calls[5], Is.Zero);
            Assert.That(coordinator.IsBlocked(account), Is.True);
            Assert.That(coordinator.IsBlocked("account-at-construction"), Is.False,
                "The first operation owns the current account, not a construction-time snapshot.");

            Assert.That(acknowledge, Is.Not.Null);
            acknowledge();
            Assert.That(saved, Is.True);
            Assert.That(coordinator.IsBlocked(account), Is.False);
        }

        [Test]
        public void ProductionRepositoryCanBeConstructedOnAWorkerWithoutStartingAnyRequests()
        {
            NetworkProfileRepository repository = null;
            Exception failure = null;
            int testThreadId = Thread.CurrentThread.ManagedThreadId;
            int constructorThreadId = testThreadId;
            var worker = new Thread(() =>
            {
                constructorThreadId = Thread.CurrentThread.ManagedThreadId;
                try { repository = new NetworkProfileRepository(); }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };

            // Do not call Load/Save/Tick or touch global PlayFab settings. Only constructor wiring runs here.
            worker.Start();
            Assert.That(worker.Join(TimeSpan.FromSeconds(5)), Is.True, "Repository construction did not finish within five seconds.");
            Assert.That(constructorThreadId, Is.Not.EqualTo(testThreadId));
            Assert.That(failure, Is.Null, "Constructors used by MonoBehaviour field initializers cannot require Unity's main thread.");
            Assert.That(repository, Is.Not.Null);
        }

        [Test]
        public void InactiveManagerImmediatelyRejectsReentrantProfileRequests()
        {
            var go = new GameObject("Inactive profile service");
            go.SetActive(false);
            var manager = go.AddComponent<PlayFabBattleManager>();
            try
            {
                PlayerProfileLoadResult result = null;
                bool? saved = null;
                manager.LoadPlayerProfile(value => result = value);
                manager.SavePlayerStatPresetData((ok, _) => saved = ok);
                Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Failure));
                Assert.That(saved, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [TestCase(PlayFabErrorCode.InvalidParams, 400, true)]
        [TestCase(PlayFabErrorCode.InvalidSessionTicket, 401, true)]
        [TestCase(PlayFabErrorCode.NotAuthorized, 403, true)]
        [TestCase(PlayFabErrorCode.DataLengthExceeded, 400, true)]
        [TestCase(PlayFabErrorCode.ConnectionError, 0, false)]
        [TestCase(PlayFabErrorCode.ServiceUnavailable, 400, false)]
        [TestCase(PlayFabErrorCode.InternalServerError, 500, false)]
        [TestCase(PlayFabErrorCode.PartialFailure, 400, false)]
        [TestCase(PlayFabErrorCode.InvalidParams, 408, false)]
        [TestCase(PlayFabErrorCode.InvalidParams, 500, false)]
        public void OnlyExplicitRejectedWritesReleaseTheBarrier(PlayFabErrorCode code, int httpCode, bool confirmed)
        {
            Assert.That(NetworkProfileRepository.IsConfirmedWriteRejection(
                new PlayFabError { Error = code, HttpCode = httpCode }), Is.EqualTo(confirmed));
        }

        [Test]
        public void UnknownWriteErrorBlocksFollowingSaveButExplicitFailureAllowsRetry()
        {
            var successes = new List<Action>();
            var failures = new List<Action<string>>();
            var unconfirmed = new List<Action<string>>();
            var repository = new NetworkProfileRepository((success, _) => success(null), null,
                writeWithUnconfirmed: (_, success, failure, unknown) =>
                { successes.Add(success); failures.Add(failure); unconfirmed.Add(unknown); });
            repository.Save(new PlayerProfileSnapshot());
            repository.Save(new PlayerProfileSnapshot());
            unconfirmed[0]("transport disconnected");
            repository.Save(new PlayerProfileSnapshot());
            Assert.That(successes.Count, Is.EqualTo(1));
            successes[0]();
            repository.Save(new PlayerProfileSnapshot());
            repository.Save(new PlayerProfileSnapshot());
            Assert.That(successes.Count, Is.EqualTo(2));
            failures[1]("request rejected");
            Assert.That(successes.Count, Is.EqualTo(3));
        }

        [Test]
        public void TimedOutLoadRetriesAndIgnoresOldSameSessionCallbacks()
        {
            double now = 0d;
            var requests = new List<Action<Dictionary<string, UserDataRecord>>>();
            var repository = new NetworkProfileRepository((success, _) => requests.Add(success),
                (_, success, _) => success(), () => now);
            var results = new List<PlayerProfileLoadResult>();
            repository.Load(results.Add);
            now = 15d;
            repository.Tick();
            repository.Load(results.Add);
            requests[0](null);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Status, Is.EqualTo(ProfileLoadStatus.Failure));
            requests[1](null);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[1].Status, Is.EqualTo(ProfileLoadStatus.Empty));
        }

        [Test]
        public void TimedOutSaveFailsWaitersButCannotBeOvertakenUntilItsResponse()
        {
            double now = 0d;
            var writes = new List<Action>();
            var repository = new NetworkProfileRepository((success, _) => success(null),
                (_, success, _) => writes.Add(success), () => now);
            var results = new List<bool>();
            repository.Save(new PlayerProfileSnapshot(), (ok, _) => results.Add(ok));
            repository.Save(new PlayerProfileSnapshot(), (ok, _) => results.Add(ok));
            now = 15d;
            repository.Tick();
            repository.Save(new PlayerProfileSnapshot(), (ok, _) => results.Add(ok));
            Assert.That(writes.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { false, false, false }, results);
            writes[0]();
            Assert.That(results.Count, Is.EqualTo(3));
            repository.Save(new PlayerProfileSnapshot(), (ok, _) => results.Add(ok));
            Assert.That(writes.Count, Is.EqualTo(2));
            writes[1]();
            Assert.That(results[3], Is.True);
        }

        [Test]
        public void ResetNotifiesEveryWaiterEvenIfOneCallbackThrows()
        {
            var loadCallbacks = new List<Action<Dictionary<string, UserDataRecord>>>();
            int errors = 0;
            int notified = 0;
            var repository = new NetworkProfileRepository((success, _) => loadCallbacks.Add(success),
                (_, success, _) => { }, callbackError: _ => errors++);
            repository.Load(_ => throw new InvalidOperationException("consumer"));
            repository.Load(result => { Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Failure)); notified++; });
            repository.Save(new PlayerProfileSnapshot(), (_, _) => throw new InvalidOperationException("consumer"));
            repository.Save(new PlayerProfileSnapshot(), (ok, _) => { Assert.That(ok, Is.False); notified++; });
            repository.ResetSession();
            loadCallbacks[0](null);
            Assert.That(errors, Is.EqualTo(2));
            Assert.That(notified, Is.EqualTo(2));
        }
    }
}
