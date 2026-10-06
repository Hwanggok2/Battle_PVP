using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.EditorData;
using BattlePvp.Logic;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterAppearanceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> _temporary = new();
        private Scene _scene;
        private CharacterCatalog _catalog, _originalCatalog;
        private CharacterDefinition _variant;
        private GameObject _player;
        private PlayerAppearance _appearance;
        private SkinnedMeshRenderer _body;
        private string _preferenceKey, _savedPreference;
        private bool _hadPreference;

        [SetUp] public void Setup()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(_scene); _scene.name = "Lobby";
            _originalCatalog = CharacterCatalog.Instance;
            _catalog = Keep(Object.Instantiate(_originalCatalog));
            typeof(CharacterCatalog).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _catalog);
            _player = Player(); _appearance = _player.GetComponent<PlayerAppearance>();
            _body = _player.GetComponentInChildren<SkinnedMeshRenderer>();
            var source = new GameObject("Variant source").AddComponent<SkinnedMeshRenderer>();
            source.sharedMesh = Keep(Object.Instantiate(_body.sharedMesh)); source.bones = _body.bones;
            source.localBounds = _body.localBounds; source.sharedMaterials = _body.sharedMaterials;
            _variant = Keep(ScriptableObject.CreateInstance<CharacterDefinition>());
            _variant.Id = "test-variant"; _variant.DisplayName = "검증용 외형"; _variant.Body = source;
            var material = Keep(new Material(_body.sharedMaterial)); material.color = Color.cyan;
            _variant.Materials = Enumerable.Repeat(material, source.sharedMesh.subMeshCount).ToArray();
            _catalog.Characters = new[] { _originalCatalog.Find(CharacterCatalog.DefaultId), _variant };
            _preferenceKey = "BattlePvp.Character.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
            _hadPreference = PlayerPrefs.HasKey(_preferenceKey); _savedPreference = PlayerPrefs.GetString(_preferenceKey);
        }
        [TearDown] public void Cleanup()
        {
            if (CharacterSelectionPanel.Instance != null)
            {
                var panel = CharacterSelectionPanel.Instance;
                foreach (var preview in panel.GetComponentsInChildren<CharacterPreview>(true)) EditorTestLifecycle.Invoke(preview, "OnDestroy");
                EditorTestLifecycle.Invoke(panel, "OnDisable"); EditorTestLifecycle.Invoke(panel, "OnDestroy");
            }
            if (GameInputController.Instance != null)
            { EditorTestLifecycle.Invoke(GameInputController.Instance, "OnDisable"); EditorTestLifecycle.Invoke(GameInputController.Instance, "OnDestroy"); }
            foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            foreach (var value in _temporary) if (value != null) Object.DestroyImmediate(value);
            _temporary.Clear();
            typeof(CharacterCatalog).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _originalCatalog);
            if (_hadPreference) PlayerPrefs.SetString(_preferenceKey, _savedPreference); else PlayerPrefs.DeleteKey(_preferenceKey);
            PlayerPrefs.Save();
        }
        private T Keep<T>(T value) where T : Object { _temporary.Add(value); return value; }
        private GameObject Player()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAppearanceInstaller.PlayerPath));
            EditorTestLifecycle.BindNetwork(player);
            EditorTestLifecycle.Invoke(player.GetComponent<PlayerAppearance>(), "Awake");
            return player;
        }
        private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
        private bool ServerSelect(string id, bool initial)
        {
            object[] args = { id, initial, null };
            return (bool)typeof(PlayerAppearance).GetMethod("TrySelectOnServer", Private).Invoke(_appearance, args);
        }

        [Test] public void CatalogRejectsAmbiguousIdsAndFallsBackForRemovedResources()
        {
            Assert.That(_catalog.ResolveId("removed"), Is.EqualTo(CharacterCatalog.DefaultId));
            var duplicate = Keep(Object.Instantiate(_variant));
            _catalog.Characters = new[] { _variant, duplicate };
            Assert.That(_catalog.Find(_variant.Id), Is.Null);
            Assert.That(_catalog.Find(new string('x', 65)), Is.Null);
        }
        [Test] public void SwappingAndRestoringPreservesRigWeaponsHitboxesAndHiddenFlags()
        {
            var animator = _player.GetComponent<Animator>(); var controller = animator.runtimeAnimatorController;
            var transforms = _player.GetComponentsInChildren<Transform>(true);
            var colliders = _player.GetComponentsInChildren<Collider>(true);
            var colliderStates = colliders.Select(c => c.enabled).ToArray();
            var originalMesh = _body.sharedMesh; var skin = new CharacterSkin(_body);
            _body.enabled = false; _body.forceRenderingOff = true;
            Assert.That(skin.Apply(_variant, out string error), Is.True, error);
            Assert.That(_body.sharedMesh, Is.SameAs(_variant.Body.sharedMesh));
            Assert.That(_body.sharedMaterial, Is.SameAs(_variant.Materials[0]));
            Assert.That(_body.enabled, Is.False); Assert.That(_body.forceRenderingOff, Is.True);
            Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
            CollectionAssert.AreEqual(transforms, _player.GetComponentsInChildren<Transform>(true));
            CollectionAssert.AreEqual(colliderStates, colliders.Select(c => c.enabled));
            skin.Restore(); Assert.That(_body.sharedMesh, Is.SameAs(originalMesh));
        }
        [Test] public void IncompatibleBindPoseIsRejectedWithoutPartiallyChangingTheBody()
        {
            var mesh = _body.sharedMesh; var material = _body.sharedMaterial;
            var poses = _variant.Body.sharedMesh.bindposes; poses[0].m03 += 1;
            _variant.Body.sharedMesh.bindposes = poses;
            Assert.That(new CharacterSkin(_body).Apply(_variant, out string error), Is.False);
            Assert.That(error, Does.Contain("바인드 포즈"));
            Assert.That(_body.sharedMesh, Is.SameAs(mesh)); Assert.That(_body.sharedMaterial, Is.SameAs(material));
        }
        [Test] public void MissingModelsAndMaterialsCannotBeApplied()
        {
            _variant.Body = null;
            Assert.That(_appearance.Request(_variant.Id, out _), Is.False);
            _variant.UseDefaultBody = true; _variant.Materials = new Material[] { null };
            Assert.That(_appearance.Request(_variant.Id, out _), Is.False);
            Assert.That(_appearance.SelectedId, Is.EqualTo(CharacterCatalog.DefaultId));
        }
        [Test] public void OfflineSelectionSurvivesPlayerReplacementAndUnknownSavedIdsFallBack()
        {
            Assert.That(_appearance.Request(_variant.Id, out string error), Is.True, error);
            Assert.That(CharacterAppearanceStore.Read(), Is.EqualTo(_variant.Id));
            var replacement = Player().GetComponent<PlayerAppearance>(); EditorTestLifecycle.Invoke(replacement, "Start");
            Assert.That(replacement.SelectedId, Is.EqualTo(_variant.Id));
            PlayerPrefs.SetString(_preferenceKey, "removed");
            Assert.That(CharacterAppearanceStore.Read(), Is.EqualTo(CharacterCatalog.DefaultId));
        }
        [TestCase("Battle")]
        [TestCase("Login")]
        public void NonEditableScenesRejectLocalChanges(string scene)
        {
            _scene.name = scene;
            Assert.That(_appearance.Request(_variant.Id, out _), Is.False);
        }
        [Test] public void ServerAllowsOnlyOneInitialSubmissionDuringBattle()
        {
            _scene.name = "Battle"; _appearance.OnStartServer();
            Assert.That(ServerSelect(_variant.Id, true), Is.True);
            Set(_appearance, "_nextRequest", 0d);
            Assert.That(ServerSelect(CharacterCatalog.DefaultId, true), Is.False);
            Assert.That(ServerSelect(CharacterCatalog.DefaultId, false), Is.False);
            Assert.That(_appearance.SelectedId, Is.EqualTo(_variant.Id));
        }
        [Test] public void ServerSelectionCompletesCombatReadinessWithoutChangingStatReceipt()
        {
            var stats = _player.GetComponent<BattlePvp.Stats.StatManager>();
            Set(stats, "_serverStatsInitialized", true);
            Assert.That(_appearance.HasServerSelectionReady, Is.True, "Standalone scene players do not wait for a network selection.");
            _appearance.OnStartServer();
            Assert.That(stats.HasServerStats, Is.True, "Profile receipt and combat readiness are different states.");
            Assert.That(stats.HasServerCombatStats, Is.False);
            Assert.That(ServerSelect(_variant.Id, false), Is.True);
            Assert.That(_appearance.HasServerSelectionReady, Is.True);
            Assert.That(stats.HasServerCombatStats, Is.True);
            _scene.name = "Battle"; Set(_appearance, "_nextRequest", 0d);
            Assert.That(ServerSelect(CharacterCatalog.DefaultId, true), Is.False, "A waiting-room selection consumes the initial submission.");
        }
        [Test] public void SelectionDeadlineSealsTheDefaultBeforeAnyDelayedBattleSubmission()
        {
            _appearance.OnStartServer();
            _scene.name = "Battle";
            Set(_appearance, "_initialDeadline", NetworkTime.time - 1d);
            Assert.That(ServerSelect(_variant.Id, true), Is.False);
            Assert.That(_appearance.HasServerSelectionReady, Is.True);
            Assert.That(_appearance.SelectedId, Is.EqualTo(CharacterCatalog.DefaultId));
            Set(_appearance, "_nextRequest", 0d);
            Assert.That(ServerSelect(_variant.Id, true), Is.False);
        }
        [Test] public void MissingSelectionTimesOutEvenWithoutAnotherRequest()
        {
            _appearance.OnStartServer();
            double deadline = (double)typeof(PlayerAppearance).GetField("_initialDeadline", Private).GetValue(_appearance);
            var expire = typeof(PlayerAppearance).GetMethod("ExpireInitialSelection", Private);
            expire.Invoke(_appearance, new object[] { deadline - .01d });
            Assert.That(_appearance.HasServerSelectionReady, Is.False);
            expire.Invoke(_appearance, new object[] { deadline });
            Assert.That(_appearance.HasServerSelectionReady, Is.True);
            Assert.That(_appearance.SelectedId, Is.EqualTo(CharacterCatalog.DefaultId));
            _scene.name = "Battle";
            Assert.That(ServerSelect(_variant.Id, true), Is.False);
        }
        [Test] public void HostsPersonalLobbyDoesNotUnlockSelectionForPlayersStillInBattle()
        {
            _appearance.OnStartServer();
            Assert.That(ServerSelect(_variant.Id, false), Is.True);
            // The EditMode runner can disallow additive regular scenes. A preview scene
            // still gives the retained player a distinct scene while Lobby stays active.
            Scene battle = EditorSceneManager.NewPreviewScene();
            battle.name = "Battle";
            try
            {
                SceneManager.MoveGameObjectToScene(_player, battle);
                Set(_appearance, "_nextRequest", 0d);
                Assert.That(PlayerAppearance.CanEdit, Is.True, "The host's local lobby remains editable.");
                Assert.That(ServerSelect(CharacterCatalog.DefaultId, false), Is.False);
                Assert.That(_appearance.SelectedId, Is.EqualTo(_variant.Id));
            }
            finally { SceneManager.MoveGameObjectToScene(_player, _scene); EditorSceneManager.ClosePreviewScene(battle); }
        }
        [Test] public void PendingCharacterBlocksIncomingDamageCombatAndMovementReadiness()
        {
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Do not replace a live network session.");
            var serverMode = typeof(NetworkServer).GetProperty(nameof(NetworkServer.active));
            var identity = _player.GetComponent<NetworkIdentity>();
            var identityMode = typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer));
            var stats = _player.GetComponent<BattlePvp.Stats.StatManager>();
            var health = _player.GetComponent<HealthSystem>();
            var combat = _player.GetComponent<PlayerCombat>();
            var movement = _player.GetComponent<PlayerManager>();
            Set(stats, "_serverStatsInitialized", true);
            Set(health, "_statManager", stats); Set(health, "_maxHp", 100f); Set(health, "_currentHp", 100f);
            Set(combat, "_statManager", stats); Set(movement, "_statManager", stats);
            _appearance.OnStartServer();
            try
            {
                serverMode.SetValue(null, true); identityMode.SetValue(identity, true);
                Assert.That(stats.HasServerStats, Is.True);
                Assert.That(stats.HasServerCombatStats, Is.False);
                Assert.That(typeof(PlayerCombat).GetProperty("HasAuthoritativeCombatStats", Private).GetValue(combat), Is.False);
                var damage = health.ApplyDamage(new DamageRequest(50, DamageSource.Poison, 0, null, Vector3.zero));
                Assert.That(damage.Accepted, Is.False); Assert.That(health.CurrentHp, Is.EqualTo(100f));
                var validator = (ServerMovementValidator)typeof(PlayerManager).GetField("_serverMovement", Private).GetValue(movement);
                _player.transform.position = Vector3.zero;
                validator.Reset(Vector3.zero, NetworkTime.time - .1d);
                typeof(PlayerManager).GetMethod("RecordServerMovementControls", Private).Invoke(movement, null);
                typeof(PlayerManager).GetMethod("ApplySyncedTransformOnServer", Private).Invoke(movement,
                    new object[] { Vector3.forward * .25f, Quaternion.identity, NetworkTime.time, false });
                Assert.That(validator.Position, Is.EqualTo(Vector3.zero));
                Assert.That(_player.transform.position, Is.EqualTo(Vector3.zero));
            }
            finally { identityMode.SetValue(identity, false); serverMode.SetValue(null, false); }
        }
        [Test] public void ServerRejectsUnknownIdsAndDeadPlayers()
        {
            _appearance.OnStartServer(); Assert.That(ServerSelect("unknown", false), Is.False);
            Set(_appearance, "_nextRequest", 0d); Set(_player.GetComponent<HealthSystem>(), "_isDead", true);
            Assert.That(ServerSelect(_variant.Id, false), Is.False);
        }
        [Test] public void InitialNetworkSnapshotAndSubsequentDeltaApplyTheSameAppearance()
        {
            var client = Player().GetComponent<PlayerAppearance>();
            _appearance.OnStartServer(); Assert.That(ServerSelect(_variant.Id, false), Is.True);
            var writer = new NetworkWriter(); _appearance.OnSerialize(writer, true);
            client.OnDeserialize(new NetworkReader(writer.ToArraySegment()), true); client.OnStartClient();
            Assert.That(client.SelectedId, Is.EqualTo(_variant.Id));
            Assert.That(client.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, Is.SameAs(_variant.Body.sharedMesh));
            Set(_appearance, "_nextRequest", 0d); Assert.That(ServerSelect(CharacterCatalog.DefaultId, false), Is.True);
            writer = new NetworkWriter(); _appearance.OnSerialize(writer, false);
            client.OnDeserialize(new NetworkReader(writer.ToArraySegment()), false);
            Assert.That(client.SelectedId, Is.EqualTo(CharacterCatalog.DefaultId));
            Assert.That(client.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, Is.Not.SameAs(_variant.Body.sharedMesh));
        }
        [Test] public void SwitchingDuringLocalStealthRestoresTheNewMaterials()
        {
            using var stealth = new SkillStealthPresentation(_player.transform);
            stealth.Apply(true, true);
            Assert.That(_appearance.Request(_variant.Id, out _), Is.True);
            stealth.Apply(true, true);
            Assert.That(_body.sharedMaterial, Is.Not.SameAs(_variant.Materials[0]));
            stealth.Apply(false, true);
            Assert.That(_body.sharedMaterial, Is.SameAs(_variant.Materials[0]));
        }
        [Test] public void ActiveBuffAuraFollowsTheReplacementMeshWithoutDuplicatingShells()
        {
            var skills = _player.GetComponent<ExpandedSkillController>();
            EditorTestLifecycle.Invoke(_player.GetComponent<HealthSystem>(), "Awake");
            EditorTestLifecycle.Invoke(skills, "Awake");
            skills.States[(int)JobSkillKind.Fortify] = new SkillRuntime { ActiveUntil = skills.Now + 10 };
            using var aura = new SkillBuffAura(); aura.Tick(skills, false);
            var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Skill buff body glow");
            Assert.That(_appearance.Request(_variant.Id, out _), Is.True);
            aura.Tick(skills, false);
            Assert.That(glow.sharedMesh, Is.SameAs(_variant.Body.sharedMesh));
            CollectionAssert.AreEqual(_body.bones, glow.bones);
            Assert.That(glow.sharedMaterials.Length, Is.EqualTo(_body.sharedMesh.subMeshCount));
            Assert.That(_player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r.name == "Skill buff body glow"), Is.EqualTo(1));
        }
        [Test] public void MenuPausesInputEscapeClosesAndPreviewHasNoGameplayComponents()
        {
            var input = new GameObject("Input").AddComponent<GameInputController>(); EditorTestLifecycle.Invoke(input, "Awake");
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAppearanceInstaller.UiPath));
            var panel = ui.GetComponent<CharacterSelectionPanel>(); EditorTestLifecycle.Invoke(panel, "Awake");
            panel.Open();
            Assert.That(CharacterSelectionPanel.IsOpen, Is.True); Assert.That(GameInputController.IsPaused, Is.True);
            var stage = GameObject.Find("Character preview stage"); Assert.That(stage, Is.Not.Null);
            Assert.That(stage.GetComponentsInChildren<NetworkIdentity>(), Is.Empty);
            Assert.That(stage.GetComponentsInChildren<PlayerCombat>(), Is.Empty);
            GameInputController.HandleEscape();
            Assert.That(CharacterSelectionPanel.IsOpen, Is.False); Assert.That(GameInputController.IsPaused, Is.False);
        }

        [Test] public void NativeSkinsReplaceAuraBodiesAndRecoverFromStealth()
        {
            _catalog.Characters = _originalCatalog.Characters;
            var skills = _player.GetComponent<ExpandedSkillController>();
            EditorTestLifecycle.Invoke(_player.GetComponent<HealthSystem>(), "Awake");
            EditorTestLifecycle.Invoke(skills, "Awake");
            skills.States[(int)JobSkillKind.Fortify] = new SkillRuntime { ActiveUntil = skills.Now + 30 };
            using var aura = new SkillBuffAura();
            using var stealth = new SkillStealthPresentation(_player.transform);
            aura.Tick(skills, false);
            foreach (var id in new[] { "brute", "picochan", "default" })
            {
                stealth.Apply(true, false);
                Assert.That(_appearance.Request(id, out var error), Is.True, error);
                stealth.Apply(true, false); aura.Tick(skills, true);
                var body = _player.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Single(r => r.sharedMesh != null && !r.name.StartsWith("Skill buff"));
                Assert.That(body.forceRenderingOff, Is.True);
                stealth.Apply(false, false);
                // Complete the visual follower's LateUpdate before checking the next frame's aura.
                foreach (var follower in _player.GetComponentsInChildren<CharacterPoseFollower>()) follower.SyncPose();
                aura.Tick(skills, false);
                var glow = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Skill buff body glow");
                Assert.That(glow.sharedMesh, Is.SameAs(body.sharedMesh));
                Assert.That(body.forceRenderingOff, Is.False); Assert.That(glow.enabled, Is.True);
                CollectionAssert.AreEqual(body.bones, glow.bones);
            }
        }
    }
}
