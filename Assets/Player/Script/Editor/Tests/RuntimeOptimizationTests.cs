using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class RuntimeOptimizationTests
    {
        [Test]
        public void MetadataHasNoDependencyOnImportedSkinnedModels()
        {
            var paths = AssetDatabase.GetDependencies("Assets/Resources/CharacterCatalog.asset", true);
            Assert.That(paths.Any(p => p.EndsWith("NativeSkin.asset") || p.Contains("/CharacterVisuals/")), Is.False);
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");
            foreach (var definition in catalog.Characters.Where(d => d != null && d.HasVisual))
            {
                var body = definition.VisualPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = body.sharedMesh;
                Assert.That(mesh.lodCount, Is.EqualTo(3), definition.Id);
                Assert.That(mesh.subMeshCount, Is.EqualTo(body.sharedMaterials.Length));
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    for (int lod = 1; lod < mesh.lodCount; lod++)
                    {
                        Assert.That(mesh.GetLod(sub, lod).indexCount, Is.LessThanOrEqualTo(mesh.GetLod(sub, lod - 1).indexCount));
                        string material = body.sharedMaterials[sub].name.ToLowerInvariant();
                        if (material.Contains("eye") || material.Contains("face"))
                            Assert.That(mesh.GetLod(sub, lod), Is.EqualTo(mesh.GetLod(sub, 0)), "Facial layers must retain their original geometry.");
                    }
                }
            }
        }

        [Test]
        public void HookBoundsAndCachedNormalsMatchRecalculatedMeshWhileLengthChanges()
        {
            using var chain = new SkillHookChain(null);
            var mesh = (Mesh)typeof(SkillHookChain).GetField("_mesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chain);
            foreach (var end in new[] { new Vector3(0,0,6), new Vector3(1,2,1), new Vector3(0,4,0), new Vector3(.07f,0,0), new Vector3(8,0,0) })
            {
                chain.Update(Vector3.zero, end);
                Assert.That(mesh.vertices.All(v => mesh.bounds.Contains(v)), Is.True);
                var expected = Object.Instantiate(mesh);
                try
                {
                    expected.RecalculateNormals();
                    var normals = mesh.normals; var reference = expected.normals;
                    for (int i = 0; i < normals.Length; i++)
                        Assert.That(Vector3.Angle(normals[i], reference[i]), Is.LessThan(.2f), "Normal " + i);
                }
                finally { Object.DestroyImmediate(expected); }
            }
            chain.Hide(); chain.Update(Vector3.zero, Vector3.forward);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
        }

        [Test]
        public void ReusingATrailResetsItsFlightAndReusesTheRenderer()
        {
            var root = new GameObject("Trail reuse");
            try
            {
                var trail = root.AddComponent<ArrowFlightTrail>();
                trail.Initialize(null, Color.red, Vector3.zero);
                var line = root.GetComponent<LineRenderer>();
                trail.Sample(Vector3.forward * 10); trail.Finish(Vector3.forward * 9);
                trail.Initialize(null, Color.blue, Vector3.right);
                Assert.That(trail.IsFinished, Is.False);
                Assert.That(root.GetComponents<LineRenderer>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponent<LineRenderer>(), Is.SameAs(line));
                Assert.That(line.positionCount, Is.EqualTo(1));
                Assert.That(line.GetPosition(0), Is.EqualTo(Vector3.right));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DifferentEffectiveRenderingCannotBeComparedAsTheSameConditions()
        {
            var a = new PerformanceCaptureContext(); var b = new PerformanceCaptureContext();
            Assert.That(a.Matches(b), Is.True);
            b.RenderScale = .75f; Assert.That(a.Matches(b), Is.False);
            b.RenderScale = 1; b.RenderFrameInterval = 2; Assert.That(a.Matches(b), Is.False);
            b.RenderFrameInterval = 1; b.AdaptiveTier = 0; Assert.That(a.Matches(b), Is.False);
        }

        [UnityTest]
        public IEnumerator VisualPoolsAndLazyLoadsSurviveReuseAndSceneUnload()
        {
            yield return new EnterPlayMode();
            var a = SceneManager.CreateScene("Pool test A"); var b = SceneManager.CreateScene("Pool test B");
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            try
            {
                var inactivePlayer = new GameObject("Appearance activation test");
                inactivePlayer.SetActive(false);
                inactivePlayer.AddComponent<Mirror.NetworkIdentity>();
                var appearance = inactivePlayer.AddComponent<PlayerAppearance>();
                try
                {
                    typeof(PlayerAppearance).GetMethod("ApplyVisual", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(appearance, null);
                    Assert.That(appearance.VisualRevision, Is.Zero, "Inactive players defer their visual work.");
                    inactivePlayer.SetActive(true);
                    Assert.That(appearance.VisualRevision, Is.EqualTo(1), "Reactivation must resume the deferred selection.");
                }
                finally { inactivePlayer.SetActive(false); Object.Destroy(inactivePlayer); }
                var first = CombatVisualPool.Rent(null, Vector3.zero, Quaternion.identity, a);
                first.SetActive(true); CombatVisualPool.Return(first);
                var reused = CombatVisualPool.Rent(null, Vector3.one, Quaternion.identity, a);
                Assert.That(reused, Is.SameAs(first));
                Assert.That(reused.activeSelf, Is.False);
                Assert.That(reused.transform.position, Is.EqualTo(Vector3.one));
                var other = CombatVisualPool.Rent(null, Vector3.zero, Quaternion.identity, b);
                Assert.That(other.scene, Is.EqualTo(b));
                Assert.That(other, Is.Not.SameAs(first));
                definition.SetVisualResource("CharacterVisuals/megumi");
                yield return definition.LoadVisualAsync();
                Assert.That(definition.IsVisualLoaded, Is.True);
                Assert.That(definition.LoadVisualAsync().MoveNext(), Is.False);
                yield return SceneManager.UnloadSceneAsync(a);
                Assert.That(first == null, Is.True); Assert.That(other != null, Is.True);
                CombatVisualPool.Return(other);
            }
            finally { Object.Destroy(definition); }
            yield return SceneManager.UnloadSceneAsync(b);
            yield return new ExitPlayMode();
        }
    }
}
