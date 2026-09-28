using System.IO;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using BodyPart = BattlePvp.Combat.BodyPart;

namespace BattlePvp.Remodel.Editor
{
    public static class TacticalRoomBuilder
    {
        private const string Folder = "Assets/Remodel/Textures/";
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
        private static Material _alloy, _cyan, _pink, _amber;
        public static void ApplyCombatPolish()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            string previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            foreach (var scene in Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt))
                if (scene.isDirty) throw new System.InvalidOperationException("Save existing scene edits first.");
            Materials(); BuildSpire(); CombatSoundBuilder.GenerateSwings();
            EditorSceneManager.OpenScene("Assets/Scenes/Battle_waiting.unity");
            BuildDummies(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity");
            foreach (var arc in Object.FindObjectsByType<SkillArcHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                WaitingCombatBuilder.FitIcons(arc);
            var old = GameObject.Find("Lobby Training Range"); if (old != null) Object.DestroyImmediate(old);
            var range = new GameObject("Lobby Training Range").AddComponent<BattlePvp.Lobby.LobbyTrainingRange>();
            RemodelUiBuilder.Ref(range, "_dummyPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            GeneratePreviews();
        }
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            string previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            foreach (var scene in Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt))
                if (scene.isDirty) throw new System.InvalidOperationException("Save existing scene edits first.");
            Materials(); ConfigureLocomotion(); BuildSpire();
            WaitingCombatBuilder.Apply();
            BuildDummies(); BuildDetails();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            var battle = EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
            var environment = GameObject.Find("Remodel Environment");
            foreach (Transform old in environment.transform.Cast<Transform>().Where(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == "Assets/Remodel/Prefabs/spire.prefab").ToArray())
                Object.DestroyImmediate(old.gameObject);
            var tower = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/spire.prefab"), environment.transform);
            tower.SetActive(false);
            RemodelUiBuilder.Ref(Object.FindFirstObjectByType<BattleMapSelection>(), "_spire", tower);
            EditorSceneManager.SaveScene(battle); AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            GeneratePreviews();
        }
        private static void ConfigureLocomotion()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            if (!controller.parameters.Any(p => p.name == "LocomotionRate")) controller.AddParameter(new AnimatorControllerParameter { name = "LocomotionRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            foreach (var layer in controller.layers)
                foreach (var child in layer.stateMachine.states)
                    if (child.state.name == "Movement" || child.state.name == "Crouch Walk")
                    { child.state.speedParameter = "LocomotionRate"; child.state.speedParameterActive = true; EditorUtility.SetDirty(child.state); }
            EditorUtility.SetDirty(controller);
        }
        public static void GeneratePreviews() => QueuePreview(0);
        private static void QueuePreview(int index)
        {
            if (index > 3) { AssetDatabase.Refresh(); return; }
            string id = index == 0 ? "arena" : index == 1 ? "foundry" : "spire";
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(index == 3 ? "Assets/Prefabs/Player.prefab" : "Assets/Remodel/Prefabs/" + id + ".prefab"));
            root.hideFlags = HideFlags.DontSave;
            Vector3 portraitTarget = new Vector3(0, 1.4f, 0);
            if (index == 3)
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                var animator = root.GetComponent<Animator>();
                var idle = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle"));
                if (idle != null) idle.SampleAnimation(root, 0);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) portraitTarget = root.transform.InverseTransformPoint(head.position) + Vector3.up * .04f;
            }
            root.transform.position = new Vector3(2000 + index * 300, 0, 0);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            // URP GPU instance data needs a rendered editor frame, not just two delayCall callbacks.
            double captureAfter = EditorApplication.timeSinceStartup + .5;
            SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.CallbackFunction capture = null;
            capture = () =>
            {
                if (EditorApplication.timeSinceStartup < captureAfter) return;
                EditorApplication.update -= capture;
                if (root == null) return;
                try
                {
                    if (index == 3) Capture(root, portraitTarget + new Vector3(0, .02f, .9f), portraitTarget, 224, 240, Folder + "player-portrait.png", 32);
                    else Capture(root, new Vector3(26, 21, -29), new Vector3(0, 1.8f, 0), 760, 360, Folder + "map-preview-" + index + ".png", 52);
                }
                finally { Object.DestroyImmediate(root); }
                QueuePreview(index + 1);
            };
            EditorApplication.update += capture;
        }
        private static void Capture(GameObject target, Vector3 cameraPosition, Vector3 lookAt, int width, int height, string path, float fieldOfView)
        {
            Vector3 offset = target.transform.position;
            foreach (Transform child in target.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var cameraGo = new GameObject("Asset preview camera", typeof(Camera)); var camera = cameraGo.GetComponent<Camera>();
            var lightGo = new GameObject("Asset preview light", typeof(Light)); var light = lightGo.GetComponent<Light>();
            var texture = new RenderTexture(width, height, 24); var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                light.type = LightType.Directional; light.intensity = width < 300 ? 2.5f : 1.2f; light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(25, 155, 0);
                camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .04f, .07f);
                camera.fieldOfView = fieldOfView; camera.nearClipPlane = .03f; camera.farClipPlane = 140;
                camera.transform.position = offset + cameraPosition; camera.transform.LookAt(offset + lookAt); camera.targetTexture = texture;
                camera.Render(); RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(image); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo); }
        }
        private static void Materials()
        {
            _alloy = Mat("spire-alloy", new Color(.065f, .085f, .12f), false);
            _cyan = Mat("spire-cyan", new Color(.02f, .8f, 1), true);
            _pink = Mat("spire-magenta", new Color(.8f, .04f, .4f), true);
            _amber = Mat("spire-amber", new Color(1, .5f, .04f), true);
        }
        private static Material Mat(string name, Color color, bool glow)
        {
            string path = "Assets/Remodel/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Remodel/Materials/terminal-alloy.mat").shader); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color); mat.enableInstancing = true;
            if (glow) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 2); }
            EditorUtility.SetDirty(mat); return mat;
        }
        private static GameObject Box(Transform root, string name, Vector3 position, Vector3 size, Material material, bool solid = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        private static void WorldText(Transform parent, string text, Vector3 pos, float width, Color color)
        {
            var label = new GameObject("Sign " + text.Split('\n')[0], typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            label.transform.SetParent(parent, false); label.transform.localPosition = pos;
            label.font = Font; label.text = text; label.fontSize = 2; label.color = color; label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(width, 1.2f);
        }
        private static void BuildSpire()
        {
            var root = new GameObject("Neon_spire");
            try
            {
                var r = root.transform;
                Box(r, "Courtyard", new Vector3(0, -.25f, 0), new Vector3(62, .5f, 52), _alloy);
                for (int x = -28; x <= 28; x += 4) Box(r, "Floor grid X", new Vector3(x, .012f, 0), new Vector3(.035f, .015f, 50), _cyan, false);
                for (int z = -24; z <= 24; z += 4) Box(r, "Floor grid Z", new Vector3(0, .013f, z), new Vector3(60, .015f, .035f), _pink, false);
                Box(r, "Roof deck", new Vector3(0, 4, 0), new Vector3(12, .4f, 10), _alloy);
                Box(r, "West wall", new Vector3(-5.8f, 1.9f, 0), new Vector3(.4f, 3.8f, 10), _alloy);
                Box(r, "East wall", new Vector3(5.8f, 1.9f, 0), new Vector3(.4f, 3.8f, 10), _alloy);
                foreach (int side in new[] {-1, 1})
                {
                    foreach (int half in new[] {-1, 1}) Box(r, "Door wall", new Vector3(half * 3.8f, 1.9f, side * 4.8f), new Vector3(4.4f, 3.8f, .4f), _alloy);
                    Box(r, "Door header", new Vector3(0, 3.4f, side * 4.8f), new Vector3(3.2f, .8f, .4f), _alloy);
                    Box(r, "Entrance light", new Vector3(0, 2.95f, side * 5.03f), new Vector3(3.2f, .08f, .05f), _amber, false);
                    Box(r, "Roof front cover", new Vector3(0, 4.6f, side * 4.8f), new Vector3(12, .8f, .35f), _alloy);
                    Box(r, "Roof neon", new Vector3(0, 4.26f, side * 5.06f), new Vector3(12, .08f, .05f), _pink, false);
                    Box(r, "Roof east cover", new Vector3(5.8f, 4.6f, side * 3.1f), new Vector3(.35f, .8f, 3.8f), _alloy);
                    Foothold(r, "Roof front foothold " + side, new Vector3(2.8f, 4.375f, side * 4.05f), new Vector3(1.5f, .35f, 1.15f));
                    Foothold(r, "Roof east foothold " + side, new Vector3(5.05f, 4.375f, side * 3.1f), new Vector3(1.15f, .35f, 1.5f));
                }
                Box(r, "Roof west cover", new Vector3(-5.8f, 4.6f, 0), new Vector3(.35f, .8f, 10), _alloy);
                Foothold(r, "Roof west foothold", new Vector3(-5.05f, 4.375f, -2), new Vector3(1.15f, .35f, 1.5f));
                Box(r, "Rooftop generator", new Vector3(-2, 4.9f, 1), new Vector3(2.4f, 1.4f, 1.6f), _alloy);
                Foothold(r, "Generator foothold", new Vector3(-2, 4.55f, -.4f), new Vector3(1.6f, .7f, 1.2f));
                Box(r, "Generator strip", new Vector3(-2, 5.2f, .18f), new Vector3(2, .2f, .04f), _cyan, false);
                Box(r, "Stair landing", new Vector3(6.6f, 4.05f, 0), new Vector3(2.4f, .3f, 2.2f), _alloy);
                var stairs = new GameObject("Spiral Stairs").transform; stairs.SetParent(r, false); stairs.localPosition = new Vector3(9, 0, 0);
                Box(stairs, "Central column", new Vector3(0, 2.25f, 0), new Vector3(1.3f, 4.5f, 1.3f), _alloy);
                // Each convex wedge is an actual walkable tread; 14 cm rise, 1.8 m clear width.
                for (int i = 0; i < 30; i++)
                {
                    float a0 = (180 + i * 12) * Mathf.Deg2Rad, a1 = (180 + (i + 1) * 12) * Mathf.Deg2Rad;
                    float top = (i + 1) * .14f;
                    var vertices = new Vector3[8];
                    for (int j = 0; j < 4; j++)
                    {
                        float radius = j == 0 || j == 3 ? 1.4f : 3.2f; float angle = j < 2 ? a0 : a1;
                        vertices[j] = new Vector3(Mathf.Cos(angle) * radius, top, Mathf.Sin(angle) * radius);
                        vertices[j + 4] = vertices[j] - Vector3.up * .18f;
                    }
                    var mesh = new Mesh { name = "Spiral tread " + i, vertices = vertices,
                        triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7} };
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    string meshPath = "Assets/Remodel/Meshes/spire-step-" + i + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
                    else AssetDatabase.CreateAsset(mesh, meshPath);
                    var step = new GameObject("Step " + i, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)); step.transform.SetParent(stairs, false);
                    step.GetComponent<MeshFilter>().sharedMesh = mesh; step.GetComponent<MeshRenderer>().sharedMaterial = _alloy;
                    step.GetComponent<MeshCollider>().sharedMesh = mesh; step.GetComponent<MeshCollider>().convex = true;
                    float mid = (a0 + a1) * .5f;
                    var edge = Box(stairs, "Step light", new Vector3(Mathf.Cos(mid) * 2.3f, top + .015f, Mathf.Sin(mid) * 2.3f), new Vector3(1.75f, .025f, .035f), _cyan, false);
                    edge.transform.localRotation = Quaternion.Euler(0, -mid * Mathf.Rad2Deg, 0);
                    if (i % 2 == 0) Box(stairs, "Outer rail post", new Vector3(Mathf.Cos(mid) * 3.2f, top + .48f, Mathf.Sin(mid) * 3.2f), new Vector3(.09f, .96f, .09f), _alloy);
                }
                for (int i = 0; i < 10; i++)
                {
                    float x = i % 2 == 0 ? -18 : 18, z = -18 + i / 2 * 9;
                    Box(r, "Courtyard cover", new Vector3(x, .65f, z), new Vector3(3.8f, 1.3f, 2), _alloy);
                    Foothold(r, "Courtyard foothold " + i, new Vector3(x, .325f, z - 1.6f), new Vector3(1.6f, .65f, 1.2f));
                    Box(r, "Cover beacon", new Vector3(x, 1.32f, z), new Vector3(3.8f, .05f, 2), i % 2 == 0 ? _pink : _amber, false);
                }
                foreach (int side in new[] {-1, 1})
                {
                    Box(r, "Boundary X", new Vector3(side * 31, 2, 0), new Vector3(.5f, 4, 52), _alloy);
                    Box(r, "Boundary Z", new Vector3(0, 2, side * 26), new Vector3(62, 4, .5f), _alloy);
                    for (int i = -2; i <= 2; i++)
                    {
                        float h = 9 + ((i + 3) % 3) * 4;
                        Box(r, "Skyline", new Vector3(i * 12, h * .5f, side * 33), new Vector3(8, h, 6), _alloy, false);
                        for (int y = 2; y < h; y += 3) Box(r, "Skyline sign", new Vector3(i * 12, y, side * 29.9f), new Vector3(6, .3f, .05f), i % 2 == 0 ? _cyan : _pink, false);
                    }
                }
                WorldText(r, "HELIX / 02", new Vector3(0, 3.45f, -5.05f), 6, Color.cyan);
                WorldText(r, "옥상 진입", new Vector3(9, 1.8f, -3.5f), 4, Color.cyan);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Remodel/Prefabs/spire.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void Foothold(Transform root, string name, Vector3 position, Vector3 size)
        {
            Box(root, name, position, size, _alloy);
            Box(root, name + " light", position + Vector3.up * (size.y * .5f + .012f),
                new Vector3(size.x, .024f, .06f), _cyan, false);
        }
        private static void BuildDummies()
        {
            var old = GameObject.Find("Training Range"); if (old != null) Object.DestroyImmediate(old);
            var group = new GameObject("Training Range");
            var dummy = new GameObject("Training Dummy");
            try
            {
                dummy.AddComponent<NetworkIdentity>(); dummy.AddComponent<StatManager>();
                var health = dummy.AddComponent<DummyHealth>();
                var so = new SerializedObject(health); so.FindProperty("_requireBodyPartHitboxes").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
                Box(dummy.transform, "Plinth", new Vector3(0, .08f, 0), new Vector3(1.6f, .16f, 1.3f), _alloy);
                Part(dummy.transform, "Head", new Vector3(0, 1.8f, 0), new Vector3(.4f, .4f, .42f), _pink, BodyPart.Head);
                Part(dummy.transform, "Torso", new Vector3(0, 1.2f, 0), new Vector3(.65f, .72f, .38f), _alloy, BodyPart.Body);
                Part(dummy.transform, "Left arm", new Vector3(-.49f, 1.25f, 0), new Vector3(.25f, .6f, .3f), _alloy, BodyPart.Body);
                Part(dummy.transform, "Right arm", new Vector3(.49f, 1.25f, 0), new Vector3(.25f, .6f, .3f), _alloy, BodyPart.Body);
                foreach (int side in new[] {-1,1}) Part(dummy.transform, "Leg", new Vector3(side * .2f, .51f, 0), new Vector3(.27f, .64f, .32f), _cyan, BodyPart.Legs);
                Box(dummy.transform, "Target", new Vector3(0, 1.3f, -.205f), new Vector3(.24f, .22f, .015f), _amber, false);
                WorldText(dummy.transform, "훈련 표적\n<size=60%>머리 ×1.5   몸통 ×1   다리 ×0.8</size>", new Vector3(0, 2.65f, 0), 3.8f, new Color(.4f, 1, .9f));
                var prefab = PrefabUtility.SaveAsPrefabAsset(dummy, "Assets/Remodel/Prefabs/training-dummy.prefab");
                for (int i = 0; i < 3; i++)
                {
                    var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    placed.name = "Training Dummy " + (i + 1);
                    placed.transform.localPosition = new Vector3(-4 + i * 4, 0, 6.3f);
                }
            }
            finally { Object.DestroyImmediate(dummy); }
        }
        private static void Part(Transform parent, string name, Vector3 position, Vector3 size, Material mat, BodyPart part)
        {
            var go = Box(parent, name, position, size, mat);
            var marker = go.AddComponent<HitBodyPart>(); var so = new SerializedObject(marker); so.FindProperty("_bodyPart").enumValueIndex = (int)part; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Move(Transform target, Vector2 pos, Vector2 size)
        {
            var rect = (RectTransform)target; rect.anchoredPosition = pos; rect.sizeDelta = size;
            var text = target.GetComponentInChildren<TMP_Text>(); if (text != null && text.transform != target) text.rectTransform.sizeDelta = size - new Vector2(16, 4);
        }
        private static void BuildDetails()
        {
            var terminal = Object.FindFirstObjectByType<WaitingRoomTerminal>();
            var panel = (RectTransform)GameObject.Find("Terminal UI").transform.Find("Terminal Panel");
            panel.sizeDelta = new Vector2(1360, 900);
            var expanded = new SerializedObject(panel.GetComponent<ExpandedPanelLayout>()); expanded.FindProperty("_preferredScale").floatValue = 1; expanded.ApplyModifiedPropertiesWithoutUndo();
            Move(panel.Find("Heading"), new Vector2(-350, 390), new Vector2(580, 56)); panel.Find("Heading").GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
            Move(panel.Find("Close"), new Vector2(565, 395), new Vector2(150, 48));
            Move(panel.Find("Room Summary"), new Vector2(-350, -392), new Vector2(585, 52)); panel.Find("Room Summary").GetComponent<TMP_Text>().fontSize = 18;
            Move(panel.Find("Select Map"), new Vector2(345, 45), new Vector2(570, 48));
            Move(panel.Find("Match Duration"), new Vector2(-350, -228), new Vector2(585, 52));
            Move(panel.Find("Start Match"), new Vector2(-350, -310), new Vector2(585, 72));
            var detail = panel.gameObject.AddComponent<WaitingRoomDetails>();
            var titleLabel = RoomUiElements.Text("Room title label", panel, Font, "방 제목", new Vector2(585, 34), new Vector2(-350, 316));
            var title = RoomUiElements.Input("Room title input", panel, Font, "방 제목", new Vector2(585, 58), new Vector2(-350, 262));
            var capacity = RoomUiElements.Button("Capacity", panel, Font, "정원 · 8명", new Vector2(585, 54), new Vector2(-350, 182));
            var privateRoot = RoomUiElements.Rect("Private room", panel, new Vector2(585, 52), new Vector2(-350, 104));
            var toggle = privateRoot.gameObject.AddComponent<Toggle>();
            var background = RoomUiElements.Rect("Box", privateRoot, new Vector2(36, 36), new Vector2(-270, 0)).gameObject.AddComponent<Image>(); background.color = new Color(.07f,.17f,.22f);
            var check = RoomUiElements.Rect("Check", background.transform, new Vector2(22, 22), Vector2.zero).gameObject.AddComponent<Image>(); check.color = Color.cyan;
            toggle.targetGraphic = background; toggle.graphic = check; toggle.isOn = false;
            RoomUiElements.Text("Private label", privateRoot, Font, "비밀방 · 암호를 아는 플레이어만 입장", new Vector2(520, 50), new Vector2(28, 0));
            var password = RoomUiElements.Input("Room password", panel, Font, "암호 입력 · 기존 암호 유지 시 비워두기", new Vector2(585, 56), new Vector2(-350, 32), true);
            var save = RoomUiElements.Button("Save settings", panel, Font, "방 설정 저장", new Vector2(585, 56), new Vector2(-350, -53));
            var status = RoomUiElements.Text("Status", panel, Font, "", new Vector2(585, 88), new Vector2(-350, -139), 20);
            var preview = RoomUiElements.Rect("Map preview", panel, new Vector2(570, 270), new Vector2(345, 220)).gameObject.AddComponent<RawImage>(); preview.raycastTarget = false;
            RoomUiElements.Text("Roster title", panel, Font, "참가자", new Vector2(570, 36), new Vector2(345, -7), 24);
            var cards = new RoomPlayerCard[8];
            for (int i = 0; i < 8; i++)
            {
                var rect = RoomUiElements.Rect("Player card " + (i + 1), panel, new Vector2(278, 90), new Vector2(200 + (i % 2) * 292, -80 - (i / 2) * 98));
                rect.gameObject.AddComponent<Image>().color = new Color(.04f, .09f, .135f, 1);
                cards[i] = rect.gameObject.AddComponent<RoomPlayerCard>();
                var portrait = RoomUiElements.Rect("Portrait", rect, new Vector2(72, 78), new Vector2(-98, 0)).gameObject.AddComponent<RawImage>();
                portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "player-portrait.png"); portrait.raycastTarget = false;
                var name = RoomUiElements.Text("Name", rect, Font, "플레이어", new Vector2(176, 36), new Vector2(38, 20), 20);
                name.overflowMode = TextOverflowModes.Ellipsis;
                var kick = RoomUiElements.Button("Kick", rect, Font, "강퇴하기", new Vector2(176, 30), new Vector2(38, -22)); kick.GetComponentInChildren<TMP_Text>().fontSize = 16;
                var accent = RoomUiElements.Rect("Stat accent", rect, new Vector2(3, 90), new Vector2(-138, 0)).gameObject.AddComponent<Image>(); accent.raycastTarget = false;
                RemodelUiBuilder.Ref(cards[i], "_name", name); RemodelUiBuilder.Ref(cards[i], "_kick", kick); RemodelUiBuilder.Ref(cards[i], "_kickLabel", kick.GetComponentInChildren<TMP_Text>()); RemodelUiBuilder.Ref(cards[i], "_accent", accent);
                rect.gameObject.SetActive(false);
            }
            RemodelUiBuilder.Ref(detail, "_title", title); RemodelUiBuilder.Ref(detail, "_password", password); RemodelUiBuilder.Ref(detail, "_private", toggle);
            RemodelUiBuilder.Ref(detail, "_capacityButton", capacity); RemodelUiBuilder.Ref(detail, "_capacityText", capacity.GetComponentInChildren<TMP_Text>());
            RemodelUiBuilder.Ref(detail, "_save", save); RemodelUiBuilder.Ref(detail, "_status", status); RemodelUiBuilder.Ref(detail, "_mapPreview", preview);
            var serialized = new SerializedObject(detail); var array = serialized.FindProperty("_cards"); array.arraySize = 8;
            for (int i = 0; i < 8; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            array = serialized.FindProperty("_mapImages"); array.arraySize = 3;
            for (int i = 0; i < 3; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "map-preview-" + i + ".png");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
