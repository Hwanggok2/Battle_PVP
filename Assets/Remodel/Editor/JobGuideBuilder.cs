using System;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.Remodel.Editor
{
    public static class JobGuideBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/JobGuide.prefab";
        private static readonly Color Ink = new Color(.025f, .055f, .09f, .98f);
        private static readonly Color Cyan = new Color(.24f, .9f, .92f);
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");

        [MenuItem("Battle PvP/UI/Build Job Guide")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building UI.");
            string original = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var prefab = BuildPrefab();
            foreach (string name in new[] { "Lobby", "Battle_waiting", "Battle" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Job Guide");
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                PrefabUtility.InstantiatePrefab(prefab, scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
            AssetDatabase.SaveAssets();
        }
        private static GameObject BuildPrefab()
        {
            var root = new GameObject("Job Guide", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                root.GetComponent<Canvas>().sortingOrder = 155;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
                var open = Button("Open", root.transform, "직업", new Vector2(110, 44), new Vector2(-470, -42));
                var openRect = (RectTransform)open.transform; openRect.anchorMin = openRect.anchorMax = Vector2.one;
                var panel = Rect("Panel", root.transform, new Vector2(1040, 840), Vector2.zero); Image(panel, Ink); Border(panel);
                Text("Heading", panel, SkillGameData.Text("UI_Skill_Title", "직업과 스킬"), new Vector2(280, 48), new Vector2(-340, 376), 30);
                var close = Button("Close", panel, SkillGameData.Text("UI_Skill_Close", "닫기"), new Vector2(88, 38), new Vector2(446, 376));
                Button("Save", panel, SkillGameData.Text("UI_Skill_Save", "적용"), new Vector2(88, 38), new Vector2(340, 376));
                Text("Hint", panel, SkillGameData.Text("UI_Skill_Hint"), new Vector2(600, 30), new Vector2(-180, 333), 17).color = new Color(.54f, .69f, .74f);
                for (int i = 0; i < JobGuideContent.Count; i++)
                    Button("Job" + i, panel, JobGuideContent.Name(i), new Vector2(230, 66), new Vector2(-371, 253 - i * 84));
                var portrait = Image(Rect("Portrait", panel, new Vector2(78, 78), new Vector2(-161, 266)), Color.white); portrait.preserveAspect = true;
                Text("JobName", panel, "직업", new Vector2(535, 42), new Vector2(170, 287), 26);
                var requirement = Text("Requirement", panel, "", new Vector2(535, 53), new Vector2(170, 243), 17); requirement.color = Cyan;
                Text("Description", panel, "", new Vector2(680, 45), new Vector2(140, 191), 18);
                for (int i = 0; i < 4; i++)
                {
                    var card = Rect("Skill" + i, panel, new Vector2(680, 134), new Vector2(140, 102 - i * 140));
                    Image(card, new Color(.05f, .10f, .16f, 1)); Border(card);
                    var icon = Image(Rect("Icon", card, new Vector2(40, 40), new Vector2(-308, 41)), Color.white); icon.preserveAspect = true;
                    Text("Name", card, "스킬", new Vector2(335, 32), new Vector2(-110, 42), 21);
                    for (int slot = 0; slot < 2; slot++)
                    {
                        var equip=Button("Equip" + slot, card, slot == 0 ? "Q" : "E", new Vector2(72, 30), new Vector2(194 + slot * 86, 42));
                        equip.GetComponentInChildren<TMP_Text>().fontSize=16;
                    }
                    Text("Effect", card, "", new Vector2(640, 60), new Vector2(0, -10), 16);
                    Text("Timing", card, "", new Vector2(640, 21), new Vector2(0, -51), 14).color = Cyan;
                }
                Text("Footer", panel, SkillGameData.Text("UI_Skill_Footer"), new Vector2(960, 25), new Vector2(0, -401), 15).color = new Color(.54f, .69f, .74f);
                var controller = root.AddComponent<JobGuidePanel>(); var so = new SerializedObject(controller);
                so.FindProperty("_panel").objectReferenceValue = panel.gameObject;
                so.FindProperty("_openButton").objectReferenceValue = open;
                so.FindProperty("_portraits").objectReferenceValue = AssetDatabase.LoadAssetAtPath<IdentitySpriteSet>("Assets/Resources/IdentitySpriteSet.asset");
                var skills = AssetDatabase.FindAssets("t:JobSkillData", new[] { "Assets/Player/skill" })
                    .Select(guid => AssetDatabase.LoadAssetAtPath<JobSkillData>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
                var property = so.FindProperty("_skills"); property.arraySize = skills.Length;
                for (int i = 0; i < skills.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = skills[i];
                so.ApplyModifiedPropertiesWithoutUndo(); panel.gameObject.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        private static Image Image(RectTransform rect, Color color)
        { var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
        private static TMP_Text Text(string name, Transform parent, string value, Vector2 size, Vector2 position, float fontSize)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font; text.text = value; text.fontSize = fontSize; text.color = new Color(.84f, .92f, .97f);
            text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false; return text;
        }
        private static Button Button(string name, Transform parent, string label, Vector2 size, Vector2 position)
        {
            var rect = Rect(name, parent, size, position); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Image(rect, new Color(.035f, .08f, .13f, 1));
            var text = Text("Label", rect, label, size - Vector2.one * 10, Vector2.zero, 22); text.alignment = TextAlignmentOptions.Center;
            var colors = button.colors; colors.highlightedColor = new Color(.65f, 1, 1); colors.pressedColor = new Color(.35f, .7f, .8f); button.colors = colors;
            rect.gameObject.AddComponent<NeonButtonSound>(); Border(rect); return button;
        }
        private static void Border(RectTransform rect)
        {
            for (int i = 0; i < 4; i++)
            {
                var edge = Rect("Edge" + i, rect, Vector2.zero, Vector2.zero);
                edge.anchorMin = i == 0 ? new Vector2(0, 1) : i == 3 ? new Vector2(1, 0) : Vector2.zero;
                edge.anchorMax = i == 1 ? new Vector2(1, 0) : i == 2 ? new Vector2(0, 1) : Vector2.one;
                edge.sizeDelta = i < 2 ? new Vector2(0, 1) : new Vector2(1, 0);
                Image(edge, new Color(Cyan.r, Cyan.g, Cyan.b, .45f)).raycastTarget = false;
            }
        }
    }
}
