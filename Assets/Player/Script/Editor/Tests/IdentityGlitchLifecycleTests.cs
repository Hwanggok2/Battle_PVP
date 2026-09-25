using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class IdentityGlitchLifecycleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> _sources = new List<GameObject>();
        private GameObject _root;
        private UIIdentityGlitchBinder _binder;
        private IdentityGlitchSourceStub _source;
        private Material _baseMaterial;

        [SetUp]
        public void SetUp()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Player/Shader/UIIdentityGlitch.shader");
            Assert.That(shader, Is.Not.Null, "Use the actual material properties consumed by the player UI.");
            _baseMaterial = new Material(shader);
            _root = new GameObject("Identity glitch lifecycle fixture", typeof(RectTransform));
            _root.SetActive(false);
            _root.AddComponent<Image>().material = _baseMaterial;
            _binder = _root.AddComponent<UIIdentityGlitchBinder>();
            _source = AddSource<IdentityGlitchSourceStub>();
            SetSources(_source);
        }

        [TearDown]
        public void TearDown()
        {
            _root.SetActive(false);
            Material runtime = Get<Material>("_runtimeMaterial");
            Set("_runtimeMaterial", null);
            _root.GetComponent<Image>().material = null;
            // The production component defers destruction; EditMode fixtures release their clones immediately.
            if (runtime != null) UnityEngine.Object.DestroyImmediate(runtime);
            UnityEngine.Object.DestroyImmediate(_root);
            foreach (GameObject source in _sources)
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            _sources.Clear();
            UnityEngine.Object.DestroyImmediate(_baseMaterial);
        }

        [Test]
        public void FirstEnableReadsLowHealthWithoutWaitingForAnEvent()
        {
            _source.SetSnapshot(25f, 100f);
            _root.SetActive(true);
            AssertMaterial(8.5f, 0f);
            AssertSubscribers(_source, 1);
        }

        [TestCase(15f, 100f, 100f, 100f, 4f, 0f)]
        [TestCase(100f, 100f, 20f, 100f, 8.8f, 0f)]
        [TestCase(150f, 100f, 20f, 100f, 8.8f, 0f)]
        [TestCase(20f, 100f, 150f, 100f, 4f, 0.5f)]
        [TestCase(75f, 100f, 75f, 200f, 7.75f, 0f)]
        [TestCase(100f, 200f, 100f, 50f, 4f, 1f)]
        public void ReenableRestoresHealthAndOverflowChangedWhileUnsubscribed(
            float previousHp, float previousMax, float currentHp, float currentMax, float pulse, float overlap)
        {
            _source.SetSnapshot(previousHp, previousMax);
            _root.SetActive(true);
            _root.SetActive(false);
            AssertSubscribers(_source, 0);
            _source.SetSnapshot(currentHp, currentMax);
            _root.SetActive(true);
            AssertMaterial(pulse, overlap);
            AssertSubscribers(_source, 1);
        }

        [Test]
        public void ActiveHealthEventsUseTheSamePulseRuleAsTheInitialSnapshot()
        {
            _root.SetActive(true);
            _source.Publish(75f, 200f);
            AssertMaterial(7.75f, 0f);
            _source.Publish(150f, 100f);
            AssertMaterial(4f, 0.5f);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void InvalidMaximumRestoresNormalPulseAndClearsOldOverflow(float maximum)
        {
            _source.SetSnapshot(150f, 100f);
            _root.SetActive(true);
            _root.SetActive(false);
            _source.SetSnapshot(10f, maximum);
            _root.SetActive(true);
            AssertMaterial(4f, 0f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingReaderRestoresDefaultsAfterAPreviousHealthEvent(bool statusWithoutReader)
        {
            _root.SetActive(true);
            _source.Publish(20f, 100f);
            _source.PublishOverflow(true, 0.5f);
            _root.SetActive(false);
            Set("_statusSourceBehaviour", statusWithoutReader ? AddSource<IdentityGlitchStatusOnlyStub>() : null);
            // Exercise the snapshot without scene-wide auto-resolution selecting another open scene's player.
            Invoke("RefreshSourceInterfaces");
            Invoke("PullCurrentHpFromReader");
            Assert.That(Get<IDamageReceiver>("_hpReader"), Is.Null);
            AssertMaterial(4f, 0f);
        }

        [Test]
        public void SourceReplacementUnsubscribesTheOriginalAndReenablesExactlyOnce()
        {
            _root.SetActive(true);
            IdentityGlitchSourceStub replacement = AddSource<IdentityGlitchSourceStub>();
            replacement.SetSnapshot(25f, 100f);
            // Serialized references can change before OnDisable; unsubscription must use the bound interfaces.
            SetSources(replacement);
            for (int i = 0; i < 10; i++)
            {
                _root.SetActive(false);
                AssertSubscribers(_source, 0);
                AssertSubscribers(replacement, 0);
                _root.SetActive(true);
                AssertSubscribers(replacement, 1);
                _source.Publish(150f, 100f);
                AssertMaterial(8.5f, 0f);
            }
        }

        [Test]
        public void DestroyedReaderIsNotKeptAliveThroughAnInterfaceReference()
        {
            _source.SetSnapshot(20f, 100f);
            _root.SetActive(true);
            UnityEngine.Object.DestroyImmediate(_source.gameObject);
            _root.SetActive(false);
            AssertSubscribers(_source, 0);
            Invoke("RefreshSourceInterfaces");
            Assert.That(Get<IPlayerStatusSource>("_statusSource"), Is.Null);
            Assert.That(Get<IDamageReceiver>("_hpReader"), Is.Null);
            Invoke("PullCurrentHpFromReader");
            AssertMaterial(4f, 0f);
            IdentityGlitchSourceStub replacement = AddSource<IdentityGlitchSourceStub>();
            replacement.SetSnapshot(150f, 100f);
            SetSources(replacement);
            _root.SetActive(true);
            AssertMaterial(4f, 0.5f);
            AssertSubscribers(replacement, 1);
        }

        private T AddSource<T>() where T : MonoBehaviour
        {
            var source = new GameObject(typeof(T).Name);
            _sources.Add(source);
            return source.AddComponent<T>();
        }

        private void SetSources(IdentityGlitchSourceStub source)
        {
            Set("_identitySourceBehaviour", source);
            Set("_statusSourceBehaviour", source);
        }

        private void AssertMaterial(float pulse, float overlap)
        {
            Material runtime = Get<Material>("_runtimeMaterial");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.GetFloat("_EmissionPulse"), Is.EqualTo(pulse).Within(0.0001f));
            Assert.That(runtime.GetFloat("_OverlapPercent"), Is.EqualTo(overlap).Within(0.0001f));
        }

        private static void AssertSubscribers(IdentityGlitchSourceStub source, int expected)
        {
            Assert.That(source.IdentitySubscribers, Is.EqualTo(expected));
            Assert.That(source.HpSubscribers, Is.EqualTo(expected));
            Assert.That(source.OverflowSubscribers, Is.EqualTo(expected));
        }

        private void Set(string field, object value) => typeof(UIIdentityGlitchBinder).GetField(field, Private).SetValue(_binder, value);
        private T Get<T>(string field) => (T)typeof(UIIdentityGlitchBinder).GetField(field, Private).GetValue(_binder);
        private void Invoke(string method) => typeof(UIIdentityGlitchBinder).GetMethod(method, Private).Invoke(_binder, null);
    }

    public sealed class IdentityGlitchSourceStub : MonoBehaviour, IIdentitySource, IPlayerStatusSource, IDamageReceiver
    {
        public event Action<Identity> IdentityChanged;
        public event Action<float, float> HpChanged;
        public event Action<bool, float> OverflowChanged;
        public Identity CurrentIdentity => new Identity(IdentityType.Monostat, StatKind.STR);
        public float CurrentHp { get; private set; } = 100f;
        public float MaxHp { get; private set; } = 100f;
        public int IdentitySubscribers => IdentityChanged?.GetInvocationList().Length ?? 0;
        public int HpSubscribers => HpChanged?.GetInvocationList().Length ?? 0;
        public int OverflowSubscribers => OverflowChanged?.GetInvocationList().Length ?? 0;
        public void SetSnapshot(float current, float maximum) { CurrentHp = current; MaxHp = maximum; }
        public void Publish(float current, float maximum)
        {
            SetSnapshot(current, maximum);
            HpChanged?.Invoke(current, maximum);
            bool overflow = maximum > 0f && current > maximum;
            OverflowChanged?.Invoke(overflow, overflow ? Mathf.Clamp01((current - maximum) / maximum) : 0f);
        }
        public void PublishOverflow(bool active, float overlap) => OverflowChanged?.Invoke(active, overlap);
        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition) => Publish(CurrentHp - amount, MaxHp);
    }

    public sealed class IdentityGlitchStatusOnlyStub : MonoBehaviour, IPlayerStatusSource
    {
        public event Action<float, float> HpChanged { add { } remove { } }
        public event Action<bool, float> OverflowChanged { add { } remove { } }
    }
}
