using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using BattlePvp.Combat;
using BattlePvp.EditorData;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class SkillExpansionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _player;
        ExpandedSkillController _skills;
        StatManager _stats;
        HealthSystem _health;

        [SetUp] public void Setup()
        {
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(_player);
            _stats = _player.GetComponent<StatManager>();
            _health = _player.GetComponent<HealthSystem>();
            _skills = _player.GetComponent<ExpandedSkillController>();
            EditorTestLifecycle.Invoke(_stats, "Awake");
            EditorTestLifecycle.Invoke(_health, "Awake");
            EditorTestLifecycle.Invoke(_player.GetComponent<PlayerCombat>(), "Awake");
            EditorTestLifecycle.Invoke(_player.GetComponent<SkillLoadout>(), "Awake");
            EditorTestLifecycle.Invoke(_skills, "Awake");
            Set(_health, "_maxHp", 100f); Set(_health, "_currentHp", 100f);
            foreach (int kind in SkillLoadout.Defaults()) _player.GetComponent<SkillLoadout>().Choices.Add(kind);
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(_player); }
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target,args);
        void Active(JobSkillKind kind) => _skills.States[(int)kind] = new SkillRuntime { ActiveUntil = _skills.Now + 10 };
        void Equip(int job, JobSkillKind kind)
        {
            typeof(StatManager).GetProperty(nameof(StatManager.CurrentIdentity)).SetValue(_stats, UI.JobGuideContent.IdentityAt(job));
            _player.GetComponent<SkillLoadout>().Choices[job*2] = (int)kind;
            Set(_skills, "_nextUse", 0d);
        }

        [Test] public void WorkbooksImportEveryActiveSkillAndValidateStrings()
        {
            var imported = SkillWorkbookImporter.Parse("GameData");
            try
            {
                Assert.That(imported.Skills, Has.Length.EqualTo(21));
                Assert.That(imported.Skills.Count(s=>s.Kind>=100), Is.EqualTo(13));
                Assert.That(imported.Jobs, Has.Length.EqualTo(6));
                Assert.That(imported.Pools, Has.Length.EqualTo(23));
                foreach (var row in imported.Strings) { SkillWorkbookImporter.ValidateFormat(row.Content_Kor,row.FormatArgCount); SkillWorkbookImporter.ValidateFormat(row.Content_Eng,row.FormatArgCount); }
                foreach (var row in imported.Skills)
                {
                    var presentation = SkillPresentationCatalog.Data(row.Kind);
                    Assert.That(presentation, Is.Not.Null, row.Id);
                    Assert.That(presentation.UsesGameData, Is.True, row.Id);
                    Assert.That(presentation.IconSprite, Is.Not.Null, row.Id);
                    Assert.That(presentation.UseSfx, Is.Not.Null, row.Id);
                    Assert.That(SkillGameData.Description((JobSkillKind)row.Kind), Is.Not.Empty);
                }
            }
            finally { Object.DestroyImmediate(imported); }
        }
        [TestCase(JobSkillKind.Berserk)]
        [TestCase(JobSkillKind.Recovery)] [TestCase(JobSkillKind.Fortify)]
        public void BuffBodyGlowReusesTheAnimatedSkinAndHonorsConcealmentAndExpiry(JobSkillKind kind)
        {
            using var aura = new SkillBuffAura();
            Active(kind); aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Skill buff body glow");
            var body = glow.transform.parent.GetComponent<SkinnedMeshRenderer>();
            Assert.That(glow.enabled, Is.True);
            Assert.That(glow.sharedMesh, Is.SameAs(body.sharedMesh));
            Assert.That(glow.bones, Is.EqualTo(body.bones));
            Assert.That(glow.GetComponents<Collider>(), Is.Empty);
            aura.Tick(_skills, true); Assert.That(glow.enabled, Is.False, "remote stealth must not reveal the buff");
            aura.Tick(_skills, false); Assert.That(glow.enabled, Is.True);
            _skills.States.Clear(); aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
            Active(kind); aura.Tick(_skills, false);
            Assert.That(_player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r.name == "Skill buff body glow"), Is.EqualTo(1));
            Set(_health, "_isDead", true); aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
        }
        [Test] public void FortifyKeepsBluePriorityAndOtherBuffRemainsAfterItsExpiry()
        {
            using var aura = new SkillBuffAura(); Active(JobSkillKind.Berserk); Active(JobSkillKind.Fortify);
            aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
            var properties = new MaterialPropertyBlock(); glow.GetPropertyBlock(properties);
            Color color = properties.GetColor("_BaseColor"); Assert.That(color.b, Is.GreaterThan(color.r));
            _skills.States.Remove((int)JobSkillKind.Fortify); aura.Tick(_skills, false); glow.GetPropertyBlock(properties);
            color = properties.GetColor("_BaseColor"); Assert.That(glow.enabled, Is.True); Assert.That(color.r, Is.GreaterThan(color.b));
            aura.Dispose(); aura.Tick(_skills, false);
            Assert.That(_player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r.name == "Skill buff body glow"), Is.EqualTo(1));
        }
        [Test] public void DiceReductionAndBerserkRecoveryPenaltyArePurpleAndExpire()
        {
            using var aura = new SkillBuffAura(); Active(JobSkillKind.Dice); Set(_skills, "_diceFace", 6); aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
            Assert.That(glow.enabled, Is.True); Set(_skills, "_diceFace", 1); aura.Tick(_skills, false);
            var properties = new MaterialPropertyBlock(); glow.GetPropertyBlock(properties);
            Color tint = properties.GetColor("_BaseColor");
            Assert.That(glow.enabled, Is.True); Assert.That(tint.r > tint.g && tint.b > tint.g, Is.True);
            _skills.States.Clear(); aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
            _skills.States[(int)JobSkillKind.Berserk] = new SkillRuntime { CooldownUntil = _skills.Now + 20 };
            aura.Tick(_skills, false); glow.GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(tint));
            _skills.States.Clear(); aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
        }
        [Test] public void PresetShieldIsYellowWithoutStatGlowAndHidesWhenConsumedOrConcealed()
        {
            using var defense = new DefenseSkillVfx(); using var aura = new SkillBuffAura();
            Set(_health, "_currentShield", 30f); Set(_health, "_skillInvulnerableUntil", Mirror.NetworkTime.time + 10);
            defense.Tick(_skills, false); aura.Tick(_skills, false);
            var shield = _player.transform.Find("Preset yellow shield").gameObject;
            var tint = shield.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
            Assert.That(shield.activeSelf, Is.True); Assert.That(tint.r > 1 && tint.g > 1 && tint.b < .1f, Is.True);
            Assert.That(_player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "Skill buff body glow" && r.enabled), Is.False);
            Active(JobSkillKind.Fortify); aura.Tick(_skills, false);
            Assert.That(_player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.name == "Skill buff body glow" && r.enabled), Is.True);
            defense.Tick(_skills, true); Assert.That(shield.activeSelf, Is.False);
            Active(JobSkillKind.Stealth); defense.Tick(_skills, false);
            Assert.That(shield.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor").a, Is.EqualTo(.095f));
            Set(_health, "_currentShield", 0f); defense.Tick(_skills, false); Assert.That(shield.activeSelf, Is.False);
            Set(_health, "_currentShield", 10f); defense.Tick(_skills, false); Assert.That(shield.activeSelf, Is.True);
            Assert.That(_player.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "Preset yellow shield"), Is.EqualTo(1));
            Set(_health, "_isDead", true); defense.Tick(_skills, false); Assert.That(shield.activeSelf, Is.False);
        }
        [Test] public void LegacyBuffExpirySurvivesInitialNetworkSerializationForObservers()
        {
            var combat = _player.GetComponent<PlayerCombat>();
            Call(combat, "ApplyMoveBonusLocal", CombatEffectSources.WeaponSwap, 1.2f, 3f);
            Set(combat, "_monostatStrSkillActiveUntil", _skills.Now + 10);
            var writer = new Mirror.NetworkWriter(); combat.OnSerialize(writer, true);
            Set(combat, "_skillMoveBonusUntil", 0d); Set(combat, "_monostatStrSkillActiveUntil", 0d);
            combat.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()), true);
            Assert.That(combat.HasMovementSkillBonus && combat.IsMonostatStrLifestealActive, Is.True);
            using var aura = new SkillBuffAura(); aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
            Assert.That(glow.enabled, Is.True);
            Call(combat, "CancelAllCombatActions"); aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
        }
        [Test] public void WeaponSwapGlowPersistsUntilTheBonusHitIsConsumed()
        {
            var combat = _player.GetComponent<PlayerCombat>(); Set(combat, "_nextAttackDamageMultiplier", 1.2f);
            using var aura = new SkillBuffAura(); aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
            Assert.That(glow.enabled, Is.True);
            Assert.That(combat.ConsumeNextAttackDamageMultiplier(), Is.EqualTo(1.2f));
            aura.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
        }
        [Test] public void RisingBuffSymbolsStayIndependentAndHonorStealthAndCancellation()
        {
            using var aura = new SkillBuffAura();
            var combat = _player.GetComponent<PlayerCombat>();
            Set(combat, "_monostatStrSkillActiveUntil", _skills.Now + 10);
            Active(JobSkillKind.WarCry); Active(JobSkillKind.Recovery); aura.Tick(_skills, false);
            var fist = _player.transform.Find("STR buff symbols").gameObject;
            var shoe = _player.transform.Find("WarCry buff symbols").gameObject;
            var recovery = _player.transform.Find("Recovery buff symbols").gameObject;
            Assert.That(fist.activeSelf && shoe.activeSelf && recovery.activeSelf, Is.True);
            Assert.That(fist.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Fists and upward arrows"));
            Assert.That(shoe.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Shoes and upward arrows"));
            aura.Tick(_skills, true);
            Assert.That(fist.activeSelf || shoe.activeSelf || recovery.activeSelf, Is.False);
            Active(JobSkillKind.Stealth); aura.Tick(_skills, false);
            var properties = new MaterialPropertyBlock(); fist.GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(.5f));
            _skills.States.Remove((int)JobSkillKind.WarCry); aura.Tick(_skills, false);
            Assert.That(fist.activeSelf && !shoe.activeSelf && recovery.activeSelf, Is.True);
            _skills.States.Clear(); Call(combat, "CancelAllCombatActions"); aura.Tick(_skills, false);
            Assert.That(fist.activeSelf || shoe.activeSelf || recovery.activeSelf, Is.False);
            Active(JobSkillKind.WarCry); aura.Tick(_skills, false);
            Assert.That(_player.transform.Find("WarCry buff symbols").gameObject, Is.SameAs(shoe));
            Set(_health, "_isDead", true); aura.Tick(_skills, false); Assert.That(shoe.activeSelf, Is.False);
            aura.Dispose(); Assert.That(fist == null && shoe == null && recovery == null, Is.True);
        }
        [Test] public void ConBerserkIsYellowAndRecoveryIsGreen()
        {
            using var aura = new SkillBuffAura(); Active(JobSkillKind.Berserk); aura.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
            var properties = new MaterialPropertyBlock(); glow.GetPropertyBlock(properties);
            Color color = properties.GetColor("_BaseColor"); Assert.That(color.r > 2 && color.g > 1.5f && color.b < .1f, Is.True);
            _skills.States.Clear(); Active(JobSkillKind.Recovery); aura.Tick(_skills, false); glow.GetPropertyBlock(properties);
            color = properties.GetColor("_BaseColor"); Assert.That(color.g, Is.GreaterThan(color.r + color.b));
            Assert.That(_player.transform.Find("Recovery buff symbols").gameObject.activeSelf, Is.True);
        }

        DebuffIndicator Debuffs(GameObject target)
        {
            var indicator = target.GetComponent<DebuffIndicator>() ?? target.AddComponent<DebuffIndicator>();
            EditorTestLifecycle.Invoke(indicator, "Awake");
            return indicator;
        }
        [TestCase("_stunnedUntil")] [TestCase("_rootUntil")]
        [TestCase("_hookedUntil")] [TestCase("_vulnerableUntil")]
        public void ControlDebuffsShowOnePurpleDownwardGroupAndExpire(string field)
        {
            var indicator = Debuffs(_player);
            Set(_skills, field, _skills.Now + 3); Call(indicator, "LateUpdate");
            var arrows = _player.transform.Find("Debuff downward arrows");
            Assert.That(arrows.gameObject.activeSelf && _health.HasDebuff, Is.True);
            var block = new MaterialPropertyBlock(); arrows.GetComponent<Renderer>().GetPropertyBlock(block);
            Assert.That(block.GetFloat("_Direction"), Is.EqualTo(-1));
            Color color = block.GetColor("_BaseColor"); Assert.That(color.b > color.g && color.r > color.g, Is.True);
            Set(_skills, field, 0d); Call(indicator, "LateUpdate");
            Assert.That(arrows.gameObject.activeSelf || _health.HasDebuff, Is.False);
            Set(_skills, field, _skills.Now + 3); Call(indicator, "LateUpdate");
            Assert.That(_player.transform.Find("Debuff downward arrows"), Is.SameAs(arrows));
            Set(_health, "_isDead", true); Call(indicator, "LateUpdate");
            Assert.That(arrows.gameObject.activeSelf || _health.HasDebuff, Is.False);
        }
        [Test] public void StatPenaltiesOverlapWithoutDoublingArrowsOrHidingBehindRecovery()
        {
            var indicator = Debuffs(_player);
            Active(JobSkillKind.Dice); Set(_skills, "_diceFace", 1);
            _skills.States[(int)JobSkillKind.Berserk] = new SkillRuntime { CooldownUntil = _skills.Now + 20 };
            Active(JobSkillKind.Recovery); Call(indicator, "LateUpdate");
            var arrows = _player.transform.Find("Debuff downward arrows");
            Assert.That(arrows.gameObject.activeSelf, Is.True);
            _skills.States.Remove((int)JobSkillKind.Dice); Call(indicator, "LateUpdate");
            Assert.That(arrows.gameObject.activeSelf, Is.True, "a positive regen bonus does not remove the negative effect");
            _skills.States.Remove((int)JobSkillKind.Berserk); Call(indicator, "LateUpdate");
            Assert.That(arrows.gameObject.activeSelf, Is.False);
            Assert.That(_player.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "Debuff downward arrows"), Is.EqualTo(1));
        }
        [Test] public void AppliedSlowsAndTauntShowArrowsButVoluntarySkillPosturesDoNot()
        {
            var indicator = Debuffs(_player); var move = _player.GetComponent<PlayerManager>();
            move.SetMovementEffect(CombatEffectSources.BowCharge, .5f, 5);
            int postureSource = (int)typeof(ExpandedSkillController).GetField("MoveSource", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            move.SetMovementEffect(postureSource, 0, 5);
            Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.False);
            move.SetMovementEffect(CombatEffectSources.KickSlow, .5f, 5);
            move.SetMovementEffect(CombatEffectSources.WeaponSwap, 3, 5);
            Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.True, "a speed buff cannot hide an active slow");
            move.RemoveMovementEffect(CombatEffectSources.KickSlow);
            Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.False);
            var combat = _player.GetComponent<PlayerCombat>();
            Set(combat, "_tauntedByNetId", 123u); Set(combat, "_tauntedUntil", _skills.Now + 3);
            Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.True);
            Set(combat, "_tauntedUntil", 0d);
            Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.False);
        }
        [Test] public void PoisonArrowsTrackEachSourceAndStopOnStackCancellation()
        {
            var other = Object.Instantiate(_player);
            try
            {
                var indicator = Debuffs(_player);
                var a = _player.GetComponent<PlayerCombat>(); var b = other.GetComponent<PlayerCombat>();
                var stackA = (PoisonStackCollection<IDamageReceiver, Vector3>)typeof(PlayerCombat).GetField("_poisonStacks", Private).GetValue(a);
                var stackB = (PoisonStackCollection<IDamageReceiver, Vector3>)typeof(PlayerCombat).GetField("_poisonStacks", Private).GetValue(b);
                stackA.Add(_health, Vector3.zero, _skills.Now, 3, 5); stackB.Add(_health, Vector3.zero, _skills.Now, 6, 5);
                Call(indicator, "TrackPoison", a); Call(indicator, "TrackPoison", b); Call(indicator, "LateUpdate");
                Assert.That(_health.HasDebuff, Is.True);
                stackA.Clear(); Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.True);
                stackB.Clear(); Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.False);
                stackA.Add(_health, Vector3.zero, _skills.Now - 10, 1, 5); Call(indicator, "TrackPoison", a);
                Call(indicator, "LateUpdate"); Assert.That(_health.HasDebuff, Is.False);
            }
            finally { Object.DestroyImmediate(other); }
        }
        [Test] public void DebuffFlagReplicatesForLateObserversAndClearsWithoutBreakingOtherHealthState()
        {
            var indicator = Debuffs(_player); _skills.ApplyControl(3, false, false); Call(indicator, "LateUpdate");
            var writer = new Mirror.NetworkWriter(); _health.OnSerialize(writer, true);
            Call(_health, "SetDebuffPresentation", false); _health.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()), true);
            Assert.That(_health.HasDebuff, Is.True); Assert.That(_health.CurrentHp, Is.EqualTo(100));
            Set(_skills, "_stunnedUntil", 0d); Call(indicator, "LateUpdate");
            writer = new Mirror.NetworkWriter(); _health.OnSerialize(writer, true);
            Call(_health, "SetDebuffPresentation", true); _health.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()), true);
            Assert.That(_health.HasDebuff, Is.False);
        }
        [Test] public void DummyDebuffsShowDownwardArrowsAndReplicateTheirClearState()
        {
            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dummy.prefab"));
            try
            {
                EditorTestLifecycle.BindNetwork(target);
                var dummy = target.GetComponent<DummyHealth>(); Set(dummy, "_currentHp", 100f);
                var indicator = Debuffs(target); dummy.ApplyStun(3, true); Call(indicator, "LateUpdate");
                var arrows = target.transform.Find("Debuff downward arrows");
                Assert.That(arrows.gameObject.activeSelf && dummy.HasDebuff, Is.True);
                var writer = new Mirror.NetworkWriter(); dummy.OnSerialize(writer, true);
                Call(dummy, "SetDebuffPresentation", false); dummy.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()), true);
                Assert.That(dummy.HasDebuff, Is.True);
                Set(dummy, "_stunnedUntil", 0d); Call(indicator, "LateUpdate");
                Assert.That(arrows.gameObject.activeSelf, Is.True, "vulnerability still remains");
                Set(dummy, "_vulnerableUntil", 0d); Call(indicator, "LateUpdate");
                Assert.That(arrows.gameObject.activeSelf || dummy.HasDebuff, Is.False);
            }
            finally { Object.DestroyImmediate(target); }
        }
        [TestCase("bad {1}",1)] [TestCase("{0} {2}",2)] [TestCase("{0",1)]
        public void InvalidStringSchemaIsRejected(string text,int count) => Assert.Throws<InvalidDataException>(()=>SkillWorkbookImporter.ValidateFormat(text,count));

        [Test] public void InvalidWorkbookCannotReplaceTheLastImportedData()
        {
            var data=AssetDatabase.LoadAssetAtPath<SkillGameData>(SkillWorkbookImporter.AssetPath);
            string before=EditorJsonUtility.ToJson(data);
            string folder=Path.GetFullPath(Path.Combine("Temp","SkillWorkbookTest-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            try
            {
                foreach(string name in new[]{"Skill","Character","String"}) File.Copy("GameData/GameData_"+name+".xlsx",Path.Combine(folder,"GameData_"+name+".xlsx"));
                using(var zip=ZipFile.Open(Path.Combine(folder,"GameData_Skill.xlsx"),ZipArchiveMode.Update))
                {
                    var entry=zip.GetEntry("xl/worksheets/sheet1.xml"); XDocument xml;
                    using(var stream=entry.Open()) xml=XDocument.Load(stream);
                    XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    xml.Descendants(ns+"c").Single(c=>(string)c.Attribute("r")=="C5").Element(ns+"v").Value="999";
                    entry.Delete(); using(var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open()) xml.Save(stream);
                }
                Assert.Throws<InvalidDataException>(()=>SkillWorkbookImporter.ImportFrom(folder));
                Assert.That(EditorJsonUtility.ToJson(data),Is.EqualTo(before));
            }
            finally { foreach(string name in new[]{"Skill","Character","String"}) File.Delete(Path.Combine(folder,"GameData_"+name+".xlsx")); Directory.Delete(folder); }
        }

        [Test] public void ReplicatedSkillAndTrapStateRoundTripsForLateJoiners()
        {
            _skills.States[(int)JobSkillKind.Knife]=new SkillRuntime{Charges=1,NextChargeAt=18,CooldownUntil=25};
            _skills.Traps[42]=new SkillTrapSnapshot{Position=new Vector3(1,2,3),ExpiresAt=60,ClosedAt=24,Closed=true};
            using var writer=Mirror.NetworkWriterPool.Get();
            _skills.States.OnSerializeAll(writer); _skills.Traps.OnSerializeAll(writer);
            using var reader=Mirror.NetworkReaderPool.Get(writer.ToArraySegment());
            var states=new Mirror.SyncDictionary<int,SkillRuntime>(); var traps=new Mirror.SyncDictionary<int,SkillTrapSnapshot>();
            states.OnDeserializeAll(reader); traps.OnDeserializeAll(reader);
            Assert.That(traps[42].ClosedAt,Is.EqualTo(24));
            Assert.That(traps[42].Closed,Is.True);
            Assert.That(states[(int)JobSkillKind.Knife].Charges,Is.EqualTo(1));
            Assert.That(states[(int)JobSkillKind.Knife].NextChargeAt,Is.EqualTo(18));
            Assert.That(traps[42].Position,Is.EqualTo(new Vector3(1,2,3))); Assert.That(traps[42].ExpiresAt,Is.EqualTo(60));
        }

        [Test] public void AllJobsHaveTwoDistinctValidatedSlots()
        {
            var defaults=SkillLoadout.Defaults(); Assert.That(SkillLoadout.Validate(defaults),Is.True);
            Assert.That(SkillLoadout.Validate(new int[2]),Is.False);
            Assert.That(SkillLoadout.Validate(null),Is.False);
            for(int job=0;job<6;job++)
            {
                var duplicate=(int[])defaults.Clone(); duplicate[job*2+1]=duplicate[job*2]; Assert.That(SkillLoadout.Validate(duplicate),Is.False);
                var forged=(int[])defaults.Clone(); forged[job*2]=999; Assert.That(SkillLoadout.Validate(forged),Is.False);
            }
            defaults[0]=(int)JobSkillKind.Knife; Assert.That(SkillLoadout.Validate(defaults),Is.False);
        }
        [Test] public void CastingRejectsUnselectedSkillSlotsAndNonFiniteAim()
        {
            Equip(0,JobSkillKind.WarCry);
            Assert.That(_skills.TryUse(-1,Vector3.forward),Is.False);
            Assert.That(_skills.TryUse(2,Vector3.forward),Is.False);
            Assert.That(_skills.TryUse(0,new Vector3(float.NaN,0,1)),Is.False);
            Assert.That(_skills.TryUse(0,Vector3.zero),Is.False);
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Set(_skills,"_nextUse",0d); Assert.That(_skills.TryUse(0,Vector3.forward),Is.False,"Cannot restart active buff.");
        }
        [Test] public void BerserkDrainAndRecoveryPenaltyFollowToggle()
        {
            Equip(1,JobSkillKind.Berserk);
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.AttackMultiplier,Is.EqualTo(2)); Assert.That(_skills.AttackSpeedMultiplier,Is.EqualTo(1.3f)); Assert.That(_skills.RegenMultiplier,Is.Zero);
            Set(_skills,"_nextDrain",_skills.Now-1); Call(_skills,"Update");
            Assert.That(_health.CurrentHp,Is.EqualTo(97).Within(.001f));
            Set(_skills,"_nextUse",0d); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.Berserking,Is.False); Assert.That(_skills.RegenMultiplier,Is.EqualTo(.5f));
            Assert.That(_skills.Read(JobSkillKind.Berserk).CooldownUntil-_skills.Now,Is.EqualTo(20).Within(.1));
            _skills.States[(int)JobSkillKind.Berserk]=default;
            Assert.That(_skills.RegenMultiplier,Is.EqualTo(1));
        }
        [TestCase(1f)] [TestCase(2f)] [TestCase(4f)] [TestCase(4.1f)]
        public void BerserkDrainStopsAtOneWithoutDeathAndClearsCopiedToggle(float hp)
        {
            Equip(1, JobSkillKind.Berserk); Assert.That(_skills.TryUse(0, Vector3.forward), Is.True);
            Set(_health, "_currentHp", hp); Set(_skills, "_maintainedCopy", (int)JobSkillKind.Berserk);
            Set(_skills, "_nextDrain", _skills.Now - 1); Call(_skills, "Update");
            Assert.That(_health.CurrentHp, Is.EqualTo(Mathf.Max(1, hp - 3)).Within(.001f));
            Assert.That(_health.IsDead, Is.False);
            Assert.That(_skills.Berserking, Is.EqualTo(hp > 4));
            if (hp <= 4)
            {
                Assert.That(_skills.Read(JobSkillKind.Berserk).CooldownUntil - _skills.Now, Is.EqualTo(20).Within(.1));
                Assert.That(_skills.RegenMultiplier, Is.EqualTo(.5f));
                Assert.That(typeof(ExpandedSkillController).GetField("_maintainedCopy", Private).GetValue(_skills), Is.EqualTo(-1));
                Set(_skills, "_nextUse", 0d); Assert.That(_skills.TryUse(0, Vector3.forward), Is.False);
            }
        }
        [Test] public void RecoveryChargesThirtyPercentAndDoublesRegeneration()
        {
            Equip(1,JobSkillKind.Recovery); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_health.CurrentHp,Is.EqualTo(70)); Assert.That(_skills.RegenMultiplier,Is.EqualTo(2));
            _skills.CancelForLoadout(); _skills.States[(int)JobSkillKind.Recovery]=default;
            _health.SetCurrentHp(30); Set(_skills,"_nextUse",0d);
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.False,"Cannot spend all remaining health.");
        }
        [Test] public void ChargeCooldownStartsOnCancellationAndTurnRateIsBounded()
        {
            Equip(0,JobSkillKind.Charge); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.Read(JobSkillKind.Charge).CooldownUntil,Is.Zero);
            Set(_skills,"_lastTurn",_skills.Now-.1); Call(_skills,"Steer",Vector3.back);
            var direction=(Vector3)typeof(ExpandedSkillController).GetField("_chargeDirection",Private).GetValue(_skills);
            Assert.That(Vector3.Angle(direction,Vector3.forward),Is.LessThan(10));
            _skills.NotifyAttackStarted(); Assert.That(_skills.IsCharging,Is.False);
            Assert.That(_skills.Read(JobSkillKind.Charge).CooldownUntil-_skills.Now,Is.EqualTo(20).Within(.1));
        }
        [Test] public void ChargeUsesUpdatedSpeedCurveAndRepressCancelsEvenDuringInputDebounce()
        {
            Equip(0,JobSkillKind.Charge); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.Read(JobSkillKind.Charge).CooldownUntil,Is.Zero);
            Set(_skills,"_chargeStarted",_skills.Now);
            double started=_skills.Now;
            float start=(float)Call(_skills,"ResolveChargeSpeed",started),end=(float)Call(_skills,"ResolveChargeSpeed",started+2.5);
            var mono=new StatContainer(); mono.AGI.Invested=30;
            float agi=StatBalanceCalculator.Calculate(mono,new Identity(IdentityType.Monostat,StatKind.AGI)).MoveSpeed;
            Assert.That(start,Is.EqualTo(_stats.GetDerivedStats().MoveSpeed*.8f).Within(.001f));
            Assert.That(end,Is.EqualTo(agi*1.3f).Within(.001f));
            Assert.That((float)Call(_skills,"ResolveChargeSpeed",started+1.25),Is.EqualTo((start+end)*.5f).Within(.001f));
            Assert.That((float)Call(_skills,"ResolveChargeSpeed",started+4),Is.EqualTo(end));
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.IsCharging,Is.False);
            Assert.That(_skills.Read(JobSkillKind.Charge).CooldownUntil-_skills.Now,Is.EqualTo(20).Within(.1));
        }
        [TestCase(JobSkillKind.WarCry)] [TestCase(JobSkillKind.MonostatStrLifesteal)]
        public void OtherSkillSlotCancelsChargeWithoutCastingIt(JobSkillKind other)
        {
            Equip(0, JobSkillKind.Charge); _player.GetComponent<SkillLoadout>().Choices[1] = (int)other;
            Assert.That(_skills.TryUse(0, Vector3.forward), Is.True);
            _player.GetComponent<PlayerCombat>().UseSkillSlot(1, true);
            Assert.That(_skills.IsCharging, Is.False);
            Assert.That(_skills.Active(other), Is.False);
            Assert.That(_player.GetComponent<PlayerCombat>().IsMonostatStrLifestealActive, Is.False);
        }
        [Test] public void ChargePassThroughRestoresOnlyItsOwnCollisionChanges()
        {
            var victim=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var own=_player.GetComponent<CharacterController>(); var other=victim.GetComponent<Collider>(); var obstacle=wall.GetComponent<Collider>();
                Equip(0,JobSkillKind.Charge); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
                Call(_skills,"PassChargeTarget",victim.transform);
                Assert.That(Physics.GetIgnoreCollision(own,other),Is.True);
                Assert.That(Physics.GetIgnoreCollision(own,obstacle),Is.False,"walls remain solid");
                _skills.NotifyAttackStarted(); Assert.That(Physics.GetIgnoreCollision(own,other),Is.False);
                Physics.IgnoreCollision(own,other,true);
                Call(_skills,"PassChargeTarget",victim.transform); _skills.CancelForLoadout();
                Assert.That(Physics.GetIgnoreCollision(own,other),Is.True,"preserve collision filtering owned by another system");
                Physics.IgnoreCollision(own,other,false);
                Call(_skills,"PassChargeTarget",victim.transform); _skills.CancelForLoadout();
                Assert.That(Physics.GetIgnoreCollision(own,other),Is.False,"cancellation restores contacts even after charge state is cleared");
            }
            finally { Object.DestroyImmediate(victim); Object.DestroyImmediate(wall); }
        }
        [TestCase(false)] [TestCase(true)] public void StealthBreaksOnDamageOrAttackAndGrantsAmbush(bool attack)
        {
            Active(JobSkillKind.Stealth);
            if(attack) _skills.NotifyAttackStarted(); else _skills.NotifyDamaged();
            Assert.That(_skills.IsStealthed,Is.False); Assert.That(_skills.AttackMultiplier,Is.EqualTo(1.2f));
            Set(_skills,"_ambushUntil",_skills.Now-1); Assert.That(_skills.AttackMultiplier,Is.EqualTo(1));
        }
        [Test] public void StealthUsesCumulativeDistance()
        {
            Active(JobSkillKind.Stealth); Set(_skills,"_lastStealthPosition",_player.transform.position);
            _player.transform.position+=Vector3.right*.6f; Call(_skills,"Update"); Assert.That(_skills.IsStealthed,Is.True);
            _player.transform.position-=Vector3.right*.6f; Call(_skills,"Update"); Assert.That(_skills.IsStealthed,Is.False);
        }
        [Test] public void KnifeRefillsSequentiallyAndStopsAtThree()
        {
            Assert.That(_skills.Read(JobSkillKind.Knife).Charges,Is.EqualTo(3));
            _skills.States[(int)JobSkillKind.Knife]=new SkillRuntime{Charges=0,NextChargeAt=1};
            Call(_skills,"Recharge",9.01d); Assert.That(_skills.Read(JobSkillKind.Knife).Charges,Is.EqualTo(2));
            Call(_skills,"Recharge",17.01d); Assert.That(_skills.Read(JobSkillKind.Knife).Charges,Is.EqualTo(3)); Assert.That(_skills.Read(JobSkillKind.Knife).NextChargeAt,Is.Zero);
        }
        [TestCase(JobSkillKind.Trap,3,25)]
        [TestCase(JobSkillKind.StrategistRoll,2,8)]
        [TestCase(JobSkillKind.PolymathRoll,2,10)]
        public void ChargeSkillsSpendSequentiallyPreserveRechargeAcrossLoadoutAndReplicate(JobSkillKind kind,int maximum,float interval)
        {
            Assert.That(_skills.ChargeState(kind).Charges,Is.EqualTo(maximum));
            Assert.That(_skills.SpendCharge(kind),Is.True); double next=_skills.Read(kind).NextChargeAt;
            Assert.That(next-_skills.Now,Is.EqualTo(interval).Within(.05));
            var partial=_skills.Hud(0,kind); Assert.That(partial.Phase,Is.EqualTo(UI.SkillHudPhase.Ready));
            Assert.That(partial.RemainingSeconds,Is.GreaterThan(0));
            for(int i=1;i<maximum;i++) Assert.That(_skills.SpendCharge(kind),Is.True);
            Assert.That(_skills.Read(kind).NextChargeAt,Is.EqualTo(next),"spending again must not restart recharge");
            Assert.That(_skills.SpendCharge(kind),Is.False);
            Assert.That(_skills.Hud(0,kind).Phase,Is.EqualTo(UI.SkillHudPhase.Cooldown));
            _skills.CancelForLoadout(); Assert.That(_skills.Read(kind).Charges,Is.Zero);
            var writer=new Mirror.NetworkWriter(); _skills.OnSerialize(writer,true); _skills.States.Clear();
            _skills.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true);
            Assert.That(_skills.Read(kind).Charges,Is.Zero); Assert.That(_skills.Read(kind).NextChargeAt,Is.EqualTo(next));
            Call(_skills,"Recharge",next+.01); Assert.That(_skills.Read(kind).Charges,Is.EqualTo(1));
            Call(_skills,"Recharge",next+interval*maximum); Assert.That(_skills.Read(kind).Charges,Is.EqualTo(maximum));
            Assert.That(_skills.Read(kind).NextChargeAt,Is.Zero);
        }
        [Test] public void DiceHudShowsRemainingDurationAsADecreasingRadialTimer()
        {
            double now=_skills.Now;
            _skills.States[(int)JobSkillKind.Dice]=new SkillRuntime{ActiveUntil=now+7.5,CooldownUntil=now+7.5};
            var hud=_skills.Hud(0,JobSkillKind.Dice);
            Assert.That(hud.Phase,Is.EqualTo(UI.SkillHudPhase.Active));
            Assert.That(hud.RemainingSeconds,Is.EqualTo(7.5).Within(.05)); Assert.That(hud.NormalizedFill,Is.EqualTo(.5).Within(.01));
            _skills.States.Clear(); hud=_skills.Hud(0,JobSkillKind.Dice);
            Assert.That(hud.Phase,Is.EqualTo(UI.SkillHudPhase.Ready)); Assert.That(hud.RemainingSeconds,Is.Zero);
        }
        [Test] public void ShieldImpactOnlyFiresOnAbsorptionAndSurvivesTheBreakingHit()
        {
            using var defense=new DefenseSkillVfx(); defense.Tick(_skills,false);
            int hits=0; _health.ShieldHit+=_=>hits++;
            Set(_health,"_currentShield",5f);
            _health.ApplyDamage(new DamageRequest(8,DamageSource.Fixed,0,null,Vector3.forward+Vector3.up));
            defense.Tick(_skills,false);
            Assert.That(hits,Is.EqualTo(1)); Assert.That(_health.CurrentShield,Is.Zero); Assert.That(_health.CurrentHp,Is.EqualTo(97));
            var shell=_player.transform.Find("Preset yellow shield"); Assert.That(shell.gameObject.activeSelf,Is.True);
            var renderer=shell.GetComponent<MeshRenderer>(); var properties=new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_ImpactAge"),Is.InRange(0,.35f));
            Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").a,Is.Zero,"broken shield shows only impact light");
            _health.ApplyDamage(new DamageRequest(3,DamageSource.Fixed,0,null,Vector3.forward)); Assert.That(hits,Is.EqualTo(1));
            defense.Tick(_skills,true); Assert.That(shell.gameObject.activeSelf,Is.False,"concealment also hides hit light");
            Set(defense,"_shieldHitAt",Time.unscaledTimeAsDouble-1); defense.Tick(_skills,false); Assert.That(shell.gameObject.activeSelf,Is.False);
        }
        [Test] public void ImportedTrapAndFasterCastKeepFootprintAndAnimationInSync()
        {
            var prefab=SkillPresentationCatalog.Instance.Find((int)JobSkillKind.Trap).Prefab;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Skills/Source/Quaternius/BearTrap_Open.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length),Is.EqualTo(source.triangles.Length));
            Assert.That(prefab.GetComponent<SkillTrapVisual>(),Is.Not.Null);
            var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Player/Anim/Player.controller");
            var state=controller.layers.Single(l=>l.name=="ExpandedSkills").stateMachine.states.Single(s=>s.state.name=="Skill_SHARED_Trap").state;
            Assert.That(state.motion.averageDuration/state.speed,Is.EqualTo(1.1f).Within(.001));
            Assert.That(SkillGameData.Number(JobSkillKind.StrategistRoll,"RollDistance",0),Is.EqualTo(3.6f));
            Assert.That(SkillGameData.Number(JobSkillKind.StrategistRoll,"RollDurationSeconds",0),Is.EqualTo(.35f));
        }
        [Test] public void StunBlocksAttacksWhileTrapOnlyLocksMovementAndLook()
        {
            _skills.ApplyControl(3,false,true); Assert.That(_skills.BlocksCombat,Is.True); Assert.That(_skills.IncomingMultiplier,Is.EqualTo(1.6f));
            Assert.That(_player.GetComponent<PlayerManager>().IsSkillInputLocked(SkillInputLockFlags.Attack),Is.True);
            _skills.CancelForLoadout(); _skills.ApplyControl(5,true,false);
            Assert.That(_skills.BlocksCombat,Is.False); Assert.That(_skills.LookLocked,Is.True);
            Assert.That(Quaternion.Angle(_skills.RestrictRotation(Quaternion.Euler(0,180,0)),_player.transform.rotation),Is.LessThan(.01f));
        }
        [Test] public void FortifyChangesDefenseWithoutChangingJobOrInvestment()
        {
            var raw=new StatContainer{DEF=new StatSlot{Invested=30}}; _stats.ApplyLocalSceneStats(raw);
            var identity=_stats.CurrentIdentity; float original=_stats.GetFinalTotal(StatKind.DEF);
            Active(JobSkillKind.Fortify); Call(_skills,"Update");
            Assert.That(_stats.GetFinalTotal(StatKind.DEF),Is.EqualTo(original*2)); Assert.That(_stats.CurrentIdentity,Is.EqualTo(identity));
            Assert.That(_skills.ControlFlags & SkillInputLockFlags.Move,Is.Not.Zero);
            _skills.CancelForLoadout(); Assert.That(_stats.GetFinalTotal(StatKind.DEF),Is.EqualTo(original));
        }
        [Test] public void BloodlustAppliesBothSpeedBonusesAndTenPercentHealingOnlyWhileActive()
        {
            Equip(0,JobSkillKind.MonostatStrLifesteal);
            var combat=_player.GetComponent<PlayerCombat>();
            float normal=(float)Call(combat,"ResolveCurrentAttackSpeed");
            Set(combat,"_monostatStrSkillActiveUntil",_skills.Now+10);
            Assert.That((float)Call(combat,"ResolveCurrentAttackSpeed"),Is.EqualTo(normal*1.2f).Within(.001f));
            Assert.That(combat.MonostatStrMoveMultiplier,Is.EqualTo(1.1f).Within(.001f));
            Call(_skills,"Update");
            var movement=_player.GetComponent<PlayerManager>();
            var effects=typeof(PlayerManager).GetField("_movementEffects",Private).GetValue(movement);
            Assert.That((float)effects.GetType().GetMethod("Evaluate").Invoke(effects,new object[]{_skills.Now}),Is.EqualTo(1.1f).Within(.001f));
            _health.SetCurrentHp(50); combat.NotifyPhysicalDamageDealt(20);
            Assert.That(_health.CurrentHp,Is.EqualTo(52).Within(.001f));
            Set(combat,"_monostatStrSkillActiveUntil",_skills.Now-1);
            Assert.That((float)Call(combat,"ResolveCurrentAttackSpeed"),Is.EqualTo(normal).Within(.001f));
            Assert.That(combat.MonostatStrMoveMultiplier,Is.EqualTo(1));
            Call(_skills,"Update");
            Assert.That((float)effects.GetType().GetMethod("Evaluate").Invoke(effects,new object[]{_skills.Now}),Is.EqualTo(1f));
            combat.NotifyPhysicalDamageDealt(20); Assert.That(_health.CurrentHp,Is.EqualTo(52).Within(.001f));
        }
        [Test] public void BashReadinessExpiresAfterTenSecondsWhileTheFifteenSecondCooldownRemains()
        {
            Equip(3, JobSkillKind.Bash);
            Assert.That(_skills.TryUse(0, Vector3.forward), Is.True);
            var state = _skills.Read(JobSkillKind.Bash);
            Assert.That(state.CooldownUntil - state.ActiveUntil, Is.EqualTo(5).Within(.01));
            Assert.That(state.ActiveUntil - _skills.Now, Is.InRange(9.5, 10.01));
            var go = new GameObject("Expired bash target");
            try
            {
                var dummy = EditorTestLifecycle.AddNetwork<DummyHealth>(go);
                state.ActiveUntil = _skills.Now - .01; _skills.States[(int)JobSkillKind.Bash] = state;
                _skills.NotifyPhysicalHit(dummy);
                Assert.That(dummy.IsStunned, Is.False);
                Assert.That(_skills.Read(JobSkillKind.Bash).CooldownUntil, Is.EqualTo(state.CooldownUntil));
                Set(_skills, "_nextUse", 0d); Assert.That(_skills.TryUse(0, Vector3.forward), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void TauntReadinessRequiresAHitAndExpiresWithItsWeaponColor()
        {
            var combat = _player.GetComponent<PlayerCombat>();
            Assert.That(SkillPresentationCatalog.Data((int)JobSkillKind.MonostatDefTaunt).TauntReadyDurationSeconds, Is.EqualTo(10));
            Set(combat, "_advancedActiveSkillKey", (int)JobSkillKind.MonostatDefTaunt);
            Set(combat, "_advancedActiveUntil", _skills.Now + 10);
            using var defense = new DefenseSkillVfx();
            defense.Tick(_skills, false);
            var glows = _player.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "Bash weapon glow").ToArray();
            Assert.That(glows, Is.Not.Empty);
            var material = glows[0].sharedMaterial;
            Assert.That(material.GetColor("_BaseColor").r, Is.GreaterThan(material.GetColor("_BaseColor").b));
            combat.NotifyPhysicalDamageDealt(10, null); Assert.That(combat.IsTauntReady, Is.True);
            var go = new GameObject("Taunt target");
            try
            {
                var dummy = EditorTestLifecycle.AddNetwork<DummyHealth>(go);
                combat.NotifyPhysicalDamageDealt(0, dummy); Assert.That(combat.IsTauntReady, Is.True);
                combat.NotifyPhysicalDamageDealt(10, dummy); Assert.That(combat.IsTauntReady, Is.False);
                Set(combat, "_advancedActiveSkillKey", (int)JobSkillKind.MonostatDefTaunt);
                Set(combat, "_advancedActiveUntil", _skills.Now - 1);
                defense.Tick(_skills, false);
                Assert.That(combat.IsTauntReady, Is.False); Assert.That(glows.All(r => !r.enabled), Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void CombinedReadyColorsKeepTheirOverlayMaterialWhenTauntChangesTheSword()
        {
            using var defense = new DefenseSkillVfx();
            Active(JobSkillKind.Bash); defense.Tick(_skills, false);
            var glow = _player.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "Bash weapon glow");
            var material = glow.sharedMaterial;
            var combat = _player.GetComponent<PlayerCombat>();
            Set(combat, "_advancedActiveSkillKey", (int)JobSkillKind.MonostatDefTaunt);
            Set(combat, "_advancedActiveUntil", _skills.Now + 10);
            Call(combat, "RefreshSkillSwordVisualFromState"); defense.Tick(_skills, false);
            Assert.That(glow.sharedMaterial, Is.SameAs(material));
            var color = material.GetColor("_BaseColor"); Assert.That(color.r, Is.GreaterThan(1)); Assert.That(color.b, Is.GreaterThan(2));
            Set(combat, "_advancedActiveUntil", _skills.Now - 1);
            Call(combat, "RefreshSkillSwordVisualFromState"); defense.Tick(_skills, false);
            color = material.GetColor("_BaseColor"); Assert.That(color.r, Is.LessThan(.2f)); Assert.That(color.b, Is.GreaterThan(2));
            defense.Tick(_skills, true); Assert.That(glow.enabled, Is.False);
            _skills.States.Clear(); defense.Tick(_skills, false); Assert.That(glow.enabled, Is.False);
        }

        [Test] public void BashStunsDummyConsumesOnceAndVulnerabilityExpires()
        {
            var go=new GameObject("Bash dummy");
            try
            {
                var dummy=EditorTestLifecycle.AddNetwork<DummyHealth>(go);
                Set(dummy,"_maxHp",100f); Set(dummy,"_currentHp",100f);
                Active(JobSkillKind.Bash); _skills.NotifyPhysicalHit(dummy);
                Assert.That(dummy.IsStunned,Is.True); Assert.That(_skills.Active(JobSkillKind.Bash),Is.False);
                double end=(double)typeof(DummyHealth).GetField("_stunnedUntil",Private).GetValue(dummy);
                _skills.NotifyPhysicalHit(dummy);
                Assert.That((double)typeof(DummyHealth).GetField("_stunnedUntil",Private).GetValue(dummy),Is.EqualTo(end));
                var hit=new DamageRequest(10,DamageSource.Physical,0,null,Vector3.up);
                Assert.That(dummy.ApplyDamage(hit).HpDamage,Is.EqualTo(16));
                Assert.That(dummy.ApplyDamage(new DamageRequest(10,DamageSource.Fixed,0,null,Vector3.up)).HpDamage,Is.EqualTo(10));
                Set(dummy,"_stunnedUntil",_skills.Now-1); Set(dummy,"_vulnerableUntil",_skills.Now-1);
                Assert.That(dummy.IsStunned,Is.False); Assert.That(dummy.ApplyDamage(hit).HpDamage,Is.EqualTo(10));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void DummyStunExpiryIsReplicatedForLateJoiners()
        {
            var fromObject=new GameObject("stunned dummy source"); var toObject=new GameObject("dummy observer");
            try
            {
                var from=EditorTestLifecycle.AddNetwork<DummyHealth>(fromObject);
                var to=EditorTestLifecycle.AddNetwork<DummyHealth>(toObject);
                from.ApplyStun(3,true);
                var writer=new Mirror.NetworkWriter(); from.OnSerialize(writer,true);
                to.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true);
                Assert.That(to.IsStunned,Is.True);
                Assert.That(typeof(DummyHealth).GetField("_stunnedUntil",Private).GetValue(to),Is.EqualTo(typeof(DummyHealth).GetField("_stunnedUntil",Private).GetValue(from)));
                Set(from,"_stunnedUntil",0d); Set(from,"_vulnerableUntil",0d);
                writer=new Mirror.NetworkWriter(); from.OnSerialize(writer,true);
                to.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true);
                Assert.That(to.IsStunned,Is.False);
            }
            finally { Object.DestroyImmediate(fromObject); Object.DestroyImmediate(toObject); }
        }
        [Test] public void RootPreventsChargingButAllowsNonMovementSkills()
        {
            Equip(0,JobSkillKind.Charge); _skills.ApplyControl(5,true,false);
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.False);
            Equip(0,JobSkillKind.WarCry);
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.LookLocked,Is.True);
        }
        [Test] public void NearestWallBlocksHookAndStealAndRangeIsStrict()
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position=_player.transform.position+Vector3.up*1.35f+Vector3.forward*2;
                Physics.SyncTransforms();
                Assert.That(SkillTargeting.Cast(_skills,_player.transform.position+Vector3.up*1.35f,Vector3.forward,4,.08f,out var hit),Is.True);
                Assert.That(hit.collider.gameObject,Is.SameAs(wall));
                wall.transform.position+=Vector3.forward*4; Physics.SyncTransforms();
                Assert.That(SkillTargeting.Cast(_skills,_player.transform.position+Vector3.up*1.35f,Vector3.forward,4,.08f,out _),Is.False);
            }
            finally { Object.DestroyImmediate(wall); }
        }
        [Test] public void LocalStealthIsHalfTransparentAndDoesNotModifySharedMaterials()
        {
            var renderer=_player.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var original=renderer.sharedMaterial; var originalColor=original.GetColor("_BaseColor");
            using var stealth=new SkillStealthPresentation(_player.transform);
            stealth.Apply(true,true);
            Assert.That(renderer.sharedMaterial,Is.Not.SameAs(original));
            Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").a,Is.EqualTo(.5f));
            Assert.That(renderer.sharedMaterial.renderQueue,Is.EqualTo(3000));
            Assert.That(original.GetColor("_BaseColor"),Is.EqualTo(originalColor));
            stealth.Apply(false,true);
            Assert.That(renderer.sharedMaterial,Is.SameAs(original));
        }
        [Test] public void RemoteStealthHidesNewRenderersAndRestoresWithoutRevivingDeadMeshes()
        {
            var mesh=_player.GetComponentInChildren<SkinnedMeshRenderer>(true); mesh.forceRenderingOff=false;
            using var stealth=new SkillStealthPresentation(_player.transform);
            stealth.Apply(true,false);
            Assert.That(mesh.forceRenderingOff,Is.True);
            var weapon=GameObject.CreatePrimitive(PrimitiveType.Cube); weapon.transform.SetParent(_player.transform);
            stealth.Apply(true,false); Assert.That(weapon.GetComponent<Renderer>().forceRenderingOff,Is.True);
            mesh.enabled=false; stealth.Apply(false,false);
            Assert.That(mesh.forceRenderingOff,Is.False); Assert.That(mesh.enabled,Is.False);
            Assert.That(weapon.GetComponent<Renderer>().forceRenderingOff,Is.False);
        }
        [Test] public void ReadiedKnifeLeavesLocomotionAndCrouchingUnlocked()
        {
            Equip(2,JobSkillKind.Knife); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.KnifeReady,Is.True); Assert.That(_skills.ControlFlags,Is.EqualTo(SkillInputLockFlags.None));
            var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Player/Anim/Player.controller");
            var layer=controller.layers.Single(l=>l.name=="ExpandedUpperBody");
            foreach(var part in new[]{AvatarMaskBodyPart.Root,AvatarMaskBodyPart.LeftLeg,AvatarMaskBodyPart.RightLeg}) Assert.That(layer.avatarMask.GetHumanoidBodyPartActive(part),Is.False);
            Assert.That(layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),Is.True);
        }
        [TestCase(JobSkillKind.Trap,"ExpandedSkills","Skill_SHARED_Trap")]
        [TestCase(JobSkillKind.Hook,"ExpandedUpperBody","Skill_STR_Hook")]
        [TestCase(JobSkillKind.Knife,"ExpandedUpperBody","Skill_AGI_Knife")]
        public void CastCueSurvivesVisualTickBeforeAnimatorEvaluation(JobSkillKind kind,string layerName,string stateName)
        {
            var animator=_player.GetComponent<Animator>(); animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
            int layer=animator.GetLayerIndex(layerName);
            animator.Play("Empty",layer,0); animator.Update(0); animator.SetLayerWeight(layer,0);
            using var visuals=new SkillExpansionVisuals(_skills);
            visuals.Play(kind,Vector3.forward);
            visuals.Tick();
            Assert.That(animator.GetLayerWeight(layer),Is.EqualTo(1),"Input and visual Update happen before Animator evaluation.");
            animator.Update(.15f); visuals.Tick();
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName(stateName),Is.True);
            Assert.That(animator.GetLayerWeight(layer),Is.EqualTo(1));
        }
        [Test] public void SkillBonesKeepEvaluatingWhileCulledAndRestoreOnCancellation()
        {
            var animator=_player.GetComponent<Animator>(); animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
            using var visuals=new SkillExpansionVisuals(_skills);
            visuals.Play(JobSkillKind.Hook,Vector3.forward);
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
            visuals.Dispose();
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("ExpandedUpperBody")),Is.Zero);
        }
        [Test] public void HookFlightPullAndEmptyRetrievalAreOnePointFiveTimesFaster()
        {
            float speed=ExpandedSkillController.Value(JobSkillKind.Hook,"ProjectileSpeed");
            float pull=ExpandedSkillController.Value(JobSkillKind.Hook,"PullSpeed");
            float recovery=ExpandedSkillController.Value(JobSkillKind.Hook,"RetrieveSeconds");
            Assert.That(speed,Is.EqualTo(12f*1.5f));
            Assert.That(pull,Is.EqualTo(8f*1.5f));
            Assert.That(recovery,Is.EqualTo(.6f/1.5f).Within(.0001f));
            foreach(float distance in new[]{1f,4f,6f})
                Assert.That(Mathf.Max(recovery,distance/pull),Is.EqualTo(Mathf.Max(.6f,distance/8f)/1.5f).Within(.0001f));
            var animator=_player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0);
            using var visuals=new SkillExpansionVisuals(_skills);
            visuals.RetrieveHook(Vector3.forward*3,recovery);
            Assert.That(animator.GetFloat("HookRetrieveRate"),Is.EqualTo(1.5f).Within(.001f));
        }

        [Test] public void HookRetrievalKeepsItsLockAndStunCancelsTheOldCast()
        {
            Set(_skills,"_hookUntil",_skills.Now+3d); Set(_skills,"_hookReleaseAt",_skills.Now-1d);
            Call(_skills,"BeginHookRetrieval",Vector3.forward*3,.6f);
            Assert.That(_skills.IsRetrievingHook,Is.True); Assert.That(_skills.BlocksCombat,Is.True);
            Assert.That(_skills.IsHoldingHook,Is.False);
            _skills.ApplyControl(.3f,false,false);
            Assert.That(_skills.IsHookActive,Is.False); Assert.That(_skills.IsRetrievingHook,Is.False);
        }
        [Test] public void HookVictimLocksCombatAndFacesCasterUntilExpiryOrCancellation()
        {
            var caster=Object.Instantiate(_player);
            try
            {
                EditorTestLifecycle.BindNetwork(caster); caster.transform.position=_player.transform.position+Vector3.right*4;
                _skills.ApplyHookPull(caster.GetComponent<ExpandedSkillController>(),.6f);
                Assert.That(_skills.IsBeingHooked && _skills.LookLocked && _skills.BlocksCombat,Is.True);
                Assert.That(_skills.ControlFlags & (SkillInputLockFlags.Move|SkillInputLockFlags.Attack),Is.EqualTo(SkillInputLockFlags.Move|SkillInputLockFlags.Attack));
                Assert.That(Vector3.Dot(_skills.RestrictRotation(Quaternion.identity)*Vector3.forward,Vector3.right),Is.GreaterThan(.999f));
                Assert.That(_skills.TryGetHookLookPoint(out var point),Is.True);
                Assert.That(point,Is.EqualTo(caster.transform.position+Vector3.up*1.2f));
                Set(_skills,"_hookedUntil",_skills.Now-1); Set(_skills,"_stunnedUntil",_skills.Now-1);
                Assert.That(_skills.IsBeingHooked || _skills.LookLocked || _skills.BlocksCombat,Is.False);
                _skills.ApplyHookPull(caster.GetComponent<ExpandedSkillController>(),.6f); _skills.CancelForLoadout();
                Assert.That(_skills.TryGetHookLookPoint(out _),Is.False);
            }
            finally { Object.DestroyImmediate(caster); }
        }
        [Test] public void ChargeReachesMaximumAtTwoPointFiveAndExpiresAtFourSeconds()
        {
            Assert.That(ExpandedSkillController.Value(JobSkillKind.Charge,"AccelerationSeconds"),Is.EqualTo(2.5f));
            Equip(0,JobSkillKind.Charge); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.Read(JobSkillKind.Charge).ActiveUntil-_skills.Now,Is.EqualTo(4).Within(.1));
        }
        [Test] public void KnifeModelGripsItsHandleAndPointsAlongTheProjectileAxis()
        {
            var knife=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Skills/Knife.prefab"));
            try
            {
                var filter=knife.GetComponentInChildren<MeshFilter>(); var mesh=filter.sharedMesh;
                var vertices=mesh.vertices; var indices=mesh.GetIndices(0);
                Bounds grip=new Bounds(vertices[indices[0]],Vector3.zero);
                foreach(int index in indices) grip.Encapsulate(vertices[index]);
                Assert.That(Vector3.Distance(filter.transform.TransformPoint(grip.center),knife.transform.position),Is.LessThan(.001f));
                Assert.That(Vector3.Dot(filter.transform.TransformDirection(Vector3.forward),knife.transform.forward),Is.GreaterThan(.999f));
                Assert.That(filter.GetComponent<Renderer>().bounds.size.z,Is.EqualTo(.38f).Within(.002));
            }
            finally { Object.DestroyImmediate(knife); }
        }
        [Test] public void TrapPreviewCanCancelWithoutSpendingCooldownOrStolenCopy()
        {
            Equip(5,JobSkillKind.Steal); _skills.CopiedKind=(int)JobSkillKind.Trap;
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.TrapReady,Is.True); Assert.That(_skills.Traps.Count,Is.Zero);
            Assert.That(_skills.CopiedKind,Is.EqualTo((int)JobSkillKind.Trap));
            Assert.That(_skills.Read(JobSkillKind.Steal).CooldownUntil,Is.Zero);
            Set(_skills,"_nextUse",0d); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
            Assert.That(_skills.TrapReady,Is.False); Assert.That(_skills.CopiedKind,Is.EqualTo((int)JobSkillKind.Trap));
        }
        [Test] public void InterruptedCastCannotFinishOrClearTheNextSkillsLock()
        {
            var hook=(System.Collections.IEnumerator)Call(_skills,"Hook",Vector3.forward,1);
            var trap=(System.Collections.IEnumerator)Call(_skills,"PlaceTrap",Vector3.forward,1);
            Assert.That(hook.MoveNext(),Is.True); Assert.That(trap.MoveNext(),Is.True);
            Set(_skills,"_hookCast",2); Set(_skills,"_trapCast",2); Set(_skills,"_busyUntil",_skills.Now+5);
            Assert.That(hook.MoveNext(),Is.False); Assert.That(trap.MoveNext(),Is.False);
            Assert.That(_skills.BlocksCombat,Is.True); Assert.That(_skills.Traps.Count,Is.Zero);
        }
        [Test] public void TrapRejectsWallsCliffsAndArbitraryClientPositions()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                _player.transform.position=new Vector3(300,10,300);
                _player.transform.rotation=Quaternion.identity;
                floor.transform.position=_player.transform.position+Vector3.down*.5f; floor.transform.localScale=new Vector3(10,1,10);
                wall.transform.position=_player.transform.position+Vector3.right*20;
                Equip(4,JobSkillKind.Trap); Physics.SyncTransforms();
                Assert.That(_skills.TryGetTrapPlacement(out var point),Is.True);
                Assert.That(Vector3.Distance(point,_player.transform.position),Is.LessThan(1.3f));
                Assert.That(_skills.ConfirmTrap(point),Is.False,"A valid point alone cannot bypass readiness.");
                Assert.That(_skills.TryUse(0,Vector3.forward),Is.True);
                Assert.That(_skills.ConfirmTrap(point+Vector3.forward*3),Is.False);
                Assert.That(_skills.ConfirmTrap(new Vector3(float.NaN,0,0)),Is.False);
                wall.transform.position=_player.transform.position+Vector3.up*.5f+Vector3.forward*.6f;
                wall.transform.localScale=new Vector3(2,1,.1f); Physics.SyncTransforms();
                Assert.That(_skills.TryGetTrapPlacement(out _),Is.False,"Cannot place through a wall.");
                wall.SetActive(false); floor.transform.localScale=new Vector3(.4f,1,.4f); floor.transform.position=point+Vector3.down*.53f; Physics.SyncTransforms();
                Assert.That(_skills.TryGetTrapPlacement(out _),Is.False,"A center point on a ledge is insufficient support.");
                Assert.That(_skills.TrapReady,Is.True); Assert.That(_skills.Read(JobSkillKind.Trap).CooldownUntil,Is.Zero);
            }
            finally { Object.DestroyImmediate(floor); Object.DestroyImmediate(wall); }
        }
        [Test] public void CopiedBerserkCanBeTurnedOffWithTheStealSlot()
        {
            Equip(5,JobSkillKind.Steal); _skills.CopiedKind=(int)JobSkillKind.Berserk;
            Assert.That(_skills.TryUse(0,Vector3.forward),Is.True); Assert.That(_skills.Berserking,Is.True);
            Assert.That(_skills.CopiedKind,Is.EqualTo(-1)); Assert.That(_skills.Read(JobSkillKind.Steal).CooldownUntil,Is.GreaterThan(_skills.Now));
            Set(_skills,"_nextUse",0d); Assert.That(_skills.TryUse(0,Vector3.forward),Is.True); Assert.That(_skills.Berserking,Is.False);
        }
        [Test] public void PlacedTrapsSurviveSkillLoadoutIdentityAndDeathCancellation()
        {
            for(int i=1;i<=4;i++) _skills.Traps[i]=new SkillTrapSnapshot { Position=Vector3.right*i,ExpiresAt=_skills.Now+60+i };
            _skills.CancelForLoadout();
            Call(_skills,"IdentityChanged",_stats.CurrentIdentity);
            Call(_skills,"Cancel");
            Call(_skills,"UpdateTraps");
            Assert.That(_skills.Traps.Count,Is.EqualTo(4));
            Assert.That(_skills.Traps[1].ExpiresAt,Is.LessThan(_skills.Traps[4].ExpiresAt));
            var visuals=(SkillExpansionVisuals)typeof(ExpandedSkillController).GetField("_visuals",Private).GetValue(_skills);
            var rendered=(System.Collections.Generic.Dictionary<int,GameObject>)typeof(SkillExpansionVisuals).GetField("_traps",Private).GetValue(visuals);
            Assert.That(rendered.Count,Is.EqualTo(4));
        }
        [Test] public void ClosingOneTrapLeavesTheOtherExpiryAndRejectsRepeatedHits()
        {
            var trap=SkillTrap.Create(_skills,Vector3.zero,_skills.Now+60);
            _skills.Traps[trap.Id]=new SkillTrapSnapshot { ExpiresAt=trap.ExpiresAt };
            _skills.Traps[999]=new SkillTrapSnapshot { ExpiresAt=_skills.Now+80 };
            double later=_skills.Traps[999].ExpiresAt; int id=trap.Id;
            _skills.CloseTrap(trap);
            Assert.That(_skills.Traps[id].Closed,Is.True);
            Assert.That(_skills.Traps[999].ClosedAt,Is.Zero);
            Assert.That(_skills.Traps[999].ExpiresAt,Is.EqualTo(later));
            var closed=_skills.Traps[id]; closed.ClosedAt=_skills.Now-SkillTrapVisual.CloseLifetime-1; _skills.Traps[id]=closed;
            Call(_skills,"UpdateTraps"); Assert.That(_skills.Traps.ContainsKey(id),Is.False);
        }
        [Test] public void TrapJawsCloseWithoutMovingTheGroundedBaseAndHandleMissingSnapshot()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Skills/Trap.prefab"));
            try
            {
                var visual=go.GetComponent<SkillTrapVisual>(); Assert.That(visual,Is.Not.Null);
                var left=go.transform.Find("TrapLeftJaw"); var right=go.transform.Find("TrapRightJaw"); var plate=go.transform.Find("TrapBase");
                var position=plate.position; visual.Render(10,11);
                Assert.That(Quaternion.Angle(left.localRotation,Quaternion.identity),Is.LessThan(.01f));
                visual.Render(11+SkillTrapVisual.CloseSeconds,11);
                Assert.That(Quaternion.Angle(left.localRotation,Quaternion.identity),Is.EqualTo(78).Within(.01));
                Assert.That(Quaternion.Angle(right.localRotation,Quaternion.identity),Is.EqualTo(78).Within(.01));
                Assert.That(plate.position,Is.EqualTo(position));
                Assert.That(visual.RenderRemoved(11.2),Is.False);
                Assert.That(visual.RenderRemoved(12),Is.True);
                Assert.That(go.transform.localScale,Is.EqualTo(Vector3.zero));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void CancellationRemovesDiceVisualsAndAllowsTheNextLifeToRender()
        {
            Active(JobSkillKind.Dice); Set(_skills,"_diceFace",4); Call(_skills,"Update");
            Assert.That(GameObject.Find("Skill Dice Result"),Is.Not.Null);
            _skills.CancelForLoadout(); Assert.That(GameObject.Find("Skill Dice Result"),Is.Null);
            Active(JobSkillKind.Dice); Call(_skills,"Update"); Assert.That(GameObject.Find("Skill Dice Result"),Is.Not.Null);
        }
        [TestCase(false)] [TestCase(true)] public void KnifeAddsOnePoisonStackEvenWithCoating(bool coating)
        {
            var combat=_player.GetComponent<PlayerCombat>();
            Set(combat,"_monostatAgiSkillActiveUntil",coating ? _skills.Now+10 : 0d);
            combat.NotifyPhysicalDamageDealt(1,_health,Vector3.zero);
            combat.AddKnifePoison(_health,Vector3.zero);
            var poison=(PoisonStackCollection<IDamageReceiver,Vector3>)typeof(PlayerCombat).GetField("_poisonStacks",Private).GetValue(combat);
            var ticks=new System.Collections.Generic.List<PoisonTick<IDamageReceiver,Vector3>>();
            poison.CollectTicks(_skills.Now,6,_=>true,ticks);
            Assert.That(ticks,Has.Count.EqualTo(1)); Assert.That(ticks[0].Damage,Is.EqualTo(6));
        }
    }
}
