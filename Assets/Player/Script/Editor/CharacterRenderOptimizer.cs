using System;
using System.Collections.Generic;
using System.Linq;
using BattlePvp.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorData
{
    public static class CharacterRenderOptimizer
    {
        [MenuItem("Battle PvP/Characters/Optimize Imported Rendering")]
        public static void OptimizeCatalog()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CharacterAppearanceInstaller.CatalogPath);
            foreach (var definition in catalog.Characters)
            {
                if (definition == null || definition.VisualPrefab == null) continue;
                string path = AssetDatabase.GetAssetPath(definition.VisualPrefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var body in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        // Preserve all vertex streams, weights and bind poses. Only index batches change.
                        Mesh mesh = body.sharedMesh;
                        if (mesh.lodCount > 1)
                        {
                            PreserveFaceDetails(mesh, body.sharedMaterials);
                            continue; // Re-running does not treat LOD indices as LOD0 geometry.
                        }
                        var materials = new List<Material>();
                        var triangles = new List<List<int>>();
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            var material = body.sharedMaterials[sub];
                            int group = materials.FindIndex(m => Equivalent(m, material));
                            if (group < 0) { group = materials.Count; materials.Add(material); triangles.Add(new List<int>()); }
                            triangles[group].AddRange(mesh.GetTriangles(sub));
                        }
                        mesh.subMeshCount = materials.Count;
                        for (int sub = 0; sub < materials.Count; sub++) mesh.SetTriangles(triangles[sub], sub, false);
                        // Two conservative simplification levels; original LOD0 is retained exactly.
                        MeshLodUtility.GenerateMeshLods(mesh, 2);
                        body.sharedMaterials = materials.ToArray();
                        PreserveFaceDetails(mesh, body.sharedMaterials);
                        EditorUtility.SetDirty(mesh);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        private static void PreserveFaceDetails(Mesh mesh, Material[] materials)
        {
            for (int sub = 0; sub < materials.Length; sub++)
            {
                string name = materials[sub].name.ToLowerInvariant();
                var original = mesh.GetLod(sub, 0);
                // Small eye layers can disappear or expose the inside of the head after simplification.
                // Share their original index ranges at every distance without adding draw calls.
                if (original.indexCount / 3 >= 1024 && !name.Contains("face") && !name.Contains("eye") &&
                    !name.Contains("mouth") && !name.Contains("lash") && !name.Contains("brow")) continue;
                for (int lod = 1; lod < mesh.lodCount; lod++) mesh.SetLod(sub, lod, original);
            }
            EditorUtility.SetDirty(mesh);
        }

        private static bool Equivalent(Material a, Material b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.shader != b.shader || a.ComputeCRC() != b.ComputeCRC() ||
                a.renderQueue != b.renderQueue || a.enableInstancing != b.enableInstancing ||
                a.doubleSidedGI != b.doubleSidedGI || a.globalIlluminationFlags != b.globalIlluminationFlags ||
                !a.shaderKeywords.OrderBy(k => k).SequenceEqual(b.shaderKeywords.OrderBy(k => k))) return false;
            for (int i = 0; i < a.shader.GetPropertyCount(); i++)
            {
                int id = a.shader.GetPropertyNameId(i);
                switch (a.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        if (a.GetTexture(id) != b.GetTexture(id) || a.GetTextureOffset(id) != b.GetTextureOffset(id) || a.GetTextureScale(id) != b.GetTextureScale(id)) return false;
                        break;
                    case ShaderPropertyType.Color: if (a.GetColor(id) != b.GetColor(id)) return false; break;
                    case ShaderPropertyType.Vector: if (a.GetVector(id) != b.GetVector(id)) return false; break;
                    case ShaderPropertyType.Int: if (a.GetInteger(id) != b.GetInteger(id)) return false; break;
                    default: if (a.GetFloat(id) != b.GetFloat(id)) return false; break;
                }
            }
            for (int i = 0; i < a.passCount; i++)
                if (a.GetShaderPassEnabled(a.GetPassName(i)) != b.GetShaderPassEnabled(b.GetPassName(i))) return false;
            return true;
        }
    }
}
