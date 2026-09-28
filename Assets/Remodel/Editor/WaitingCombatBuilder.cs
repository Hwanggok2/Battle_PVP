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
    /// <summary>Applies this update without rebuilding the existing maps or unrelated UI.</summary>
    public static class WaitingCombatBuilder
    {
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
        private static Sprite Circle => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        public static void Apply()
        {
            CombatSoundBuilder.Generate();
            foreach (string path in new[] { "Assets/Prefabs/UI_Root.prefab", "Assets/Prefabs/Player.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var arc in root.GetComponentsInChildren<SkillArcHud>(true)) FitIcons(arc);
                    if (path.EndsWith("Player.prefab"))
                    {
                        var audio = root.GetComponent<CombatAudio>() ?? root.AddComponent<CombatAudio>();
                        var so = new SerializedObject(audio);
                        Clips(so.FindProperty("_swings"), "sword-air-", 3);
                        Clips(so.FindProperty("_hits"), "sword-cut-", 2);
                        so.FindProperty("_death").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(CombatSoundBuilder.Folder + "death-discharge.wav");
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle_waiting.unity");
            CreateTerminal();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        private static void Clips(SerializedProperty array, string prefix, int count)
        {
            array.arraySize = count;
            for (int i = 0; i < count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(CombatSoundBuilder.Folder + prefix + i + ".wav");
        }
        public static void FitIcons(SkillArcHud arc)
        {
            foreach (var skill in arc.GetComponentsInChildren<SkillUI>(true))
            {
                var so = new SerializedObject(skill);
                var old = so.FindProperty("_baseImage").objectReferenceValue as Image;
                var maskRoot = skill.transform.Find("Fitted Icon") as RectTransform;
                if (maskRoot == null) maskRoot = Rect("Fitted Icon", skill.transform, new Vector2(88, 88), Vector2.zero);
                var maskImage = maskRoot.GetComponent<Image>() ?? maskRoot.gameObject.AddComponent<Image>();
                maskImage.sprite = Circle; maskImage.color = Color.white; maskImage.raycastTarget = false;
                var mask = maskRoot.GetComponent<Mask>() ?? maskRoot.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
                var iconRect = maskRoot.Find("Icon") as RectTransform ?? Rect("Icon", maskRoot, new Vector2(88, 88), Vector2.zero);
                var icon = iconRect.GetComponent<Image>() ?? iconRect.gameObject.AddComponent<Image>();
                if (old != null && old != icon) { icon.sprite = old.sprite; old.gameObject.SetActive(false); }
                icon.preserveAspect = false; icon.raycastTarget = false; icon.color = Color.white;
                so.FindProperty("_baseImage").objectReferenceValue = icon;
                so.ApplyModifiedPropertiesWithoutUndo();
                maskRoot.SetAsFirstSibling();
                var label = skill.transform.Find("SkillName") as RectTransform;
                if (label != null) label.anchoredPosition = new Vector2(0, -62);
                if (skill.GetComponent<SkillTooltip>() == null) skill.gameObject.AddComponent<SkillTooltip>();
            }
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        private static TMP_Text Label(string name, Transform parent, string text, Vector2 size, Vector2 pos, float fontSize = 24)
        {
            var rect = Rect(name, parent, size, pos); var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Font; label.text = text; label.fontSize = fontSize;
            label.color = new Color(.78f, .94f, 1); label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center; label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 pos)
        {
            var rect = Rect(name, parent, new Vector2(610, 64), pos);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.055f, .16f, .21f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Label", rect, text, new Vector2(570, 56), Vector2.zero);
            rect.gameObject.AddComponent<NeonButtonSound>(); return button;
        }
        private static Material Material(string name, Color color, bool glow)
        {
            string path = "Assets/Remodel/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/Remodel/Materials/waiting_0.mat");
                material = new Material(reference.shader); AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (glow) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2.2f); }
            EditorUtility.SetDirty(material); return material;
        }
        private static void Box(Transform root, string name, Vector3 pos, Vector3 size, Material material, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        private static void CreateTerminal()
        {
            var existing = GameObject.Find("Waiting Tactical Terminal"); if (existing != null) Object.DestroyImmediate(existing);
            var uiExisting = GameObject.Find("Terminal UI"); if (uiExisting != null) Object.DestroyImmediate(uiExisting);
            var root = new GameObject("Waiting Tactical Terminal");
            var terminal = root.AddComponent<WaitingRoomTerminal>();
            var dark = Material("terminal-alloy", new Color(.025f, .05f, .075f), false);
            var cyan = Material("terminal-cyan", new Color(.04f, .9f, .82f), true);
            var purple = Material("terminal-magenta", new Color(.55f, .06f, 1), true);
            Box(root.transform, "Pedestal", new Vector3(0, .56f, 0), new Vector3(1.25f, 1.12f, .9f), dark, true);
            Box(root.transform, "Console", new Vector3(0, 1.2f, 0), new Vector3(1.9f, .24f, 1.25f), dark, true);
            Box(root.transform, "Screen", new Vector3(0, 1.49f, .25f), new Vector3(1.55f, .56f, .13f), dark, true);
            Box(root.transform, "Screen glow", new Vector3(0, 1.48f, .174f), new Vector3(1.4f, .43f, .014f), cyan);
            for (int i = 0; i < 4; i++)
                Box(root.transform, "Display scanline " + i, new Vector3(-.2f + i * .05f, 1.35f + i * .09f, .16f), new Vector3(.85f - i * .1f, .028f, .015f), dark);
            Box(root.transform, "Floor beacon", new Vector3(0, .025f, 0), new Vector3(2.45f, .025f, 1.7f), purple);
            Box(root.transform, "Pedestal strip", new Vector3(0, .65f, -.46f), new Vector3(.08f, .7f, .025f), cyan);
            var focus = new GameObject("Focus"); focus.transform.SetParent(root.transform, false); focus.transform.localPosition = new Vector3(0, 1.4f, 0);
            var titleGo = new GameObject("Tactical label", typeof(TextMeshPro)); titleGo.transform.SetParent(root.transform, false);
            titleGo.transform.localPosition = new Vector3(0, 2.05f, 0);
            var title = titleGo.GetComponent<TextMeshPro>(); title.font = Font; title.text = "전술 단말\n<size=65%>E · 길게 누르기</size>";
            title.fontSize = 2.2f; title.alignment = TextAlignmentOptions.Center; title.color = new Color(.35f, 1, .9f);
            title.rectTransform.sizeDelta = new Vector2(3, .7f);

            var canvasGo = new GameObject("Terminal UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 185;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var panel = Rect("Terminal Panel", canvasGo.transform, new Vector2(760, 680), Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(.014f, .03f, .054f, .985f);
            panel.gameObject.AddComponent<ExpandedPanelLayout>();
            Label("Heading", panel, "전술 단말", new Vector2(640, 54), new Vector2(0, 270), 36);
            var summary = Label("Room Summary", panel, "", new Vector2(650, 126), new Vector2(0, 166), 22);
            var map = Button("Select Map", panel, "전장 · 연구 구역", new Vector2(0, 44)); map.gameObject.AddComponent<WaitingMapChoice>();
            var duration = Button("Match Duration", panel, "경기 시간 · 3분", new Vector2(0, -40));
            var start = Button("Start Match", panel, "경기 시작", new Vector2(0, -146));
            var starter = start.gameObject.AddComponent<BattleStartController>();
            var so = new SerializedObject(starter); so.FindProperty("_allowKeyboardShortcut").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            var close = Button("Close", panel, "닫기 · Esc", new Vector2(0, -244));
            var prompt = Rect("Terminal Prompt", canvasGo.transform, new Vector2(420, 68), new Vector2(0, -150));
            prompt.gameObject.AddComponent<Image>().color = new Color(.02f, .07f, .1f, .88f);
            prompt.gameObject.AddComponent<TerminalTouchInput>();
            var promptText = Label("Prompt", prompt, "E 길게 누르기 · 전술 단말", new Vector2(408, 48), new Vector2(0, 5), 22);
            var progress = Rect("Hold Progress", prompt, new Vector2(392, 4), new Vector2(0, -27)).gameObject.AddComponent<Image>();
            progress.sprite = Circle; progress.type = Image.Type.Filled; progress.fillMethod = Image.FillMethod.Horizontal;
            progress.color = new Color(.1f, 1, .88f); progress.raycastTarget = false; progress.fillAmount = 0;
            RemodelUiBuilder.Ref(terminal, "_panel", panel.gameObject); RemodelUiBuilder.Ref(terminal, "_prompt", prompt.gameObject);
            RemodelUiBuilder.Ref(terminal, "_promptText", promptText); RemodelUiBuilder.Ref(terminal, "_progress", progress);
            RemodelUiBuilder.Ref(terminal, "_roomSummary", summary); RemodelUiBuilder.Ref(terminal, "_durationLabel", duration.GetComponentInChildren<TMP_Text>());
            RemodelUiBuilder.Ref(terminal, "_durationButton", duration); RemodelUiBuilder.Ref(terminal, "_closeButton", close);
            RemodelUiBuilder.Ref(terminal, "_focusPoint", focus.transform);
            panel.gameObject.SetActive(false); prompt.gameObject.SetActive(false);
            var battleCanvas = GameObject.Find("Canvas_Battle");
            foreach (string name in new[] { "Start_button", "Map Choice" })
            { var old = battleCanvas.transform.Find(name); if (old != null) old.gameObject.SetActive(false); }
            var hint = battleCanvas.transform.Find("Waiting Controls");
            if (hint != null) hint.GetComponent<TMP_Text>().text = "중앙 단말 · E 길게 누르기    T · 커서    Esc · 설정";
        }
    }
}
