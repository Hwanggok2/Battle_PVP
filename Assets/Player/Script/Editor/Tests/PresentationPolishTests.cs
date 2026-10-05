using System.Linq;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class PresentationPolishTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(30)] [TestCase(60)] [TestCase(144)]
        public void ConfiguredJumpKeepsItsHeightWithShorterAirtime(int fps)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var fields = new SerializedObject(prefab.GetComponent<PlayerManager>());
            float height = fields.FindProperty("jumpHeight").floatValue, gravity = fields.FindProperty("gravity").floatValue;
            float v = Mathf.Sqrt(2 * gravity * height), apex = v / gravity, y = 0, t = 0;
            Assert.That(height, Is.EqualTo(.8f));
            Assert.That(apex * 2, Is.InRange(.5f, .6f));
            while (t < apex - .000001f)
            {
                float dt = Mathf.Min(1f / fps, apex - t); y += JumpPhysics.Integrate(ref v, gravity, dt); t += dt;
            }
            Assert.That(y, Is.EqualTo(height).Within(.0001f));
            y += JumpPhysics.Integrate(ref v, gravity, apex);
            Assert.That(y, Is.Zero.Within(.0001f));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void JobGuideUsesOnlyTheActualSkillsAndTheirLiveValues(int job)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/JobGuide.prefab"));
            var guide = go.GetComponent<JobGuidePanel>();
            var instanceField = typeof(JobGuidePanel).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = instanceField.GetValue(null);
            try
            {
                EditorTestLifecycle.Invoke(guide, "Awake");
                guide.Select(job);
                var panel = go.transform.Find("Panel");
                var identity = JobGuideContent.IdentityAt(job);
                var skills = (JobSkillData[])typeof(JobGuidePanel).GetField("_skills", Private).GetValue(guide);
                Assert.That(panel.gameObject.activeSelf, Is.False, "The guide must start closed, even if authored open.");
                for (int slot = 0; slot < 2; slot++)
                {
                    bool expected = CombatSkillRules.TrySelect(identity, slot, out var kind);
                    var card = panel.Find("Skill" + slot);
                    Assert.That(card.gameObject.activeSelf, Is.EqualTo(expected));
                    if (!expected) continue;
                    var data = skills.Single(s => s.SkillKind == kind);
                    Assert.That(card.Find("Effect").GetComponent<TMP_Text>().text, Is.EqualTo(SkillDescription.Effect(data)).And.Not.Empty);
                    Assert.That(card.Find("Timing").GetComponent<TMP_Text>().text, Does.Contain($"재사용 {data.CooldownSeconds:0.#}초"));
                    Assert.That(data.UseSfx, Is.Not.Null, "Every equipped skill needs its activation sound.");
                    Assert.That(data.UseSfx.length, Is.GreaterThan(.1f));
                }
            }
            finally { Object.DestroyImmediate(go); instanceField.SetValue(null, previous); }
        }

        [Test]
        public void GuideClassificationMatchesStatAllocationRules()
        {
            var calculator = new IdentityCalculator();
            var stats = new StatContainer(); stats.STR.Invested = 30;
            Assert.That(JobGuideContent.IndexOf(calculator.ResolveIdentity(stats, out _)), Is.Zero);
            stats.STR.Invested = 18; stats.CON.Invested = 6; stats.AGI.Invested = 3; stats.DEF.Invested = 3;
            Assert.That(JobGuideContent.IndexOf(calculator.ResolveIdentity(stats, out _)), Is.EqualTo(4));
            stats.STR.Invested = stats.CON.Invested = 8; stats.AGI.Invested = stats.DEF.Invested = 7;
            Assert.That(JobGuideContent.IndexOf(calculator.ResolveIdentity(stats, out _)), Is.EqualTo(5));
        }

        [Test]
        public void SkillSoundOwnsItsSourceAndUsesDistanceForOtherPlayers()
        {
            var go = new GameObject("Audio ownership test");
            var original = go.AddComponent<AudioSource>();
            var presentation = new CombatSkillPresentation(go, null);
            try
            {
                var source = (AudioSource)typeof(CombatSkillPresentation).GetField("_audioSource", Private).GetValue(presentation);
                Assert.That(source, Is.Not.SameAs(original));
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.maxDistance, Is.EqualTo(24));
                Assert.That(source.dopplerLevel, Is.Zero);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
                var death = new SerializedObject(prefab.GetComponent<CombatAudio>()).FindProperty("_death").objectReferenceValue as AudioClip;
                Assert.That(death, Is.Not.Null);
                Assert.That(death.length, Is.GreaterThan(1));
            }
            finally { presentation.Dispose(); Object.DestroyImmediate(go); }
        }
    }
}
