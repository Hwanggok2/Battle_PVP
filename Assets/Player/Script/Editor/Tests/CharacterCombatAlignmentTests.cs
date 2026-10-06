using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;
using BodyPart = BattlePvp.Combat.BodyPart;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterCombatAlignmentTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static CharacterCatalog Catalog => AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");

        [TestCase("megumi", 1f)] [TestCase("brute", 1f)]
        [TestCase("security-officer", 1f)] [TestCase("casual-1", 1f)] [TestCase("picochan", 1f)]
        [TestCase("megumi", 1.2f)] [TestCase("brute", 1.2f)]
        [TestCase("security-officer", 1.2f)] [TestCase("casual-1", 1.2f)] [TestCase("picochan", 1.2f)]
        public void VisibleHeadMatchesItsColliderAndServerHistory(string id, float scale)
        {
            var player = CreatePlayer(scale);
            try
            {
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                skin.SyncPose();
                var animator = player.GetComponentInChildren<CharacterPoseFollower>().GetComponentInChildren<Animator>();
                var point = animator.GetBoneTransform(HumanBodyBones.Head).position + player.transform.up * (.06f * scale);
                var head = player.GetComponentsInChildren<HitBodyPart>(true).Single(p => p.Part == BodyPart.Head).GetComponent<Collider>();
                Physics.SyncTransforms();
                Assert.That(Vector3.Distance(head.ClosestPoint(point), point), Is.LessThan(.015f * scale),
                    "The visible head must be inside the damage collider.");
                var historyType = typeof(PlayerCombat).Assembly.GetType("ServerPoseHistory");
                var history = player.GetComponent(historyType) ?? player.AddComponent(historyType);
                historyType.GetMethod("RefreshBodyParts").Invoke(history, null);
                historyType.GetMethod("RecordPose", Private).Invoke(history, new object[] { 10d, player.transform.position, true });
                var validate = historyType.GetMethod("TryValidateBodyPart");
                object[] actual = { 10d, BodyPart.Head, point, Vector3.zero, 0f };
                Assert.That((bool)validate.Invoke(history, actual), Is.True);
                Assert.That((float)actual[4], Is.EqualTo(1.5f));
                object[] missed = { 10d, BodyPart.Head, point + Vector3.up, Vector3.zero, 0f };
                Assert.That((bool)validate.Invoke(history, missed), Is.False);
            }
            finally { Object.DestroyImmediate(player); }
        }

        [TestCase("picochan", "brute", 1f)] [TestCase("megumi", "brute", 1.2f)]
        [TestCase("security-officer", "brute", 1f)] [TestCase("casual-1", "brute", 1f)]
        [TestCase("brute", "picochan", 1f)]
        public void VisibleSwordContactDealsHeadDamageOnceAndIsRecordedByServer(string attackerId, string defenderId, float defenderScale)
        {
            var attacker = CreatePlayer(1); var defender = CreatePlayer(defenderScale);
            var processorOwner = new GameObject("Isolated damage processor");
            var attack = ScriptableObject.CreateInstance<AttackData>(); attack.damage = 1;
            var serverActive = typeof(Mirror.NetworkServer).GetProperty("active");
            bool wasServer = Mirror.NetworkServer.active;
            try
            {
                using var attackerSkin = new CharacterSkin(attacker.GetComponentInChildren<SkinnedMeshRenderer>());
                using var defenderSkin = new CharacterSkin(defender.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(attackerSkin.Apply(Catalog.Find(attackerId), out var error), Is.True, error);
                Assert.That(defenderSkin.Apply(Catalog.Find(defenderId), out error), Is.True, error);
                var sword = attacker.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Sword");
                sword.gameObject.SetActive(true); attackerSkin.SyncPose(); defenderSkin.SyncPose();
                var fitted = sword.GetComponentsInChildren<MeshFilter>().Single(f => f.sharedMesh != null);
                var box = sword.GetComponent<BoxCollider>();
                var blade = sword.GetComponent<MeleeHitBox>();
                EditorTestLifecycle.Invoke(blade, "Awake");
                var headBone = defender.GetComponentInChildren<CharacterPoseFollower>().GetComponentInChildren<Animator>()
                    .GetBoneTransform(HumanBodyBones.Head);
                var headPoint = headBone.position + defender.transform.up * (.06f * defenderScale);
                // Arrange a contact of the actual rendered blade with the actual native head.
                // Animation timing is covered separately by the swept-blade tests.
                attacker.transform.position += headPoint - fitted.transform.TransformPoint(box.center);
                attacker.GetComponent<Animator>().Update(0);
                attackerSkin.SyncPose();
                Assert.That(Vector3.Distance(fitted.transform.TransformPoint(box.center), headPoint), Is.LessThan(.005f));

                processorOwner.AddComponent<Mirror.NetworkIdentity>();
                var attackerStats = processorOwner.AddComponent<StatManager>();
                var values = new StatContainer(); values.STR.Invested = 10;
                Set(attackerStats, "_stats", values);
                var processor = processorOwner.AddComponent<AttackProcessor>();
                EditorTestLifecycle.Invoke(processor, "Awake");
                Set(blade, "_attackProcessor", processor); Set(blade, "_playerCombat", null);
                EditorTestLifecycle.BindNetwork(defender);
                var health = defender.GetComponent<HealthSystem>();
                EditorTestLifecycle.Invoke(health, "Awake");
                Set(health, "_currentHp", 1000f); Set(health, "_maxHp", 1000f);
                processor.ProcessHit(attack, defender.GetComponent<StatManager>(), health, headPoint);
                float bodyDamage = 1000f - health.CurrentHp;
                Assert.That(bodyDamage, Is.GreaterThan(0));
                Set(health, "_currentHp", 1000f);
                blade.SetAttackData(attack); blade.EnableHitBox();
                Physics.SyncTransforms(); EditorTestLifecycle.Invoke(blade, "LateUpdate");
                float damage = 1000f - health.CurrentHp;
                Assert.That(damage, Is.GreaterThan(0), "Visible native-sword contact must lower the target's HP.");
                Assert.That(damage, Is.EqualTo(bodyDamage * 1.5f).Within(.01f), "Contact with the visible head must retain the headshot multiplier.");
                EditorTestLifecycle.Invoke(blade, "LateUpdate");
                Assert.That(1000f - health.CurrentHp, Is.EqualTo(damage), "One swing must not damage twice.");

                // The same query poses must back the server's validation, without increasing tolerance.
                serverActive.SetValue(null, true);
                EditorTestLifecycle.Invoke(blade, "LateUpdate");
                Assert.That(blade.ValidateServerHit(headPoint, Mirror.NetworkTime.time, attacker.transform.position), Is.True);
                Assert.That(blade.ValidateServerHit(headPoint + Vector3.up * 3, Mirror.NetworkTime.time, attacker.transform.position), Is.False);
            }
            finally
            {
                serverActive.SetValue(null, wasServer);
                Object.DestroyImmediate(attack); Object.DestroyImmediate(processorOwner);
                Object.DestroyImmediate(attacker); Object.DestroyImmediate(defender);
            }
        }

        [TestCase("brute")] [TestCase("picochan")] [TestCase("casual-1")]
        public void SwitchingAndRestoringPreservesColliderIdentityAndAuthoredSettings(string id)
        {
            var player = CreatePlayer(1.2f);
            try
            {
                var shapes = player.GetComponentsInChildren<HitBodyPart>().Select(p => p.GetComponent<CapsuleCollider>()).ToArray();
                var parents = shapes.Select(c => c.transform.parent).ToArray();
                var positions = shapes.Select(c => c.transform.localPosition).ToArray();
                var rotations = shapes.Select(c => c.transform.localRotation).ToArray();
                var centers = shapes.Select(c => c.center).ToArray();
                var heights = shapes.Select(c => c.height).ToArray(); var radii = shapes.Select(c => c.radius).ToArray();
                var enabled = shapes.Select(c => c.enabled).ToArray();
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                Assert.That(skin.Apply(Catalog.Find("security-officer"), out error), Is.True, error);
                skin.Restore();
                CollectionAssert.AreEqual(shapes, player.GetComponentsInChildren<HitBodyPart>().Select(p => p.GetComponent<CapsuleCollider>()));
                for (int i = 0; i < shapes.Length; i++)
                {
                    Assert.That(shapes[i].transform.parent, Is.SameAs(parents[i]));
                    Assert.That(Vector3.Distance(shapes[i].transform.localPosition, positions[i]), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(shapes[i].transform.localRotation, rotations[i]), Is.LessThan(.01f));
                    Assert.That(shapes[i].center, Is.EqualTo(centers[i]));
                    Assert.That(shapes[i].height, Is.EqualTo(heights[i])); Assert.That(shapes[i].radius, Is.EqualTo(radii[i]));
                    Assert.That(shapes[i].enabled, Is.EqualTo(enabled[i]));
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [TestCase("megumi", "Attack/Attack")] [TestCase("brute", "Attack/Attack")]
        [TestCase("security-officer", "Attack/Attack")] [TestCase("casual-1", "Attack/Attack")] [TestCase("picochan", "Attack/Attack")]
        [TestCase("megumi", "Move/Crouch Walk")] [TestCase("brute", "Move/Crouch Walk")]
        [TestCase("security-officer", "Move/Crouch Walk")] [TestCase("casual-1", "Move/Crouch Walk")] [TestCase("picochan", "Move/Crouch Walk")]
        public void NativeDamageRegionsFollowAttackAndCrouchPoses(string id, string motion)
        {
            var player = CreatePlayer(1);
            var graph = PlayableGraph.Create("Native damage region validation");
            try
            {
                var driver = player.GetComponent<Animator>();
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/" + motion + ".anim");
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Pose", driver).SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(.2f * clip.length); graph.Evaluate(0);
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                var native = player.GetComponentInChildren<CharacterPoseFollower>().GetComponentInChildren<Animator>();
                var parts = player.GetComponentsInChildren<HitBodyPart>();
                foreach (float phase in new[] { .2f, .6f, .9f })
                {
                    playable.SetTime(phase * clip.length); graph.Evaluate(0); skin.SyncPose(); Physics.SyncTransforms();
                    var points = new[]
                    {
                        native.GetBoneTransform(HumanBodyBones.Head).position + player.transform.up * .06f,
                        native.GetBoneTransform(HumanBodyBones.Spine).position,
                        (native.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position + native.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position) * .5f
                    };
                    var regions = new[] { BodyPart.Head, BodyPart.Body, BodyPart.Legs };
                    for (int i = 0; i < regions.Length; i++)
                    {
                        var point = points[i]; var region = regions[i];
                        float miss = parts.Where(p => p.Part == region).Min(p => Vector3.Distance(p.GetComponent<Collider>().ClosestPoint(point), point));
                        Assert.That(miss, Is.LessThan(.02f), region + " at " + phase + " must follow its visible bones.");
                    }
                }
            }
            finally { graph.Destroy(); Object.DestroyImmediate(player); }
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);

        private static GameObject CreatePlayer(float scale)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            player.transform.SetPositionAndRotation(new Vector3(5100, 5200, 5300), Quaternion.Euler(0, 37, 0));
            player.transform.localScale = Vector3.one * scale;
            var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0);
            return player;
        }
    }
}
