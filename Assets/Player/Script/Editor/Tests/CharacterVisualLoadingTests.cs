using System.Collections;
using System.Reflection;
using BattlePvp.Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterVisualLoadingTests
    {
        private GameObject _owner;
        private CharacterDefinition _definition;
        private CharacterVisualLoadTestRunner _runner;
        private static readonly FieldInfo RequestField = typeof(CharacterDefinition)
            .GetField("_visualRequest", BindingFlags.Instance | BindingFlags.NonPublic);

        private void CreateLoader()
        {
            _definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            _definition.SetVisualResource("CharacterVisuals/megumi");
            _owner = new GameObject("Concurrent character loading test");
            _runner = _owner.AddComponent<CharacterVisualLoadTestRunner>();
        }

        [UnityTest]
        public IEnumerator ConcurrentCallersShareOneRequestAndBothComplete()
        {
            yield return new EnterPlayMode();
            CreateLoader();
            var first = new LoadOutcome(); var second = new LoadOutcome();
            _runner.StartCoroutine(LoadAndRecord(_definition, first));
            var sharedRequest = (ResourceRequest)RequestField.GetValue(_definition);
            Assert.That(sharedRequest, Is.Not.Null);
            Assert.That(sharedRequest.isDone, Is.False, "Both callers must start while the resource is still loading.");
            _runner.StartCoroutine(LoadAndRecord(_definition, second));
            Assert.That(RequestField.GetValue(_definition), Is.SameAs(sharedRequest));

            yield return WaitUntilComplete(first, second);
            Assert.That(first.Visual, Is.Not.Null);
            Assert.That(second.Visual, Is.SameAs(first.Visual));
            Assert.That(_definition.IsVisualLoaded, Is.True);
            Assert.That(RequestField.GetValue(_definition), Is.Null);
            var cached = new LoadOutcome();
            _runner.StartCoroutine(LoadAndRecord(_definition, cached));
            Assert.That(RequestField.GetValue(_definition), Is.Null, "Reusing a loaded visual must not start another resource request.");
            yield return WaitUntilComplete(cached, first);
            Assert.That(cached.Visual, Is.SameAs(first.Visual));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CancellingOneWaiterDoesNotBlockAnotherOrItsReplacement()
        {
            yield return new EnterPlayMode();
            CreateLoader();
            var cancelledOutcome = new LoadOutcome(); var other = new LoadOutcome(); var replacement = new LoadOutcome();
            var cancelled = _runner.StartCoroutine(LoadAndRecord(_definition, cancelledOutcome));
            var sharedRequest = (ResourceRequest)RequestField.GetValue(_definition);
            _runner.StartCoroutine(LoadAndRecord(_definition, other));
            _runner.StopCoroutine(cancelled);
            _runner.StartCoroutine(LoadAndRecord(_definition, replacement));
            Assert.That(RequestField.GetValue(_definition), Is.SameAs(sharedRequest));

            yield return WaitUntilComplete(other, replacement);
            Assert.That(cancelledOutcome.Completed, Is.False);
            Assert.That(_definition.IsVisualLoaded, Is.True);
            Assert.That(_definition.VisualPrefab, Is.Not.Null);
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class LoadOutcome
        {
            public bool Completed;
            public GameObject Visual;
        }

        private static IEnumerator LoadAndRecord(CharacterDefinition definition, LoadOutcome outcome)
        {
            yield return definition.LoadVisualAsync();
            outcome.Visual = definition.VisualPrefab;
            outcome.Completed = true;
        }

        private static IEnumerator WaitUntilComplete(LoadOutcome first, LoadOutcome second)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while ((!first.Completed || !second.Completed) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(first.Completed && second.Completed, Is.True, "All active visual-loading coroutines must finish.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_owner != null) { _owner.SetActive(false); Object.Destroy(_owner); }
            if (_definition != null) Object.Destroy(_definition);
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }

    public sealed class CharacterVisualLoadTestRunner : MonoBehaviour { }
}
