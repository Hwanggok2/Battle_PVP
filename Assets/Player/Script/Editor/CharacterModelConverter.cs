using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattlePvp.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorData
{
    /// <summary>Preserves the original character skeleton and silhouette while combining material batches.</summary>
    public static class CharacterModelConverter
    {

        [MenuItem("Battle PvP/Characters/Convert Selected Humanoid")]
        private static void ConvertSelected()
        {
            var model = Selection.activeObject as GameObject;
            if (model == null || !EditorUtility.IsPersistent(model))
                throw new ArgumentException("Project 창에서 Humanoid 모델을 선택하세요.");
            string id = model.name.ToLowerInvariant().Replace(' ', '-');
            Selection.activeObject = Convert(AssetDatabase.GetAssetPath(model), id, model.name);
        }

        public static CharacterDefinition Convert(string modelPath, string id, string displayName)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id == "." || id == ".." ||
                id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("A stable filename-safe character ID is required.");
            string root = AssetDatabase.GetSubFolders("Assets/Characters")
                .FirstOrDefault(p => string.Equals(Path.GetFileName(p), id, StringComparison.OrdinalIgnoreCase))
                ?? "Assets/Characters/" + id;
            string folder = root + "/Converted", definitionPath = folder + "/Character.asset";
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(definitionPath);
            var catalog = CharacterAppearanceInstaller.EnsureCatalog();
            if (catalog.Characters.Any(c => c != null && c.Id == id && c != definition))
                throw new InvalidOperationException("Duplicate character ID: " + id);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new ArgumentException("Missing character model: " + modelPath);
            var source = Object.Instantiate(model);
            Mesh mesh = null;
            try
            {
                source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = source.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new ArgumentException("The source model needs a valid Humanoid avatar.");
                // Combine only draw calls. Vertex positions, native bones and their proportions stay intact.
                mesh = BuildNativeMesh(source, out var bones, out var originalMaterials);
                mesh.name = displayName + " original proportions";
                Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                string meshPath = folder + "/NativeSkin.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
                else AssetDatabase.CreateAsset(mesh, meshPath);
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
                foreach (var script in source.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                foreach (var collider in source.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var rigidbody in source.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rigidbody);
                var body = new GameObject("Native character body").AddComponent<SkinnedMeshRenderer>();
                body.transform.SetParent(source.transform, false);
                body.sharedMesh = mesh; body.bones = bones;
                body.rootBone = source.transform;
                body.quality = SkinQuality.Bone4;
                body.sharedMaterials = originalMaterials.Select((m, i) => ConvertMaterial(m, folder, i)).ToArray();
                // Root-relative bounds cover attack and falling poses without CPU skinning every frame.
                var bounds = mesh.bounds;
                bounds.extents = Vector3.one * Mathf.Max(bounds.size.y, bounds.size.x) * 1.25f;
                body.localBounds = bounds;
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.enabled = false;
                source.name = displayName + " native visual";
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, folder + "/NativeVisual.prefab");
                bool created = definition == null;
                if (created) definition = ScriptableObject.CreateInstance<CharacterDefinition>();
                definition.Id = id; definition.DisplayName = displayName;
                if (created) definition.Description = "능력치는 캐릭터 설정에서 조정할 수 있습니다.";
                definition.UseDefaultBody = false; definition.VisualPrefab = prefab;
                definition.Body = null; definition.Materials = Array.Empty<Material>();
                if (created) AssetDatabase.CreateAsset(definition, definitionPath);
                EditorUtility.SetDirty(definition);
                if (!catalog.Characters.Contains(definition)) catalog.Characters = catalog.Characters.Concat(new[] { definition }).ToArray();
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                return definition;
            }
            finally
            {
                if (mesh != null && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(source);
            }
        }

        private static Mesh BuildNativeMesh(GameObject source, out Transform[] bones, out Material[] materials)
        {
            var renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            bones = renderers.SelectMany(r => r.bones).Distinct().ToArray();
            if (bones.Any(b => b == null)) throw new ArgumentException("A skin has missing bones.");
            var boneIndices = bones.Select((bone, index) => (bone, index)).ToDictionary(v => v.bone, v => v.index);
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var weights = new List<BoneWeight>(); var triangles = new List<List<int>>(); var materialList = new List<Material>();
            foreach (var renderer in renderers)
            {
                var original = renderer.sharedMesh;
                if (original == null || original.boneWeights.Length != original.vertexCount || original.subMeshCount != renderer.sharedMaterials.Length)
                    throw new ArgumentException("Invalid skinned mesh: " + renderer.name);
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, true);
                    var matrix = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    var normalMatrix = matrix.inverse.transpose;
                    var points = baked.vertices; var directions = baked.normals; var texcoords = original.uv;
                    var originalWeights = original.boneWeights;
                    int start = vertices.Count;
                    for (int i = 0; i < points.Length; i++)
                    {
                        vertices.Add(matrix.MultiplyPoint3x4(points[i]));
                        normals.Add(normalMatrix.MultiplyVector(directions[i]).normalized);
                        uv.Add(texcoords.Length == points.Length ? texcoords[i] : Vector2.zero);
                        var w = originalWeights[i];
                        w.boneIndex0 = boneIndices[renderer.bones[w.boneIndex0]];
                        w.boneIndex1 = boneIndices[renderer.bones[w.boneIndex1]];
                        w.boneIndex2 = boneIndices[renderer.bones[w.boneIndex2]];
                        w.boneIndex3 = boneIndices[renderer.bones[w.boneIndex3]];
                        weights.Add(w);
                    }
                    for (int i = 0; i < original.subMeshCount; i++)
                    {
                        var material = renderer.sharedMaterials[i]; int index = materialList.IndexOf(material);
                        if (index < 0) { index = materialList.Count; materialList.Add(material); triangles.Add(new List<int>()); }
                        triangles[index].AddRange(original.GetTriangles(i).Select(v => v + start));
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }
            if (vertices.Count == 0) throw new ArgumentException("No visible skinned meshes.");
            var result = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0, uv);
            result.boneWeights = weights.ToArray();
            result.bindposes = bones.Select(b => b.worldToLocalMatrix * source.transform.localToWorldMatrix).ToArray();
            result.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) result.SetTriangles(triangles[i], i);
            result.RecalculateBounds(); result.RecalculateTangents();
            materials = materialList.ToArray(); return result;
        }

        private static Material ConvertMaterial(Material original, string folder, int index)
        {
            if (original == null) throw new ArgumentException("Missing source material.");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            string path = folder + "/Material-" + index.ToString("D2") + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(shader); AssetDatabase.CreateAsset(result, path); }
            result.shader = shader; result.name = original.name;
            result.SetTexture("_BaseMap", original.mainTexture);
            result.SetColor("_BaseColor", original.HasProperty("_Color")
                ? original.color : Color.white);
            result.SetFloat("_Smoothness", 0); result.SetFloat("_Metallic", 0);
            result.SetFloat("_SpecularHighlights", 0); result.SetFloat("_EnvironmentReflections", 0);
            result.SetFloat("_Cull", 0); result.SetFloat("_Surface", 0);
            result.SetFloat("_AlphaClip", 1); result.SetFloat("_Cutoff", .4f);
            result.EnableKeyword("_ALPHATEST_ON");
            result.SetOverrideTag("RenderType", "TransparentCutout"); result.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(result);
            return result;
        }
    }
}
