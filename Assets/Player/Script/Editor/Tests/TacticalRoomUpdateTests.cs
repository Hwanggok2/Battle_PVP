using System.Reflection;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
using BodyPart = BattlePvp.Combat.BodyPart;

namespace BattlePvp.EditorTests
{
    public sealed class TacticalRoomUpdateTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [TestCase(30)] [TestCase(60)] [TestCase(144)]
        public void BallisticLaunchReachesConfiguredHeightAtEveryFrameRate(int fps)
        {
            float velocity = Mathf.Sqrt(2 * 9.81f * .8f), time = 0, y = 0;
            float apexTime = velocity / 9.81f;
            while (time < apexTime - .000001f)
            {
                float dt = Mathf.Min(1f / fps, apexTime - time);
                y += JumpPhysics.Integrate(ref velocity, 9.81f, dt); time += dt;
            }
            Assert.That(y, Is.EqualTo(.8f).Within(.00001f));
            Assert.That(velocity, Is.EqualTo(0).Within(.00001f));
            y += JumpPhysics.Integrate(ref velocity, 9.81f, apexTime);
            Assert.That(y, Is.EqualTo(0).Within(.00001f));
        }
        [Test]
        public void SlowLaunchFrameDoesNotAddUnacceleratedTravel()
        {
            float velocity = Mathf.Sqrt(2 * 9.81f * .8f);
            float y = JumpPhysics.Integrate(ref velocity, 9.81f, 1f / 3f);
            Assert.That(y, Is.LessThan(.8f));
            Assert.That(velocity, Is.LessThan(1f));
        }
        [TestCase(1, 1, false)] [TestCase(2, 2, true)] [TestCase(8, 8, true)]
        [TestCase(9, 2, false)] [TestCase(3, 4, false)]
        public void RoomCapacityCannotEvictOrExceedEight(int capacity, int members, bool allowed) =>
            Assert.That(RoomAdmission.ValidCapacity(capacity, members), Is.EqualTo(allowed));
        [Test]
        public void AdmissionHashIsRoomSpecificAndKeepsWhitespaceInPasswords()
        {
            Assert.That(RoomAdmission.PasswordHash("room", ""), Is.Empty);
            var hash = RoomAdmission.PasswordHash("room", " p@ss ");
            Assert.That(hash.Length, Is.EqualTo(64));
            Assert.That(hash, Is.Not.EqualTo(RoomAdmission.PasswordHash("other", " p@ss ")));
            Assert.That(hash, Is.Not.EqualTo(RoomAdmission.PasswordHash("room", "p@ss")));
        }
        [Test]
        public void ChangingOnlyPrivacyOrCapacityRefreshesExistingRoomRows()
        {
            var state = new RoomListState(); state.SetActive(true);
            var removed = new System.Collections.Generic.List<string>(); var changed = new System.Collections.Generic.List<string>();
            var rooms = new System.Collections.Generic.Dictionary<string, PlayFabBattleManager.RoomInfo>
                { { "room", new PlayFabBattleManager.RoomInfo("Title", "Host", 2) } };
            state.Apply(state.BeginRequest(), rooms, removed, changed);
            rooms["room"] = new PlayFabBattleManager.RoomInfo("Title", "Host", 2, capacity: 4, isPrivate: true);
            state.Apply(state.BeginRequest(), rooms, removed, changed);
            Assert.That(changed, Is.EqualTo(new[] {"room"}));
            Assert.That(state.Rooms["room"].Capacity, Is.EqualTo(4));
            Assert.That(state.Rooms["room"].IsPrivate, Is.True);
        }
        [Test]
        public void MovementRateOnlyDrivesLocomotionStates()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            foreach (var layer in controller.layers)
                foreach (var state in layer.stateMachine.states.Select(s => s.state))
                {
                    bool moving = state.name == "Movement" || state.name == "Crouch Walk";
                    Assert.That(state.speedParameterActive && state.speedParameter == "LocomotionRate", Is.EqualTo(moving), state.name);
                }
        }
        [TestCase(BodyPart.Head, 1.5f)] [TestCase(BodyPart.Body, 1f)] [TestCase(BodyPart.Legs, .8f)]
        public void DummyBodyZonesUseExistingCombatMultipliers(BodyPart part, float multiplier)
        {
            var root = new GameObject("Zone test");
            try { var hit = root.AddComponent<HitBodyPart>(); typeof(HitBodyPart).GetField("_bodyPart", Private).SetValue(hit, part); Assert.That(hit.DamageMultiplier, Is.EqualTo(multiplier)); }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void GlobalVfxRebindsToReplacementLocalPlayerAndIgnoresTheOldPlayer()
        {
            var ui = new GameObject("Global VFX", typeof(RectTransform), typeof(Image)); ui.SetActive(false);
            var one = new GameObject("Old player"); var two = new GameObject("New player");
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Player/Shader/UIIdentityGlitch.shader"));
            UIIdentityGlitchBinder binder = null;
            try
            {
                var a = EditorTestLifecycle.AddNetwork<StatManager>(one); var b = EditorTestLifecycle.AddNetwork<StatManager>(two);
                EditorTestLifecycle.Invoke(a, "Awake"); EditorTestLifecycle.Invoke(b, "Awake");
                var red = new StatContainer(); red.STR.Invested = 30; a.ApplyLocalSceneStats(red);
                var blue = new StatContainer(); blue.DEF.Invested = 30; b.ApplyLocalSceneStats(blue);
                ui.GetComponent<Image>().material = material; binder = ui.AddComponent<UIIdentityGlitchBinder>(); EditorTestLifecycle.Invoke(binder, "Awake");
                EditorTestLifecycle.Invoke(binder, "EnsureRuntimeMaterial");
                var rebind = typeof(UIIdentityGlitchBinder).GetMethod("OnLocalPlayerChanged", Private);
                rebind.Invoke(binder, new object[] { a }); Assert.That(ui.GetComponent<Image>().material.GetColor("_StatColor"), Is.EqualTo(Color.red));
                rebind.Invoke(binder, new object[] { b }); a.ApplyLocalSceneStats(red);
                Assert.That(ui.GetComponent<Image>().material.GetColor("_StatColor"), Is.EqualTo(Color.blue));
            }
            finally
            {
                if (binder != null)
                {
                    EditorTestLifecycle.Invoke(binder, "UnsubscribeSources");
                    var field = typeof(UIIdentityGlitchBinder).GetField("_runtimeMaterial", Private); var clone = field.GetValue(binder) as Material; field.SetValue(binder, null); if (clone != null) Object.DestroyImmediate(clone);
                }
                Object.DestroyImmediate(ui); Object.DestroyImmediate(one); Object.DestroyImmediate(two); Object.DestroyImmediate(material);
            }
        }
    }
}
