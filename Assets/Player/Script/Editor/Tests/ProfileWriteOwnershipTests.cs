using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Networking;
using NUnit.Framework;
using PlayFab;

namespace BattlePvp.EditorTests
{
    public sealed class ProfileWriteOwnershipTests
    {
        private sealed class Owner
        {
            public string Account = "title/a";
            public double Now;
            public readonly List<Action> Success = new List<Action>();
            public readonly List<Action<string>> Rejected = new List<Action<string>>();
            public readonly List<Action<string>> Unknown = new List<Action<string>>();
            public readonly NetworkProfileRepository Repository;
            public Owner(ProfileWriteCoordinator coordinator = null)
            {
                Repository = new NetworkProfileRepository((success, _) => success(null), null,
                    clock: () => Now, accountKey: () => Account,
                    writeWithUnconfirmed: (_, success, rejected, unknown) =>
                    { Success.Add(success); Rejected.Add(rejected); Unknown.Add(unknown); }, writeCoordinator: coordinator);
            }
            public void Save(Action<bool, string> completed = null) => Repository.SaveCombatRecord(1, 0, completed);
        }

        [Test]
        public void ProductionRepositoriesShareCoordinatorWhileInjectedDefaultsRemainIsolated()
        {
            var first = new NetworkProfileRepository();
            var second = new NetworkProfileRepository();
            Assert.That(Coordinator(first), Is.SameAs(Coordinator(second)));
            var isolatedA = new Owner();
            var isolatedB = new Owner();
            Assert.That(Coordinator(isolatedA.Repository), Is.Not.SameAs(Coordinator(isolatedB.Repository)));
            Assert.That(Coordinator(first), Is.Not.SameAs(Coordinator(isolatedA.Repository)));
            isolatedA.Save(); isolatedB.Save();
            Assert.That(isolatedA.Success.Count, Is.EqualTo(1));
            Assert.That(isolatedB.Success.Count, Is.EqualTo(1));
        }

        [Test]
        public void ProductionAccountKeyIncludesTitleAndDoesNotAllocateWhenUnchanged()
        {
            string previousTitle = PlayFabSettings.staticSettings.TitleId;
            string previousAccount = PlayFabSettings.staticPlayer.PlayFabId;
            try
            {
                var factory = typeof(NetworkProfileRepository).GetMethod("CreateProductionAccountKey", BindingFlags.Static | BindingFlags.NonPublic);
                var key = (Func<string>)factory.Invoke(null, null);
                PlayFabSettings.staticSettings.TitleId = "fixture-title";
                PlayFabSettings.staticPlayer.PlayFabId = "fixture-account";
                string first = key();
                Assert.That(key(), Is.SameAs(first));
                PlayFabSettings.staticSettings.TitleId = "other-fixture-title";
                Assert.That(key(), Is.Not.EqualTo(first));
                PlayFabSettings.staticSettings.TitleId = "fixture-title";
                PlayFabSettings.staticPlayer.PlayFabId = "other-fixture-account";
                Assert.That(key(), Is.Not.EqualTo(first));
            }
            finally
            {
                PlayFabSettings.staticSettings.TitleId = previousTitle;
                PlayFabSettings.staticPlayer.PlayFabId = previousAccount;
            }
        }

        [Test]
        public void ReplacingRepositoryAfterResetPreservesSameAccountLeaseButAllowsOtherAccounts()
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            var replacement = new Owner(shared);
            old.Save();
            old.Repository.ResetSession();
            bool? blocked = null;
            replacement.Save((ok, _) => blocked = ok);
            Assert.That(blocked, Is.False);
            Assert.That(replacement.Success.Count, Is.Zero);
            replacement.Account = "title/b";
            replacement.Save();
            Assert.That(replacement.Success.Count, Is.EqualTo(1));
            old.Success[0]();
            replacement.Account = "TITLE/A";
            replacement.Save();
            Assert.That(replacement.Success.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeoutDoesNotReleaseReplacementUntilActualAcknowledgement()
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            var completions = new List<bool>();
            old.Save((ok, _) => completions.Add(ok));
            old.Save((ok, _) => completions.Add(ok));
            old.Now = 15;
            old.Repository.Tick();
            var replacement = new Owner(shared);
            replacement.Save((ok, _) => completions.Add(ok));
            CollectionAssert.AreEqual(new[] { false, false, false }, completions);
            Assert.That(replacement.Success.Count, Is.Zero);
            old.Success[0]();
            replacement.Save();
            Assert.That(replacement.Success.Count, Is.EqualTo(1));
            Assert.That(completions.Count, Is.EqualTo(3));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ConfirmedRejectionReleasesAccountButUnknownFailureKeepsIt(bool confirmed)
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            old.Save();
            if (confirmed) old.Rejected[0]("confirmed rejection");
            else old.Unknown[0]("unknown outcome");
            old.Repository.ResetSession();
            var replacement = new Owner(shared);
            replacement.Save();
            Assert.That(replacement.Success.Count, Is.EqualTo(confirmed ? 1 : 0));
            if (!confirmed)
            {
                old.Rejected[0]("late confirmed rejection");
                replacement.Save();
                Assert.That(replacement.Success.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void OldDuplicateCannotReleaseLeaseAcquiredByReentrantReplacement()
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            var replacement = new Owner(shared);
            old.Save((_, __) => replacement.Save());
            old.Success[0]();
            Assert.That(replacement.Success.Count, Is.EqualTo(1));
            old.Success[0]();
            old.Rejected[0]("duplicate");
            var third = new Owner(shared);
            third.Save();
            Assert.That(third.Success.Count, Is.Zero);
            replacement.Success[0]();
            third.Save();
            Assert.That(third.Success.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResetCallbackCannotBypassLeaseThroughAnotherRepository()
        {
            var shared = new ProfileWriteCoordinator();
            var old = new Owner(shared);
            var replacement = new Owner(shared);
            old.Save((_, __) => replacement.Save());
            old.Repository.ResetSession();
            Assert.That(replacement.Success.Count, Is.Zero);
            old.Success[0]();
            replacement.Save();
            Assert.That(replacement.Success.Count, Is.EqualTo(1));
        }

        private static ProfileWriteCoordinator Coordinator(NetworkProfileRepository repository)
        {
            object queue = typeof(NetworkProfileRepository).GetField("_requests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(repository);
            return (ProfileWriteCoordinator)queue.GetType().GetField("_writes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);
        }
    }
}
