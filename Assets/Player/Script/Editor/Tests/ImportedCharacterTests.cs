using System.Linq;
using BattlePvp.Characters;
using BattlePvp.EditorData;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class ImportedCharacterTests
    {
        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void ImportedSkinPreservesGameplayObjectsAndAnimatesItsNativeSkeleton(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");
            var definition = catalog.Find(id);
            Assert.That(definition, Is.Not.Null, id);
            Assert.That(definition.Portrait, Is.Not.Null, "Missing selector portrait");
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAppearanceInstaller.PlayerPath));
            var baked = new Mesh();
            PlayableGraph graph = default;
            try
            {
                var animator = player.GetComponent<Animator>();
                Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate),
                    "The gameplay pose must update when its original mesh is replaced by a separate visual skeleton.");
                var controller = animator.runtimeAnimatorController;
                var body = player.GetComponentInChildren<SkinnedMeshRenderer>();
                var originalMesh = body.sharedMesh;
                var transforms = player.GetComponentsInChildren<Transform>(true);
                var colliders = player.GetComponentsInChildren<Collider>(true);
                var skin = new CharacterSkin(body);
                Assert.That(skin.Apply(definition, out string error), Is.True, error);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
                CollectionAssert.IsSubsetOf(transforms, player.GetComponentsInChildren<Transform>(true));
                CollectionAssert.AreEqual(colliders, player.GetComponentsInChildren<Collider>(true));
                Assert.That(body.sharedMesh, Is.Null, "The gameplay renderer must not receive a different vertex layout.");
                var originalBody = body;
                body = skin.VisibleBody;
                Assert.That(body.sharedMaterials.Length, Is.EqualTo(body.sharedMesh.subMeshCount));
                Assert.That(body.sharedMaterials.All(m => m != null && m.shader.name == "Universal Render Pipeline/Lit"), Is.True);
                foreach (var weight in body.sharedMesh.boneWeights)
                {
                    Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1).Within(.002));
                    Assert.That(new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 }
                        .All(i => i >= 0 && i < body.bones.Length), Is.True);
                }
                graph = PlayableGraph.Create("Imported skin validation");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                animator.applyRootMotion = false;
                foreach (string clipPath in new[] { "Move/Idle.anim", "Move/Walk.anim", "Attack/Attack.anim", "Skill/Bow_AimHold3.anim", "Die.anim" })
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/" + clipPath);
                    Assert.That(clip, Is.Not.Null);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    output.SetSourcePlayable(playable); graph.Play();
                    foreach (float fraction in new[] { .15f, .5f, .85f })
                    {
                        playable.SetTime(clip.length * fraction); graph.Evaluate(0);
                        skin.SyncPose();
                        body.BakeMesh(baked); baked.RecalculateBounds();
                        Assert.That(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True, clipPath);
                        var size = baked.bounds.size;
                        Assert.That(Mathf.Max(size.x, size.y, size.z), Is.InRange(.5f, 3.5f), clipPath);
                        Assert.That(baked.vertices.All(v => body.bounds.Contains(body.transform.TransformPoint(v))), Is.True,
                            "Culling bounds must contain the animated body: " + clipPath);
                    }
                    graph.DestroyPlayable(playable);
                }
                skin.Restore(); Assert.That(originalBody.sharedMesh, Is.SameAs(originalMesh));
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(baked); Object.DestroyImmediate(player);
            }
        }

        [TestCase("brute", "Assets/Characters/Brute/Source/BruteBody.prefab")]
        [TestCase("megumi", "Assets/Characters/Megumi/Source/_FBX/Megumi_HightSchoolStudent.fbx")]
        [TestCase("security-officer", "Assets/Characters/SecurityOfficer/Source/Prefab/Security Officer.prefab")]
        [TestCase("casual-1", "Assets/Characters/Casual1/Source/Casual1/Casual1.prefab")]
        [TestCase("picochan", "Assets/Characters/PicoChan/Source/Prefabs/PicoChan.prefab")]
        public void ConvertedGeometryRetainsEveryOriginalVertex(string id, string sourcePath)
        {
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var converted = Object.Instantiate(Definition(id).VisualPrefab);
            var baked = new Mesh();
            try
            {
                var expected = new System.Collections.Generic.List<Vector3>();
                foreach (var body in source.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!body.enabled) continue;
                    body.BakeMesh(baked, true);
                    var matrix = source.transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
                    expected.AddRange(baked.vertices.Select(matrix.MultiplyPoint3x4));
                }
                var actual = converted.GetComponentInChildren<SkinnedMeshRenderer>();
                actual.BakeMesh(baked, true);
                var vertices = baked.vertices;
                Assert.That(vertices.Length, Is.EqualTo(expected.Count));
                float maxError = 0;
                for (int i = 0; i < vertices.Length; i++) maxError = Mathf.Max(maxError, Vector3.Distance(expected[i], vertices[i]));
                Assert.That(maxError, Is.LessThan(.001f), "Conversion must not stretch the artist's proportions.");
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(source); Object.DestroyImmediate(converted); }
        }

        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void NativePoseFollowsPlayerTranslationRotationScaleAndVisibility(string id)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAppearanceInstaller.PlayerPath));
            var baked = new Mesh();
            PlayableGraph graph = default;
            try
            {
                var original = player.GetComponentInChildren<SkinnedMeshRenderer>();
                using var skin = new CharacterSkin(original);
                Assert.That(skin.Apply(Definition(id), out var error), Is.True, error);
                var animator = player.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                graph = PlayableGraph.Create(); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim"));
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(clip);
                clip.SetTime(.2); graph.Play(); graph.Evaluate(0); skin.SyncPose();
                var body = skin.VisibleBody;
                body.BakeMesh(baked); var expected = baked.vertices;
                player.transform.SetPositionAndRotation(new Vector3(132, 5, -201), Quaternion.Euler(0, 123, 0));
                player.transform.localScale = Vector3.one * 1.3f;
                graph.Evaluate(0); skin.SyncPose(); body.BakeMesh(baked);
                var actual = baked.vertices;
                float maxError = 0;
                // BakeMesh includes the inherited lossy scale, even with useScale=false.
                for (int i = 0; i < expected.Length; i++) maxError = Mathf.Max(maxError, Vector3.Distance(expected[i], actual[i] / 1.3f));
                Assert.That(maxError, Is.LessThan(.002f), "Pose must stay in the player's local space.");
                original.enabled = false; original.forceRenderingOff = true; skin.SyncPose();
                Assert.That(body.enabled, Is.False); Assert.That(body.forceRenderingOff, Is.True);
                original.enabled = true; original.forceRenderingOff = false; skin.SyncPose();
                Assert.That(body.enabled, Is.True); Assert.That(body.forceRenderingOff, Is.False);
            }
            finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(baked); Object.DestroyImmediate(player); }
        }

        private static CharacterDefinition Definition(string id) =>
            AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset").Find(id);

        [Test] public void CatalogOmitsExcludedModelsAndRetainsMegumiAttribution()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");
            Assert.That(catalog.Characters.Any(c => c != null && (c.Id == "maya" || c.Id == "emily")), Is.False);
            var megumi = catalog.Find("megumi");
            Assert.That(megumi.Attribution, Does.Contain("alex94i60").And.Contain("CC BY 4.0"));
            Assert.That(megumi.SourceUrl, Does.StartWith("https://www.fab.com/listings/"));
            Assert.That(megumi.LicenseUrl, Is.EqualTo("https://creativecommons.org/licenses/by/4.0/"));
        }
    }
}
