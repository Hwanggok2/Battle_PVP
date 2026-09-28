using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattlePvp.Networking;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BattlePvp.Remodel.Editor
{
    /// <summary>Imports the approved preview's baked geometry; no runtime model loader is needed.</summary>
    public static class RemodelMapBuilder
    {
        private const string Root = "Assets/Remodel";
        private static readonly string[] MapIds = { "lobby", "waiting", "arena", "foundry" };

        [MenuItem("Battle PvP/Remodel/Build Approved Maps")]
        public static void BuildAssets()
        {
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            foreach (string file in new[] { "alloy-floor", "rooftop-floor", "night-city-sky" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/" + file + ".png");
                importer.sRGBTexture = true;
                importer.wrapMode = file == "night-city-sky" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            foreach (string id in MapIds) BuildMap(id);
            var sky = MaterialAt("Sky", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/night-city-sky.png"));
            sky.SetFloat("_Exposure", 0.8f);
            sky.SetFloat("_Rotation", 180f);
            EditorUtility.SetDirty(sky);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Materials/NeonVolume.asset");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Root + "/Materials/NeonVolume.asset");
            }
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>();
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.55f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static JObject Data(string id) => JObject.Parse(File.ReadAllText(Root + "/Editor/Data/" + id + ".json"));
        private static Vector3 V(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
        private static Color C(JToken t) => new Color((float)t[0], (float)t[1], (float)t[2], 1f);
        private static Material MaterialAt(string name, string shaderName)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName)) { name = name, enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        private static void BuildMap(string id)
        {
            JObject data = Data(id);
            Scene previous = SceneManager.GetActiveScene();
            Scene staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(staging);
            var root = new GameObject("Neon_" + id);
            try
            {
                int index = 0;
                foreach (JToken item in data["meshes"])
                {
                    string name = id + "_" + index++;
                    JToken source = item["material"];
                    bool unlit = (bool)source["unlit"];
                    var material = MaterialAt(name, unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                    material.SetColor("_BaseColor", C(source["color"]));
                    material.SetFloat("_Cull", 0f);
                    if (!unlit)
                    {
                        material.SetFloat("_Metallic", (float)source["metalness"]);
                        material.SetFloat("_Smoothness", 1f - (float)source["roughness"]);
                        Color emission = C(source["emission"]) * (float)source["emissionIntensity"] * 2f;
                        material.SetColor("_EmissionColor", emission);
                        if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
                    }
                    else material.SetColor("_BaseColor", C(source["color"]) * 1.8f);
                    string floor = (string)source["floor"];
                    if (!string.IsNullOrEmpty(floor))
                        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + (floor == "alloy" ? "alloy-floor" : "rooftop-floor") + ".png"));
                    EditorUtility.SetDirty(material);
                    float[] xyz = item["positions"].ToObject<float[]>(), normals = item["normals"].ToObject<float[]>(), uv = item["uv"].ToObject<float[]>();
                    var vertices = new Vector3[xyz.Length / 3];
                    var ns = new Vector3[vertices.Length];
                    var uvs = new Vector2[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
                        ns[i] = new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                        uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                    }
                    string meshPath = Root + "/Meshes/" + name + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, meshPath); }
                    mesh.Clear();
                    mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = vertices;
                    mesh.normals = ns;
                    mesh.uv = uvs;
                    mesh.triangles = item["indices"].ToObject<int[]>();
                    mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);
                    var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(root.transform, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = unlit || (float)source["emissionIntensity"] > 0f ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    go.isStatic = true;
                }
                var collisions = new GameObject("Collision");
                collisions.transform.SetParent(root.transform, false);
                foreach (JToken box in data["colliders"]) AddBox(collisions.transform, "Cover", V(box["position"]), V(box["size"]));
                float w = (float)data["width"], d = (float)data["depth"];
                AddBox(collisions.transform, "Floor", new Vector3(0, -.18f, 0), new Vector3(w, .3f, d));
                foreach (float x in new[] { -w / 2, w / 2 }) AddBox(collisions.transform, "Boundary", new Vector3(x, 3f, 0), new Vector3(.5f, 6f, d));
                foreach (float z in new[] { -d / 2, d / 2 }) AddBox(collisions.transform, "Boundary", new Vector3(0, 3f, z), new Vector3(w, 6f, .5f));
                foreach (JToken source in data["lights"])
                {
                    var lightObject = new GameObject("Neon Fill", typeof(Light));
                    lightObject.transform.SetParent(root.transform, false);
                    lightObject.transform.localPosition = V(source["position"]);
                    var light = lightObject.GetComponent<Light>();
                    light.type = LightType.Point;
                    light.color = C(source["color"]);
                    light.range = (float)source["range"];
                    light.intensity = (float)source["intensity"];
                    light.shadows = LightShadows.None;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + id + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(staging, true);
            }
        }

        private static void AddBox(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name, typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.GetComponent<BoxCollider>().size = size;
            go.isStatic = true;
        }

        [MenuItem("Battle PvP/Remodel/Apply Approved Maps To Scenes")]
        public static void ApplyScenes()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play mode before applying scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save existing scene edits first.");
            string previous = SceneManager.GetActiveScene().path;
            foreach (string name in new[] { "Login", "Lobby", "Battle_waiting", "Battle" })
            {
                Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Single);
                foreach (var obj in scene.GetRootGameObjects())
                {
                    if (obj.name == "Remodel Environment" || obj.name == "Remodel Spawns") UnityEngine.Object.DestroyImmediate(obj);
                    else if (obj.name == "Plane" || obj.name == "Lobby_Background" || obj.name == "NetworkStartPosition_Group" || obj.name == "Dummy") obj.SetActive(false);
                }
                string map = name == "Lobby" ? "lobby" : name == "Battle_waiting" ? "waiting" : "arena";
                var environment = new GameObject("Remodel Environment");
                var activeMap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + map + ".prefab"), environment.transform);
                if (name == "Battle" || name == "Battle_waiting")
                {
                    var manager = scene.GetRootGameObjects().First(g => g.name == "BattleManager");
                    var selection = manager.GetComponent<BattleMapSelection>() ?? manager.AddComponent<BattleMapSelection>();
                    if (name == "Battle")
                    {
                        var roof = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/foundry.prefab"), environment.transform);
                        roof.SetActive(false);
                        var so = new SerializedObject(selection);
                        so.FindProperty("_research").objectReferenceValue = activeMap;
                        so.FindProperty("_rooftop").objectReferenceValue = roof;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    var spawnRoot = new GameObject("Remodel Spawns");
                    int index = 0;
                    foreach (JToken spawn in Data(map)["spawns"])
                    {
                        var go = new GameObject("Spawn " + (++index).ToString("00"), typeof(NetworkStartPosition));
                        go.transform.SetParent(spawnRoot.transform);
                        go.transform.position = V(spawn);
                        Vector3 direction = -go.transform.position; direction.y = 0;
                        go.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    }
                }
                var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First();
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.farClipPlane = 180f;
                ConfigureCamera(scene, name);
                if (name == "Lobby")
                {
                    var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Player");
                    if (player != null) { player.transform.position = new Vector3(0, .2f, -2); player.transform.rotation = Quaternion.Euler(0, 180, 0); }
                }
                var sun = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).First(l => l.type == LightType.Directional);
                sun.color = new Color(.72f, .81f, 1f); sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(48, -32, 0);
                var volume = environment.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Materials/NeonVolume.asset");
                RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sky.mat");
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.24f, .29f, .42f);
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(.04f, .07f, .13f);
                RenderSettings.fogStartDistance = 65; RenderSettings.fogEndDistance = 155;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
        }

        public static void ConfigureCamera(Scene scene, string name)
        {
            if (name == "Battle") return; // Preserve the user's combat camera.
            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First();
            var follow = camera.GetComponent<BattlePvp.CameraLogic.FollowCamera>();
            if (name == "Login")
            {
                if (follow != null) follow.enabled = false;
                camera.fieldOfView = 55;
                var flythrough = camera.GetComponent<BattlePvp.CameraLogic.TitleBattleFlythrough>() ??
                    camera.gameObject.AddComponent<BattlePvp.CameraLogic.TitleBattleFlythrough>();
                flythrough.enabled = true;
                flythrough.ApplyPose(0f);
                return;
            }
            if (follow == null && (name == "Lobby" || name == "Battle_waiting"))
                follow = camera.gameObject.AddComponent<BattlePvp.CameraLogic.FollowCamera>();
            if (name == "Battle_waiting")
            {
                camera.fieldOfView = 60;
                camera.transform.SetPositionAndRotation(new Vector3(0,1.7f,-3),Quaternion.identity);
                if (follow != null) { follow.enabled = true; follow.Offset = new Vector3(.3f,.2f,-1); }
                return;
            }
            camera.fieldOfView = name == "Login" ? 43 : 55;
            camera.transform.position = name == "Login" ? new Vector3(0,5,-28) : new Vector3(3.4f,2.5f,-7.2f);
            camera.transform.LookAt(name == "Login" ? new Vector3(0,1,0) : new Vector3(0,1.1f,-2));
            if (follow != null)
            {
                follow.enabled = name == "Lobby";
                if(name=="Lobby")
                {
                    var player=scene.GetRootGameObjects().FirstOrDefault(g=>g.GetComponent<BattlePvp.Stats.StatManager>()!=null);
                    if(player!=null)follow.SetTarget(player.transform);
                }
            }
        }

        [MenuItem("Battle PvP/Remodel/Apply Title Flythrough")]
        public static void ApplyTitleFlythrough()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save existing scene edits first.");
            string previous = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Login.unity", OpenSceneMode.Single);
            var environment = scene.GetRootGameObjects().First(g => g.name == "Remodel Environment");
            foreach (Transform child in environment.transform.Cast<Transform>().ToArray())
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (path == Root + "/Prefabs/lobby.prefab" || path == Root + "/Prefabs/arena.prefab")
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/arena.prefab"), environment.transform);
            ConfigureCamera(scene, "Login");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (previous != scene.path) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }
    }
}
