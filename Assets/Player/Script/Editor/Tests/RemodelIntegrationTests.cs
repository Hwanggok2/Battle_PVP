using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class RemodelIntegrationTests
    {
        [Test]
        public void DamagedPreferencesCannotProduceInvalidRenderingOrDuplicateReservedKeys()
        {
            var settings = new LocalGameSettingsData
            {
                quality = 100, fps = -1, brightness = float.NaN, hudScale = float.PositiveInfinity,
                hudOpacity = -5, sensitivity = 30, master = float.NaN, effects = -1,
                skill1 = "w", skill2 = "q"
            };
            settings.Sanitize();
            Assert.That(settings.quality, Is.EqualTo(2));
            Assert.That(settings.fps, Is.EqualTo(60));
            Assert.That(settings.brightness, Is.EqualTo(1));
            Assert.That(settings.hudScale, Is.EqualTo(1));
            Assert.That(settings.hudOpacity, Is.EqualTo(.35f));
            Assert.That(settings.sensitivity, Is.EqualTo(2));
            Assert.That(settings.master, Is.EqualTo(.65f));
            Assert.That(settings.effects, Is.Zero);
            Assert.That(settings.skill1, Is.EqualTo("q"));
            Assert.That(settings.skill2, Is.EqualTo("e"));
            var draft = settings.Copy(); draft.skill1 = "e"; draft.Sanitize();
            Assert.That(draft.skill2, Is.EqualTo("q"));
            Assert.That(settings.skill1, Is.EqualTo("q"), "Editing a draft must not mutate the saved settings.");
        }

        [Test]
        public void DirectSkillActionsUseDistinctKeysAndHaveNoMouseWheelBinding()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Player/Script/PlayerInputActions.inputactions");
            Assert.That(actions.FindAction("Skill1").bindings[0].path, Is.EqualTo("<Keyboard>/q"));
            Assert.That(actions.FindAction("Skill2").bindings[0].path, Is.EqualTo("<Keyboard>/e"));
            foreach (var action in new[] { actions.FindAction("Skill1"), actions.FindAction("Skill2") })
                foreach (var binding in action.bindings) Assert.That(binding.path, Does.Not.Contain("scroll"));
        }

        [Test]
        public void TwoSlotHudReadsIndependentCooldownsWithoutChangingSelectedSkill()
        {
            var owner = new GameObject("Independent skill HUD test"); owner.SetActive(false);
            var roll = ScriptableObject.CreateInstance<JobSkillData>();
            var swap = ScriptableObject.CreateInstance<JobSkillData>();
            try
            {
                var stats = EditorTestLifecycle.AddNetwork<StatManager>(owner);
                var combat = owner.AddComponent<PlayerCombat>();
                EditorTestLifecycle.BindNetwork(owner);
                Set(combat, "_statManager", stats); Set(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
                typeof(StatManager).GetProperty(nameof(StatManager.CurrentIdentity)).SetValue(stats, new Identity(IdentityType.Polymath, StatKind.STR));
                Set(roll, "_skillKind", JobSkillKind.PolymathRoll); Set(swap, "_skillKind", JobSkillKind.PolymathWeaponSwap);
                Set(combat, "_polymathRollSkillData", roll); Set(combat, "_polymathWeaponSwapSkillData", swap);
                Set(combat, "_selectedSkillIndex", 1);
                var cooldowns = (Dictionary<int,double>)Get(combat,"_offlineAdvancedCooldownUntil");
                cooldowns[(int)JobSkillKind.PolymathRoll] = Time.timeAsDouble + 10;
                Assert.That(combat.GetSkillHudState(0).Phase, Is.EqualTo(SkillHudPhase.Cooldown));
                Assert.That(combat.GetSkillHudState(1).Phase, Is.EqualTo(SkillHudPhase.Ready));
                Assert.That(combat.GetSkillHudState(0).SelectedIndex, Is.Zero);
                Assert.That(combat.GetSkillHudState(1).SelectedIndex, Is.EqualTo(1));
                Assert.That(Get(combat,"_selectedSkillIndex"), Is.EqualTo(1));
                Assert.That(combat.GetSkillHudState(-1).Visible, Is.False);
                Assert.That(combat.GetSkillHudState(2).Visible, Is.False);
                combat.UseSkillSlot(2);
                Assert.That(Get(combat,"_selectedSkillIndex"), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(roll); Object.DestroyImmediate(swap); }
        }

        [TestCase("waiting")]
        [TestCase("arena")]
        [TestCase("foundry")]
        public void AllEightSpawnVolumesAreAboveFloorAndClearOfMapCover(string map)
        {
            var data = JObject.Parse(File.ReadAllText("Assets/Remodel/Editor/Data/" + map + ".json"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/" + map + ".prefab");
            var root = Object.Instantiate(prefab);
            try
            {
                Assert.That(data["spawns"].Count(), Is.EqualTo(8));
                foreach (var token in data["spawns"])
                {
                    var p = new Vector3((float)token[0], (float)token[1], (float)token[2]);
                    // Conservative body envelope, including normal lateral clearance.
                    var body = new Bounds(p + Vector3.up, new Vector3(.8f, 1.9f, .8f));
                    foreach (var collider in root.GetComponentsInChildren<BoxCollider>())
                    {
                        var bounds = new Bounds(collider.transform.TransformPoint(collider.center), Vector3.Scale(collider.size,collider.transform.lossyScale));
                        Assert.That(body.Intersects(bounds), Is.False, map + " spawn " + p + " overlaps " + collider.name + " at " + bounds.center);
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        public void LateJoinSnapshotActivatesOnlyTheHostSelectedMap(byte selected)
        {
            var server = new GameObject("Map server fixture"); server.SetActive(false);
            var client = new GameObject("Map client fixture"); client.SetActive(false);
            var research = new GameObject("Research"); var roof = new GameObject("Roof");
            var spire = new GameObject("Spire"); spire.transform.SetParent(client.transform);
            research.transform.SetParent(client.transform); roof.transform.SetParent(client.transform);
            try
            {
                var from = EditorTestLifecycle.AddNetwork<BattlePvp.Networking.BattleMapSelection>(server);
                var to = EditorTestLifecycle.AddNetwork<BattlePvp.Networking.BattleMapSelection>(client);
                Set(from,"_selected",selected); Set(to,"_research",research); Set(to,"_rooftop",roof);
                Set(to,"_spire",spire); Set(from,"_roomTitle","테스트 전장"); Set(from,"_roomCapacity",4); Set(from,"_privateRoom",true);
                Set(from,"_matchSeconds", selected == 0 ? 300 : 600);
                var writer = new Mirror.NetworkWriter(); from.OnSerialize(writer,true);
                to.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true);
                to.OnStartClient();
                Assert.That(to.Selected, Is.EqualTo(selected));
                Assert.That(to.MatchSeconds, Is.EqualTo(selected == 0 ? 300 : 600), "Late joins receive the selected match duration with the map.");
                Assert.That(research.activeSelf, Is.EqualTo(selected==0));
                Assert.That(roof.activeSelf, Is.EqualTo(selected==1));
                Assert.That(spire.activeSelf, Is.EqualTo(selected==2));
                Assert.That(to.RoomTitle, Is.EqualTo("테스트 전장")); Assert.That(to.RoomCapacity, Is.EqualTo(4)); Assert.That(to.PrivateRoom, Is.True);
            }
            finally { Object.DestroyImmediate(server); Object.DestroyImmediate(client); }
        }

        private static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static object Get(object target,string field) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    }
}
