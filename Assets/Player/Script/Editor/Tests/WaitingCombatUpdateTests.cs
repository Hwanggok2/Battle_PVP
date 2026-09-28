using System.Linq;
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
    public sealed class WaitingCombatUpdateTests
    {
        [Test]
        public void RoomEntryAllowsAnUnconfiguredSwapPresetWhenTheMainAllocationIsComplete()
        {
            var field = typeof(BattlePvp.Managers.GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = field.GetValue(null);
            var profileRoot = new GameObject("Profile default fixture");
            var uiRoot = new GameObject("Room entry fixture");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var profile = profileRoot.AddComponent<BattlePvp.Managers.GlobalDataManager>(); field.SetValue(null, profile);
                typeof(BattlePvp.Managers.GlobalDataManager).GetField("_hasLoadedPlayerStats", flags).SetValue(profile, true);
                typeof(BattlePvp.Managers.GlobalDataManager).GetField("_hasLoadedCombatRecord", flags).SetValue(profile, true);
                typeof(BattlePvp.Managers.GlobalDataManager).GetField("_savedStats", flags).SetValue(profile, CombatPresetPlan.DefaultTarget(default));
                var ui = uiRoot.AddComponent<LobbyUIManager>();
                Assert.That(profile.HasStrategistTargetPreset, Is.False);
                Assert.That(typeof(LobbyUIManager).GetMethod("CanStartRoomFlow", flags).Invoke(ui, null), Is.True);
            }
            finally { Object.DestroyImmediate(profileRoot); Object.DestroyImmediate(uiRoot); field.SetValue(null, previous); }
        }

        [Test]
        public void MatchSettingsRejectInvalidDurationsChangesDuringCountdownAndChangesOutsideWaiting()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            string original = scene.name; scene.name = "Battle_waiting";
            var root = new GameObject("Match settings fixture");
            var server = typeof(Mirror.NetworkServer).GetProperty("active"); bool previous = Mirror.NetworkServer.active;
            var starting = typeof(BattleStartController).GetField("<IsStarting>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previousStart = starting.GetValue(null);
            try
            {
                var settings = EditorTestLifecycle.AddNetwork<BattlePvp.Networking.BattleMapSelection>(root);
                server.SetValue(null, false);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[Server\\].*SetMatchDuration.*not active"));
                settings.SetMatchDuration(600); Assert.That(settings.MatchSeconds, Is.EqualTo(180), "Clients cannot mutate room settings.");
                server.SetValue(null, true); starting.SetValue(null, false);
                settings.SetMatchDuration(300); Assert.That(settings.MatchSeconds, Is.EqualTo(300));
                settings.SetMatchDuration(301); Assert.That(settings.MatchSeconds, Is.EqualTo(300));
                starting.SetValue(null, true);
                settings.SetMatchDuration(600); Assert.That(settings.MatchSeconds, Is.EqualTo(300));
                starting.SetValue(null, false); scene.name = "Battle";
                settings.SetMatchDuration(600); Assert.That(settings.MatchSeconds, Is.EqualTo(300));
            }
            finally { server.SetValue(null, previous); starting.SetValue(null, previousStart); Object.DestroyImmediate(root); scene.name = original; }
        }
        [Test]
        public void HoldCancelsOnLookAwayAndReleaseAndRequiresReleaseBeforeReopening()
        {
            var hold = new TerminalHold();
            Assert.That(hold.Step(true, true, .5f), Is.False);
            hold.Step(false, true, .1f); Assert.That(hold.Progress, Is.Zero);
            Assert.That(hold.Step(true, true, .3f), Is.False);
            hold.Step(true, false, .1f); Assert.That(hold.Progress, Is.Zero);
            Assert.That(hold.Step(true, true, .75f), Is.True);
            Assert.That(hold.Step(true, true, 10), Is.False);
            hold.Step(false, true, .1f);
            Assert.That(hold.Step(true, true, 10), Is.False, "Closing while E remains held must not reopen the menu.");
            hold.Step(true, false, .1f);
            Assert.That(hold.Step(true, true, .75f), Is.True);
        }

        [TestCase("Battle_waiting", true)] [TestCase("Battle", true)] [TestCase("Lobby", true)] [TestCase("Login", false)]
        public void SkillsAreVisibleInWaitingAndBattle(string scene, bool expected) => Assert.That(SkillArcHud.IsVisibleScene(scene), Is.EqualTo(expected));

        [Test]
        public void DefaultAllocationIsCompleteStrategistAndPreservesEquipmentAndSwapBack()
        {
            var current = new StatContainer { STR = new StatSlot { Invested = 3, Item = 2 }, AGI = new StatSlot { Invested = 18, Item = 4 },
                CON = new StatSlot { Invested = 6, Item = 1 }, DEF = new StatSlot { Invested = 3, Item = 3 } };
            var target = CombatPresetPlan.ResolveUnconfiguredTarget(default, current);
            Assert.That(StatValidation.TryValidateClientStats(target, current, out _), Is.True);
            Assert.That(target.STR.Invested + target.CON.Invested + target.AGI.Invested + target.DEF.Invested, Is.EqualTo(30));
            Assert.That(new IdentityCalculator().ResolveIdentity(target, out _).Type, Is.EqualTo(IdentityType.Strategist));
            Assert.That(CombatPresetPlan.TryCreate(true, current, target, default, false, out var first), Is.True);
            Assert.That(CombatPresetPlan.TryCreate(true, target, target, first.ReturnPreset, first.HasReturnPreset, out var second), Is.True);
            Assert.That(second.Target, Is.EqualTo(current));
        }

        [Test]
        public void ConfiguredAllocationWinsAndInvalidAllocationsAreNotSilentlyReplaced()
        {
            var current = CombatPresetPlan.DefaultTarget(default); current.STR.Item = 3;
            var configured = current; configured.STR.Invested = 4; configured.DEF.Invested = 17; configured.STR.Item = 0;
            var target = CombatPresetPlan.ResolveUnconfiguredTarget(configured, current);
            Assert.That(target.STR.Invested, Is.EqualTo(4)); Assert.That(target.DEF.Invested, Is.EqualTo(17));
            Assert.That(target.STR.Item, Is.EqualTo(3));
            configured.STR.Invested = float.NaN;
            Assert.That(StatValidation.IsValidPreset(CombatPresetPlan.ResolveUnconfiguredTarget(configured, current)), Is.False);
        }

        [TestCase(StatKind.STR)] [TestCase(StatKind.AGI)] [TestCase(StatKind.CON)] [TestCase(StatKind.DEF)]
        public void MonostatTrailUsesTheExistingPalette(StatKind stat)
        {
            Color expected = stat switch { StatKind.STR => Color.red, StatKind.AGI => Color.green, StatKind.CON => Color.yellow, _ => Color.blue };
            Assert.That(StatVfxColor.Resolve(new Identity(IdentityType.Monostat, stat), default,
                Color.red, Color.green, Color.yellow, Color.blue), Is.EqualTo(expected));
        }

        [Test]
        public void MixedIdentityColorsUseTheSameInvestmentWeightsAsTheScreenVfx()
        {
            var stats = CombatPresetPlan.DefaultTarget(default);
            Color strategist = StatVfxColor.Resolve(new Identity(IdentityType.Strategist, StatKind.STR), stats,
                Color.red, Color.green, new Color(1, 1, 0), Color.blue);
            Assert.That(strategist.r, Is.EqualTo(1).Within(.0001)); Assert.That(strategist.g, Is.EqualTo(.25).Within(.0001));
            Assert.That(strategist.b, Is.Zero);
            stats.STR.Invested = stats.AGI.Invested = stats.CON.Invested = stats.DEF.Invested = 7.5f;
            var polymath = StatVfxColor.Resolve(new Identity(IdentityType.Polymath, StatKind.STR), stats,
                Color.red, Color.green, new Color(1, 1, 0), Color.blue);
            Assert.That(polymath.r, Is.EqualTo(1)); Assert.That(polymath.g, Is.EqualTo(1)); Assert.That(polymath.b, Is.EqualTo(.5f));
        }

        [TestCase("Assets/Prefabs/Player.prefab")] [TestCase("Assets/Prefabs/UI_Root.prefab")]
        public void BothSkillIconsFillTheSameCircleAndHaveTooltips(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var arc = prefab.GetComponentInChildren<SkillArcHud>(true);
            Assert.That(arc, Is.Not.Null);
            var slots = arc.GetComponentsInChildren<SkillUI>(true); Assert.That(slots.Length, Is.EqualTo(2));
            foreach (var slot in slots)
            {
                var icon = (Image)new SerializedObject(slot).FindProperty("_baseImage").objectReferenceValue;
                Assert.That(icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(88, 88)));
                Assert.That(icon.preserveAspect, Is.False);
                Assert.That(icon.GetComponentInParent<Mask>(true), Is.Not.Null);
                Assert.That(slot.GetComponent<SkillTooltip>(), Is.Not.Null);
            }
        }

        [Test]
        public void EveryAssignedSkillHasDescriptionAndAllCombatClipsAreLinked()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var combat = player.GetComponent<PlayerCombat>();
            foreach (var field in typeof(PlayerCombat).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.FieldType == typeof(JobSkillData)))
            {
                var skill = (JobSkillData)field.GetValue(combat);
                if (skill == null) continue;
                Assert.That(SkillDescription.Build(skill), Does.Contain(skill.DisplayName).And.Contain("재사용"));
            }
            var so = new SerializedObject(player.GetComponent<CombatAudio>());
            foreach (string field in new[] { "_swings", "_hits" })
            {
                var array = so.FindProperty(field); Assert.That(array.arraySize, Is.GreaterThanOrEqualTo(2));
                for (int i = 0; i < array.arraySize; i++)
                {
                    var clip = (AudioClip)array.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.That(clip, Is.Not.Null); Assert.That(clip.length, Is.GreaterThan(.1f));
                }
            }
            Assert.That(so.FindProperty("_death").objectReferenceValue, Is.Not.Null);
        }
    }
}
