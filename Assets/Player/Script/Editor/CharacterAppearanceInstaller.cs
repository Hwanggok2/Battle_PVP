using System;
using System.IO;
using System.Linq;
using BattlePvp.Characters;
using BattlePvp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.EditorData
{
    public static class CharacterAppearanceInstaller
    {
        public const string CatalogPath = "Assets/Resources/CharacterCatalog.asset";
        public const string PlayerPath = "Assets/Prefabs/Player.prefab";
        public const string UiPath = "Assets/Prefabs/CharacterSelection.prefab";
        private const string Folder = "Assets/Characters";

        [MenuItem("Battle PvP/Characters/Install Character Selection")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes before installing character selection.");
            var catalog = EnsureCatalog();
            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var appearance = player.GetComponent<PlayerAppearance>() ?? player.AddComponent<PlayerAppearance>();
                var serialized = new SerializedObject(appearance);
                serialized.FindProperty("_body").objectReferenceValue = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            GameObject ui;
            var root = new GameObject("Character Selection", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                root.GetComponent<Canvas>().sortingOrder = 156;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
                var panel = root.AddComponent<CharacterSelectionPanel>();
                var serialized = new SerializedObject(panel);
                serialized.FindProperty("_font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                ui = PrefabUtility.SaveAsPrefabAsset(root, UiPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            foreach (string name in new[] { "Lobby", "Battle_waiting", "Battle" })
            {
                string path = "Assets/Scenes/" + name + ".unity";
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    if (!scene.GetRootGameObjects().Any(g => g.GetComponent<CharacterSelectionPanel>() != null))
                        PrefabUtility.InstantiatePrefab(ui, scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Characters] Character selection installed. Register future resources using Battle PvP/Characters/Register Selected Model.");
        }

        public static CharacterCatalog EnsureCatalog()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogPath);
            if (catalog != null)
            {
                var existing = catalog.Find(CharacterCatalog.DefaultId);
                if (existing != null && existing.Portrait == null)
                { existing.Portrait = DefaultPortrait(); EditorUtility.SetDirty(existing); }
                return catalog;
            }
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            definition.Id = CharacterCatalog.DefaultId; definition.DisplayName = "기본 캐릭터";
            definition.Description = "모든 캐릭터 능력치의 기준 · 균형형"; definition.UseDefaultBody = true;
            definition.Portrait = DefaultPortrait();
            AssetDatabase.CreateAsset(definition, Folder + "/DefaultCharacter.asset");
            catalog = ScriptableObject.CreateInstance<CharacterCatalog>(); catalog.Characters = new[] { definition };
            catalog.PreviewRig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Anim/Ch10_nonPBR.fbx");
            catalog.PreviewController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Player/Anim/Player.controller");
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        private static Sprite DefaultPortrait()
        {
            const string path = Folder + "/DefaultPortrait.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                AssetDatabase.CopyAsset("Assets/Remodel/Textures/player-portrait.png", path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            { importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        [MenuItem("Battle PvP/Characters/Register Selected Model")]
        private static void RegisterSelected()
        {
            try
            {
                var definition = RegisterModel(Selection.activeObject as GameObject);
                Selection.activeObject = definition; EditorGUIUtility.PingObject(definition);
                Debug.Log("[Characters] Registered " + definition.Id + ". Set Display Name and Portrait in the Inspector.");
            }
            catch (ArgumentException exception) { EditorUtility.DisplayDialog("캐릭터 등록", exception.Message, "확인"); }
        }

        public static CharacterDefinition RegisterModel(GameObject model)
        {
            if (model == null || !EditorUtility.IsPersistent(model)) throw new ArgumentException("Project 창에서 FBX 또는 모델 프리팹을 선택하세요.");
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1) throw new ArgumentException("몸·의상·머리를 하나의 SkinnedMeshRenderer로 합쳐 주세요. 여러 재질/서브메시는 지원합니다.");
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            definition.Id = Guid.NewGuid().ToString("N"); definition.DisplayName = model.name;
            definition.Description = "능력치는 캐릭터 설정에서 조정할 수 있습니다."; definition.Body = skins[0];
            var playerBody = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!new CharacterSkin(playerBody).Validate(definition, out string error))
            { UnityEngine.Object.DestroyImmediate(definition); throw new ArgumentException(error + "\n기준 모델: Assets/Player/Anim/Ch10_nonPBR.fbx"); }
            var catalog = EnsureCatalog();
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/Character.asset");
            AssetDatabase.CreateAsset(definition, path);
            Undo.RecordObject(catalog, "Register character");
            catalog.Characters = (catalog.Characters ?? Array.Empty<CharacterDefinition>()).Concat(new[] { definition }).ToArray();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            return definition;
        }

        [MenuItem("Battle PvP/Characters/Validate Catalog")]
        public static void ValidateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogPath);
            if (catalog == null || catalog.Find(CharacterCatalog.DefaultId) == null) throw new InvalidOperationException("Missing default character/catalog.");
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
            var skin = new CharacterSkin(body);
            foreach (var definition in catalog.Characters)
            {
                if (definition == null || catalog.Find(definition.Id) != definition) throw new InvalidOperationException("Missing, duplicate or invalid character ID.");
                if (!skin.Validate(definition, out string error)) throw new InvalidOperationException(definition.name + ": " + error);
            }
            Debug.Log("[Characters] Catalog validated: " + catalog.Characters.Length + " character(s).");
        }
    }
}
