using System;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.Remodel.Editor
{
    public static class RemodelUiBuilder
    {
        private static readonly Color Panel = new Color(.025f, .055f, .10f, .94f);
        private static readonly Color ButtonColor = new Color(.07f, .14f, .21f, .97f);
        private static readonly Color Cyan = new Color(.24f, .9f, .92f);
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
        private static Sprite Circle;

        public static void Ref(Component component, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(component); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Refs(Component component, string field, UnityEngine.Object[] values)
        {
            var so = new SerializedObject(component); var array = so.FindProperty(field); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Bool(Component component, string field, bool value)
        { var so = new SerializedObject(component); so.FindProperty(field).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position, Vector2? anchor = null)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); Place(rect, size, position, anchor); return rect;
        }
        private static void Place(Transform target, Vector2 size, Vector2 position, Vector2? anchor = null)
        {
            if (target == null) return;
            var rect = (RectTransform)target; rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position; rect.localScale = Vector3.one;
        }
        private static void Fill(Transform target, float inset = 0)
        {
            var r = (RectTransform)target; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f,.5f);
            r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset; r.localScale = Vector3.one;
        }
        private static Image Image(RectTransform rect, Color color)
        {
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>(); image.color = color; image.sprite = null; image.type = UnityEngine.UI.Image.Type.Simple; return image;
        }
        private static TMP_Text Text(string name, Transform parent, string value, Vector2 size, Vector2 pos, float fontSize = 22)
        {
            var rect = Rect(name, parent, size, pos); var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Font; label.text = value; label.fontSize = fontSize; label.color = new Color(.84f,.92f,.97f);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap; return label;
        }
        private static Button Button(string name, Transform parent, string value, Vector2 size, Vector2 pos)
        {
            var rect = Rect(name, parent, size, pos); var image = Image(rect, ButtonColor); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image; var label = Text("Label", rect, value, size, Vector2.zero); label.alignment = TextAlignmentOptions.Center;
            Outline(rect); rect.gameObject.AddComponent<NeonButtonSound>(); return button;
        }
        private static void Outline(RectTransform rect)
        {
            // UI.Outline duplicates the entire solid quad, tinting the panel interior.
            var old = rect.GetComponent<Outline>(); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var borders = rect.Cast<Transform>().Where(t=>t.name=="Neon Border").ToArray();
            foreach(var duplicate in borders.Skip(1))UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            var frame = borders.Length>0 ? (RectTransform)borders[0] : Rect("Neon Border", rect, Vector2.zero, Vector2.zero); Fill(frame);
            for (int i = 0; i < 4; i++)
            {
                var edge = frame.Find("Edge"+i) as RectTransform ?? Rect("Edge" + i, frame, Vector2.zero, Vector2.zero);
                edge.anchorMin = i == 0 ? new Vector2(0,1) : i == 3 ? new Vector2(1,0) : Vector2.zero;
                edge.anchorMax = i == 1 ? new Vector2(1,0) : i == 2 ? new Vector2(0,1) : Vector2.one;
                edge.sizeDelta = i < 2 ? new Vector2(0,1) : new Vector2(1,0);
                Image(edge, new Color(Cyan.r,Cyan.g,Cyan.b,.45f)).raycastTarget = false;
            }
        }
        private static Slider Slider(string name, Transform parent, string label, float y, float min, float max)
        {
            Text(name + "Label", parent, label, new Vector2(240,36), new Vector2(-204,y));
            Text(name + "Value", parent, "100%", new Vector2(70,36), new Vector2(278,y));
            var root = Rect(name, parent, new Vector2(260,32), new Vector2(78,y));
            var slider = root.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max;
            Image(Rect("Track", root, new Vector2(260,5), Vector2.zero), new Color(.14f,.23f,.31f));
            var fillArea = Rect("FillArea", root, new Vector2(260,5), Vector2.zero);
            var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.zero); Fill(fill); Image(fill,Cyan); slider.fillRect = fill;
            var area = Rect("HandleArea",root,new Vector2(252,22),Vector2.zero);
            var handle = Rect("Handle",area,new Vector2(10,0),Vector2.zero); slider.handleRect = handle; slider.targetGraphic = Image(handle,Cyan);
            return slider;
        }
        private static void RowButton(string name, Transform parent, string label, float y, string value)
        { Text(name + "Label", parent, label, new Vector2(330,36),new Vector2(-160,y)); Button(name,parent,value,new Vector2(220,40),new Vector2(184,y)); }

        public static void Style(Transform root)
        {
            foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            { scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = .5f; }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            { text.font = Font; text.color = new Color(.84f,.92f,.97f); text.raycastTarget = false; }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                string n = image.name.ToLowerInvariant();
                if (n.Contains("background") || n.Contains("panel") || n == "room") { image.sprite = null; image.color = Panel; }
                else if (n == "fill") image.color = Cyan;
            }
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic is Image image) { image.color = ButtonColor; image.sprite = null; Outline(image.rectTransform); }
                var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.6f,1f,1f); colors.pressedColor = new Color(.3f,.7f,.8f); button.colors = colors;
                if (button.GetComponent<NeonButtonSound>() == null) button.gameObject.AddComponent<NeonButtonSound>();
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) { Fill(label.transform, 5); label.alignment = TextAlignmentOptions.Center; label.fontSize = 22; label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 24; }
            }
            foreach(var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if(input.targetGraphic is Image background){background.color=ButtonColor;background.sprite=null;}
                if(input.textComponent!=null)input.textComponent.color=new Color(.84f,.92f,.97f);
                if(input.placeholder is TMP_Text placeholder)placeholder.color=new Color(.45f,.59f,.68f);
            }
        }

        private static void CreateSettings(Scene scene)
        {
            var previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "System UI"); if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject("System UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; root.GetComponent<Canvas>().sortingOrder = 150;
            var open = Button("Settings",root.transform,"설정",new Vector2(84,44),Vector2.zero); Place(open.transform,new Vector2(84,44),new Vector2(-68,-42),Vector2.one);
            var panel = Rect("SettingsPanel",root.transform,new Vector2(800,640),Vector2.zero); Image(panel,Panel); Outline(panel);
            Text("Title",panel,"설정",new Vector2(200,52),new Vector2(-252,265),30);
            Button("Close",panel,"닫기",new Vector2(80,38),new Vector2(324,268));
            string[] tabs={"System","Sound","Controls"}, labels={"시스템","사운드","조작"};
            for(int i=0;i<3;i++) Button(tabs[i]+"Tab",panel,labels[i],new Vector2(208,42),new Vector2(-230+i*230,202));
            var system=Rect("System",panel,new Vector2(700,390),new Vector2(0,-10));
            RowButton("Quality",system,"그래픽 품질",156,"보통"); RowButton("Fps",system,"최대 프레임",104,"60 FPS");
            Slider("Brightness",system,"밝기",52,.6f,1.4f); Slider("HudScale",system,"HUD 크기",0,.8f,1.25f); Slider("HudOpacity",system,"HUD 불투명도",-52,.35f,1f);
            RowButton("Fullscreen",system,"전체화면",-104,"전환");
            RowButton("Quit",system,"게임 종료",-163,"종료");
            var sound=Rect("Sound",panel,new Vector2(700,390),new Vector2(0,-10));
            Slider("Master",sound,"전체 음량",150,0,1); Slider("Music",sound,"배경 음악",96,0,1); Slider("Effects",sound,"효과음",42,0,1); Slider("Ui",sound,"UI 효과음",-12,0,1);
            RowButton("Mute",sound,"음소거",-66,"꺼짐"); RowButton("BackgroundMute",sound,"백그라운드 음소거",-120,"켜짐");
            Button("SoundTest",sound,"UI 소리 듣기",new Vector2(200,36),new Vector2(184,-170));
            var controls=Rect("Controls",panel,new Vector2(700,390),new Vector2(0,-10));
            Slider("Sensitivity",controls,"마우스 감도",150,.25f,2f); RowButton("InvertY",controls,"상하 반전",85,"꺼짐");
            RowButton("Key1",controls,"스킬 1",20,"Q"); RowButton("Key2",controls,"스킬 2",-45,"E");
            Text("ControlsHelp",controls,"WASD 이동   ·   Space 점프   ·   Ctrl 앉기\n클릭 공격 / 활 당기기   ·   Enter 채팅   ·   Esc 커서",new Vector2(650,85),new Vector2(0,-145),18).textWrappingMode=TextWrappingModes.Normal;
            Text("Notice",panel,"",new Vector2(700,42),new Vector2(0,-237),17);
            Button("Defaults",panel,"기본값",new Vector2(126,42),new Vector2(-271,-285));
            Button("Cancel",panel,"취소",new Vector2(126,42),new Vector2(127,-285));
            Button("Apply",panel,"적용",new Vector2(126,42),new Vector2(272,-285));
            var controller=root.AddComponent<GameSettingsPanel>(); Ref(controller,"_panel",panel.gameObject); Ref(controller,"_openButton",open);
            Style(root.transform); EnlargePanels(root.transform); panel.gameObject.SetActive(false);
        }

        private static void Login(Scene scene)
        {
            var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas");
            var background=canvas.transform.Find("Background"); if(background!=null)background.gameObject.SetActive(false);
            if(canvas.GetComponent<TitleScreenController>()!=null)
            {
                var titleText=canvas.transform.Find("Title").GetComponentInChildren<TMP_Text>();titleText.fontSize=100;titleText.enableAutoSizing=false;
                canvas.transform.Find("Title").GetComponent<Image>().color=Color.clear;
                var border=canvas.transform.Find("Title").GetComponent<Outline>();if(border!=null)UnityEngine.Object.DestroyImmediate(border);
                var frame=canvas.transform.Find("Title/Neon Border");if(frame!=null)UnityEngine.Object.DestroyImmediate(frame.gameObject);
                ConfigureTitlePrompt(canvas.transform);LocalizeLogin(canvas.transform);return;
            }
            var panel=Rect("LoginPanel",canvas.transform,new Vector2(520,560),Vector2.zero); Image(panel,Panel); Outline(panel);
            Text("Heading",panel,"로그인",new Vector2(390,58),new Vector2(0,215),34);
            string[] names={"ID_Input","Email_Input","PW_Input","Log_In","Sign_up","Login_Status"};
            float[] y={126,52,-22,-108,-173,-225};
            for(int i=0;i<names.Length;i++)
            {
                var child=canvas.transform.Find(names[i]); if(child==null)continue; child.SetParent(panel,false);
                Place(child,new Vector2(390,i==5?44:52),new Vector2(0,y[i]));
            }
            var title=Button("Title",canvas.transform,"BATTLE <color=#55E5E9>PVP</color>",new Vector2(960,180),Vector2.zero);
            title.GetComponent<Image>().color=Color.clear; UnityEngine.Object.DestroyImmediate(title.GetComponent<Outline>());
            UnityEngine.Object.DestroyImmediate(title.transform.Find("Neon Border").gameObject);
            var text=title.GetComponentInChildren<TMP_Text>(); text.fontSize=100; text.fontStyle=FontStyles.Bold | FontStyles.Italic;
            var controller=canvas.AddComponent<TitleScreenController>(); Ref(controller,"_loginPanel",panel.gameObject); Ref(controller,"_title",title);
            panel.gameObject.SetActive(false);
            ConfigureTitlePrompt(canvas.transform);
            LocalizeLogin(canvas.transform);
        }

        private static void ConfigureTitlePrompt(Transform canvas)
        {
            var title=canvas.Find("Title");if(title==null)return;
            Fill(title); // Any touch/click on the title screen opens login.
            var prompt=title.Find("Start Prompt")?.GetComponent<TMP_Text>() ?? Text("Start Prompt",title,"Touch to start",new Vector2(600,44),new Vector2(0,-118),25);
            prompt.text="Touch to start";prompt.alignment=TextAlignmentOptions.Center;prompt.color=new Color(.65f,.87f,.92f);prompt.raycastTarget=false;
        }

        private static void LocalizeLogin(Transform root)
        {
            var panel=root.Find("LoginPanel");
            Place(panel,new Vector2(520,500),Vector2.zero);
            Image((RectTransform)panel,Panel);Outline((RectTransform)panel);
            Place(panel.Find("Heading"),new Vector2(390,58),new Vector2(0,175));
            Place(panel.Find("ID_Input"),new Vector2(390,52),new Vector2(0,96));
            Place(panel.Find("PW_Input"),new Vector2(390,52),new Vector2(0,22));
            Place(panel.Find("Log_In"),new Vector2(390,52),new Vector2(0,-65));
            Place(panel.Find("Sign_up"),new Vector2(390,48),new Vector2(0,-129));
            LoginFeedback(root);
            foreach(var input in root.GetComponentsInChildren<TMP_InputField>(true))
                if(input.placeholder is TMP_Text text)text.text=input.name=="ID_Input"?"아이디":input.name=="PW_Input"?"비밀번호":"이메일";
            foreach(var button in root.GetComponentsInChildren<Button>(true))
                if(button.name=="Log_In"||button.name=="Sign_up")button.GetComponentInChildren<TMP_Text>(true).text=button.name=="Log_In"?"로그인":"회원가입";
        }

        private static void LoginFeedback(Transform root)
        {
            var ui = root.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayFabLoginUI>(true)).First();
            var status = new SerializedObject(ui).FindProperty("_statusText").objectReferenceValue as TextMeshProUGUI;
            if (status == null) return;
            var banner = root.Find("Login Feedback") as RectTransform ?? Rect("Login Feedback", root, new Vector2(600,100), Vector2.zero);
            Place(banner, new Vector2(600,100), new Vector2(0,-82), new Vector2(.5f,1));
            Image(banner,Panel).raycastTarget=false; Outline(banner);
            var heading = banner.Find("Heading")?.GetComponent<TMP_Text>() ?? Text("Heading",banner,"접속 단말",new Vector2(536,24),new Vector2(8,25),16);
            var accent = banner.Find("Accent") as RectTransform ?? Rect("Accent",banner,new Vector2(3,68),new Vector2(-284,0));
            var activity = banner.Find("Activity") as RectTransform ?? Rect("Activity",banner,new Vector2(536,2),new Vector2(8,-36));
            status.transform.SetParent(banner,false); Place(status.transform,new Vector2(536,44),new Vector2(8,-9));
            status.font=Font; status.fontSize=22; status.enableAutoSizing=true;status.fontSizeMin=16;status.fontSizeMax=22;
            status.alignment=TextAlignmentOptions.MidlineLeft;status.richText=false;status.textWrappingMode=TextWrappingModes.Normal;status.raycastTarget=false;
            var view=banner.GetComponent<LoginStatusBanner>()??banner.gameObject.AddComponent<LoginStatusBanner>();
            Ref(view,"_heading",heading);Ref(view,"_message",status);
            var accentImage=Image(accent,Cyan);accentImage.raycastTarget=false;Ref(view,"_accent",accentImage);
            var activityImage=Image(activity,Cyan);activityImage.raycastTarget=false;Ref(view,"_activity",activityImage);
            Ref(ui,"_statusBanner",view);banner.SetAsLastSibling();banner.gameObject.SetActive(false);
        }

        private static void ConfigureAttackEffect(GameObject player)
        {
            var fx=player.GetComponent<BlockAttackVfx>()??player.AddComponent<BlockAttackVfx>();
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=cube.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.DestroyImmediate(cube);
            Ref(fx,"_cube",mesh);
            Ref(fx,"_material",AttackMaterial("AttackGlow",new Color(.08f,1.5f,1.8f,.65f)));
            Ref(fx,"_accentMaterial",AttackMaterial("AttackAccent",new Color(1.35f,.15f,1.8f,.7f)));
            var sword = new SerializedObject(player.GetComponent<PlayerCombat>()).FindProperty("_handSwordVisual").objectReferenceValue as GameObject;
            if (sword != null)
            {
                Ref(fx,"_blade",sword.transform);
                var bladeRenderer=sword.GetComponent<Renderer>();
                if(bladeRenderer!=null)
                {
                    var bounds=bladeRenderer.localBounds;
                    var fields=new SerializedObject(fx);
                    fields.FindProperty("_bladeBase").vector3Value=new Vector3(bounds.center.x,bounds.center.y,bounds.min.z+bounds.size.z*.22f);
                    fields.FindProperty("_bladeTip").vector3Value=new Vector3(bounds.center.x,bounds.center.y,bounds.max.z);
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            const string bladePath="Assets/Remodel/Materials/BladeSweep.mat";
            var bladeMaterial=AssetDatabase.LoadAssetAtPath<Material>(bladePath);
            if(bladeMaterial==null){bladeMaterial=new Material(Shader.Find("BattlePvp/BladeSweep"));AssetDatabase.CreateAsset(bladeMaterial,bladePath);}
            Ref(fx,"_bladeMaterial",bladeMaterial);
        }

        private static void EnlargePanels(Transform root)
        {
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if(rect.name!="Canvas_Customizer" && rect.name!="PlayerINFO" && rect.name!="SettingsPanel" &&
                    !(rect.name=="Room" && rect.parent!=null && rect.parent.name=="Battle_Panel"))continue;
                var sizing=rect.GetComponent<ExpandedPanelLayout>()??rect.gameObject.AddComponent<ExpandedPanelLayout>();
                sizing.Refresh();
            }
        }

        private static void WaitingStartHint(Scene scene)
        {
            var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas_Battle").transform;
            var start=canvas.Find("Start_button");
            if(start!=null)
            {
                Place(start,new Vector2(300,58),new Vector2(-195,70),new Vector2(1,0));
                var label=start.GetComponentInChildren<TMP_Text>(true);
                if(label!=null){label.text="G · 경기 시작";label.fontSize=25;}
                start.GetComponent<Button>().navigation=new Navigation {mode=Navigation.Mode.None};
            }
            var help=canvas.Find("Waiting Controls")?.GetComponent<TMP_Text>()??
                Text("Waiting Controls",canvas,"",new Vector2(540,30),Vector2.zero,18);
            Place(help.transform,new Vector2(540,30),new Vector2(-315,23),new Vector2(1,0));
            help.text="Esc 커서  ·  Enter 채팅";
            help.alignment=TextAlignmentOptions.Right;help.raycastTarget=false;
        }

        private static void RoomBrowserLayout(Transform root)
        {
            var room = root.Find("Battle_Panel/Room");
            if (room == null) return;
            Place(room, new Vector2(900,620), Vector2.zero);
            Image((RectTransform)room,new Color(.018f,.035f,.065f,1));
            Place(room.Find("Heading"),new Vector2(650,44),new Vector2(-85,270));
            Place(room.Find("Close"),new Vector2(90,38),new Vector2(358,270));
            Place(room.Find("Room_List"),new Vector2(820,320),new Vector2(0,-35));
            Place(room.Find("CreateRoom"),new Vector2(200,48),new Vector2(-294,-270));
            Place(room.Find("Refresh"),new Vector2(150,48),new Vector2(58,-270));
            Place(room.Find("EnterRoom"),new Vector2(200,48),new Vector2(284,-270));
            var current = room.Find("Current Room")?.GetComponent<TMP_Text>() ??
                Text("Current Room",room,"",new Vector2(780,72),new Vector2(0,206),22);
            current.textWrappingMode=TextWrappingModes.NoWrap;
            current.overflowMode=TextOverflowModes.Ellipsis;
            var subheading = room.Find("List Heading")?.GetComponent<TMP_Text>() ??
                Text("List Heading",room,"대기실 목록",new Vector2(780,32),new Vector2(0,146),20);
            var hint = room.Find("Browse Hint")?.GetComponent<TMP_Text>() ??
                Text("Browse Hint",room,"",new Vector2(800,40),new Vector2(0,-222),16);
            hint.textWrappingMode=TextWrappingModes.Normal;
            var leave = room.Find("Leave Room")?.GetComponent<Button>() ??
                Button("Leave Room",room,"대기실 나가기",new Vector2(220,48),new Vector2(-284,-270));
            leave.targetGraphic.color=new Color(.26f,.07f,.15f,.98f);
            leave.navigation=new Navigation {mode=Navigation.Mode.None};
            var list=room.GetComponentInChildren<RoomListManager>(true);
            var status=room.Find("List Status")?.GetComponent<TMP_Text>() ??
                Text("List Status",room,"불러오는 중…",new Vector2(760,46),new Vector2(0,-35),22);
            status.alignment=TextAlignmentOptions.Center;
            Ref(list,"_statusText",status);
            var controller=room.GetComponent<WaitingRoomPanel>()??room.gameObject.AddComponent<WaitingRoomPanel>();
            Ref(controller,"_heading",room.Find("Heading").GetComponent<TMP_Text>());
            Ref(controller,"_currentRoom",current);Ref(controller,"_browseHint",hint);
            Ref(controller,"_listHeading",subheading);
            Ref(controller,"_createButton",room.Find("CreateRoom").gameObject);
            Ref(controller,"_joinButton",room.Find("EnterRoom").gameObject);
            Ref(controller,"_leaveButton",leave);Ref(controller,"_roomList",list);
            // Above HUD controls while open, with the same responsive sizing as other common panels.
            var canvas=room.GetComponent<Canvas>();
            if(canvas==null)canvas=room.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting=true;canvas.sortingOrder=180;
            if(room.GetComponent<GraphicRaycaster>()==null)room.gameObject.AddComponent<GraphicRaycaster>();
            foreach(var button in room.GetComponentsInChildren<Button>(true))button.navigation=new Navigation {mode=Navigation.Mode.None};
            EnlargePanels(root);
            var createPopup=room.Find("RoomSetting");
            if(createPopup!=null){createPopup.SetAsLastSibling();Image((RectTransform)createPopup,new Color(Panel.r,Panel.g,Panel.b,1));}
            current.gameObject.SetActive(false);hint.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            room.gameObject.SetActive(false);
        }

        private static void ConfigureImpactAndDebug(GameObject root)
        {
            foreach(var hitbox in root.GetComponentsInChildren<MeleeHitBox>(true))
            { Bool(hitbox,"_drawDebugHitPath",false);Bool(hitbox,"_drawDebugHitPathInGame",false); }
            var targets=root.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c is HealthSystem || c is DummyHealth);
            foreach(var target in targets)
            {
                var effect=target.GetComponent<HitImpactVfx>()??target.gameObject.AddComponent<HitImpactVfx>();
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
                Ref(effect,"_square",quad.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(quad);
                Ref(effect,"_material",AssetDatabase.LoadAssetAtPath<Material>("Assets/Remodel/Materials/BladeAfterimage.mat"));
            }
        }

        [MenuItem("Battle PvP/Remodel/Apply Cursor And Room Menu")]
        public static void ApplyCursorAndRoomMenu()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save existing scene changes first.");
            string previous=SceneManager.GetActiveScene().path;
            foreach(string path in new[]{"Assets/Prefabs/Lobby_UI.prefab","Assets/Prefabs/Player.prefab","Assets/Prefabs/Dummy.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(path.EndsWith("Lobby_UI.prefab"))RoomBrowserLayout(root.transform);
                    ConfigureImpactAndDebug(root);RecordPrefabChanges(root);PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            foreach(string name in new[]{"Login","Lobby","Battle_waiting","Battle"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
                foreach(var root in scene.GetRootGameObjects())
                {
                    if(root.name=="Lobby_UI")RoomBrowserLayout(root.transform);
                    ConfigureImpactAndDebug(root);
                    foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
                        if(text.name=="ControlsHelp")text.text="WASD 이동   ·   Space 점프   ·   Ctrl 앉기\n클릭 공격 / 활 당기기   ·   Enter 채팅\nEsc 커서 전환   ·   대기실 방장 G 시작";
                }
                if(name=="Battle_waiting" || name=="Battle")
                {
                    var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas_Battle").transform;
                    var hint=canvas.Find("Waiting Controls")?.GetComponent<TMP_Text>() ??
                        Text("Waiting Controls",canvas,"",new Vector2(580,30),Vector2.zero,18);
                    Place(hint.transform,new Vector2(580,30),new Vector2(0,23),new Vector2(.5f,0));
                    hint.text="Esc 커서  ·  Enter 채팅";hint.alignment=TextAlignmentOptions.Center;
                    var input=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BattlePvp.Logic.GameInputController>(true)).First();
                    Ref(input,"_cursorHint",hint);
                    var banner=canvas.GetComponentInChildren<BattleRoomInfoBanner>(true);
                    if(banner!=null)
                    {
                        var button=new SerializedObject(banner).FindProperty("_leaveButton").objectReferenceValue as Button;
                        if(button!=null){button.GetComponentInChildren<TMP_Text>(true).text="방 정보";button.targetGraphic.color=ButtonColor;}
                    }
                }
                foreach(var root in scene.GetRootGameObjects())RecordPrefabChanges(root);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);AssetDatabase.SaveAssets();
        }

        [MenuItem("Battle PvP/Remodel/Apply Blade Trail And Panel Sizing")]
        public static void ApplyBladeAndPanelSizing()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save existing scene changes first.");
            string previous=SceneManager.GetActiveScene().path;
            foreach(string path in new[]{"Assets/Prefabs/UI_Root.prefab","Assets/Prefabs/Lobby_UI.prefab","Assets/Prefabs/Player.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(path.EndsWith("/Player.prefab"))ConfigureAttackEffect(root);
                    EnlargePanels(root.transform);RecordPrefabChanges(root);PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            foreach(string name in new[]{"Login","Lobby","Battle_waiting","Battle"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
                foreach(var root in scene.GetRootGameObjects())
                {
                    if(root.GetComponent<PlayerCombat>()!=null)ConfigureAttackEffect(root);
                    EnlargePanels(root.transform);RecordPrefabChanges(root);
                }
                if(name=="Battle_waiting")WaitingStartHint(scene);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);AssetDatabase.SaveAssets();
        }

        private static Material AttackMaterial(string name,Color color)
        {
            string path="Assets/Remodel/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
            material.enableInstancing=true;material.SetColor("_BaseColor",color);
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",2);
            material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_SrcBlendAlpha",(float)UnityEngine.Rendering.BlendMode.Zero);material.SetFloat("_DstBlendAlpha",(float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;material.SetOverrideTag("RenderType","Transparent");
            EditorUtility.SetDirty(material);return material;
        }

        [MenuItem("Battle PvP/Remodel/Apply Camera And Feedback Corrections")]
        public static void ApplyFeedbackCorrections()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save existing scene changes first.");
            string previous=SceneManager.GetActiveScene().path;
            var player=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try { ConfigureAttackEffect(player);RecordPrefabChanges(player);PrefabUtility.SaveAsPrefabAsset(player,"Assets/Prefabs/Player.prefab"); }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            foreach(string name in new[]{"Login","Lobby","Battle_waiting"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
                if(name=="Login")LoginFeedback(scene.GetRootGameObjects().First(g=>g.name=="Canvas").transform);
                else RemodelMapBuilder.ConfigureCamera(scene,name);
                if(name=="Battle_waiting")WaitingCameraHint(scene);
                foreach(var root in scene.GetRootGameObjects())RecordPrefabChanges(root);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);AssetDatabase.SaveAssets();
        }

        private static void WaitingCameraHint(Scene scene)
        {
            var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas_Battle").transform;
            var old=canvas.Find("Camera Hint");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        private static void RestoreScreenVfx(Transform root)
        {
            foreach(var binder in root.GetComponentsInChildren<UIIdentityGlitchBinder>(true))
            {
                if(binder.name!="Status_VFX")continue;
                var canvas=binder.GetComponentInParent<Canvas>();if(canvas==null)continue;
                if(binder.transform.parent!=canvas.transform)binder.transform.SetParent(canvas.transform,false);
                Fill(binder.transform);binder.transform.SetAsFirstSibling();binder.gameObject.SetActive(true);
                var graphic=binder.GetComponent<RawImage>();graphic.color=new Color(1,1,1,.36078432f);graphic.raycastTarget=false;
            }
        }

        private static void AnchorChat(BattleChatUI chat)
        {
            var settings=new SerializedObject(chat);
            var panel=settings.FindProperty("_panelRect").objectReferenceValue as RectTransform;
            if(panel==null)panel=chat.transform as RectTransform;
            var bottom=panel.TransformPoint(new Vector3(0,panel.rect.yMin,0));
            panel.pivot=new Vector2(panel.pivot.x,0);
            panel.position+=bottom-panel.TransformPoint(new Vector3(0,panel.rect.yMin,0));
            var content=settings.FindProperty("_contentRect").objectReferenceValue as RectTransform;
            if(content!=null){content.anchorMin=Vector2.zero;content.anchorMax=new Vector2(1,0);content.pivot=new Vector2(.5f,0);content.anchoredPosition=Vector2.zero;}
            var label=settings.FindProperty("_logText").objectReferenceValue as TMP_Text;
            if(label!=null)label.alignment=TextAlignmentOptions.BottomLeft;
        }

        [MenuItem("Battle PvP/Remodel/Restore FPS Input And Screen Effects")]
        public static void ApplyInputRestore()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save existing scene changes first.");
            string previous=SceneManager.GetActiveScene().path;
            foreach(string path in new[]{"Assets/Prefabs/UI_Root.prefab","Assets/Prefabs/Player.prefab"})
            {
                var contents=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    RestoreScreenVfx(contents.transform);
                    foreach(var chat in contents.GetComponentsInChildren<BattleChatUI>(true))AnchorChat(chat);
                    RecordPrefabChanges(contents);PrefabUtility.SaveAsPrefabAsset(contents,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(contents);}
            }
            foreach(string name in new[]{"Login","Lobby","Battle_waiting","Battle"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
                if(name=="Login")ConfigureTitlePrompt(scene.GetRootGameObjects().First(g=>g.name=="Canvas").transform);
                if(name=="Lobby"||name=="Battle_waiting")RemodelMapBuilder.ConfigureCamera(scene,name);
                if(name=="Battle_waiting")WaitingCameraHint(scene);
                foreach(var root in scene.GetRootGameObjects())
                {
                    RestoreScreenVfx(root.transform);
                    foreach(var chat in root.GetComponentsInChildren<BattleChatUI>(true))AnchorChat(chat);
                    RecordPrefabChanges(root);
                }
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);AssetDatabase.SaveAssets();
        }

        private static void LobbyLayout(Transform root)
        {
            var container=root.Find("Battle_Panel");
            if(container!=null&&container.TryGetComponent<Image>(out var background)){background.color=Color.clear;background.raycastTarget=false;}
            var stat=root.Find("Stat"); Place(stat,new Vector2(120,46),new Vector2(-196,-42),Vector2.one);
            if(stat!=null)stat.GetComponentInChildren<TMP_Text>(true).text="스탯";
            var battle=root.Find("Battle_Panel/Battle"); Place(battle,new Vector2(240,64),new Vector2(176,160),Vector2.zero);
            if(battle!=null)battle.GetComponentInChildren<TMP_Text>(true).text="전투 참가";
            var room=root.Find("Battle_Panel/Room"); Place(room,new Vector2(900,560),Vector2.zero);
            if(room!=null)
            {
                Image((RectTransform)room,Panel); Outline((RectTransform)room);
                Place(room.Find("Room_List"),new Vector2(820,355),new Vector2(0,12));
                Image((RectTransform)room.Find("Room_List"),new Color(.04f,.085f,.135f,.95f));
                if(room.Find("Heading")==null)Text("Heading",room,"대기실 목록",new Vector2(650,44),new Vector2(-85,228),27);
                if(room.Find("Close")==null)
                {var close=Button("Close",room,"닫기",new Vector2(90,38),new Vector2(358,230));UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(close.onClick,room.gameObject.SetActive,false);}
                Place(room.Find("CreateRoom"),new Vector2(200,48),new Vector2(-294,-228));
                Place(room.Find("Refresh"),new Vector2(150,48),new Vector2(58,-228));
                Place(room.Find("EnterRoom"),new Vector2(200,48),new Vector2(284,-228));
                var create=room.Find("RoomSetting");Place(create,new Vector2(620,200),new Vector2(0,20));Image((RectTransform)create,Panel);Outline((RectTransform)create);
                Place(create.Find("RoomName"),new Vector2(560,48),new Vector2(0,25));
                var input=create.Find("RoomName").GetComponent<TMP_InputField>();if(input.placeholder is TMP_Text placeholder)placeholder.text="대기실 이름";
                Place(create.Find("SaveSetting"),new Vector2(160,42),new Vector2(199,-54));
                if(create.Find("Cancel")==null)
                {var close=Button("Cancel",create,"취소",new Vector2(130,42),new Vector2(28,-54));UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(close.onClick,create.gameObject.SetActive,false);}
            }
            var chat=root.Find("BattleChatUI"); Place(chat,new Vector2(460,210),new Vector2(258,130),Vector2.zero);
            if(chat!=null&&chat.GetComponent<BattleChatUI>()!=null)AnchorChat(chat.GetComponent<BattleChatUI>());
            if(root.Find("GameTitle")==null)
            {var title=Text("GameTitle",root,"BATTLE <color=#55E5E9>PVP</color>",new Vector2(370,48),Vector2.zero,30);Place(title.transform,new Vector2(370,48),new Vector2(221,-42),new Vector2(0,1));title.fontStyle=FontStyles.Bold|FontStyles.Italic;}
        }

        private static void HudLayout(Transform root)
        {
            var health=root.Find("Canvas_HUD/Health_Root");
            Place(health,new Vector2(340,108),new Vector2(200,85),Vector2.zero);
            if(health!=null)
            {
                var visibility=health.GetComponent<HudVisibilitySettings>() ?? health.gameObject.AddComponent<HudVisibilitySettings>();
                visibility.ShowInWaitingRoom=true;
                var back=health.Find("HP_Back"); if(back!=null){Fill(back);Image((RectTransform)back,Panel);}
                Place(health.Find("HP_Fill"),new Vector2(286,16),new Vector2(0,4));
                Place(health.Find("Overflow_Fill"),new Vector2(286,6),new Vector2(0,-20));
                Place(health.Find("HP_Fill/HP_Text"),new Vector2(286,34),new Vector2(0,30));
                var hpText=health.Find("HP_Fill/HP_Text").GetComponent<TMP_Text>();hpText.fontSize=24;hpText.enableAutoSizing=false;
                Place(health.Find("HitDamage"),new Vector2(200,40),new Vector2(0,86));
            }
            var identity=root.Find("Canvas_HUD/Identity_Widget");
            Place(identity,new Vector2(340,50),new Vector2(200,169),Vector2.zero);
            if(identity!=null)
            {
                if(identity.GetComponent<HudVisibilitySettings>()==null)identity.gameObject.AddComponent<HudVisibilitySettings>();
                Image((RectTransform)identity,Panel);
                var frame=identity.Find("Frame");if(frame!=null){Fill(frame);Image((RectTransform)frame,Color.clear).raycastTarget=false;}
                if(identity.Find("IdentityName")==null)Text("IdentityName",identity,"",new Vector2(266,38),new Vector2(25,0),20);
            }
            RestoreScreenVfx(root);
            var old=root.Find("SkillUI"); if(old!=null)old.gameObject.SetActive(false);
            foreach(var oldArc in root.Cast<Transform>().Where(t=>t.name=="Skill Arc").ToArray())UnityEngine.Object.DestroyImmediate(oldArc.gameObject);
            var arc=Rect("Skill Arc",root,new Vector2(300,280),new Vector2(-40,70),new Vector2(1,0)); arc.pivot=new Vector2(1,0);
            arc.gameObject.AddComponent<CanvasGroup>(); var controller=arc.gameObject.AddComponent<SkillArcHud>();
            var slots=new SkillUI[2];var keys=new TMP_Text[2];var buttons=new Button[2];
            for(int i=0;i<2;i++)
            {
                var button=Button("Skill"+(i+1),arc,"",new Vector2(92,92),i==0?new Vector2(-182,68):new Vector2(-106,178));
                Place(button.transform,new Vector2(92,92),((RectTransform)button.transform).anchoredPosition,new Vector2(1,0));
                var baseImage=button.GetComponent<Image>();baseImage.sprite=Circle;baseImage.color=ButtonColor;
                UnityEngine.Object.DestroyImmediate(button.transform.Find("Neon Border").gameObject);
                UnityEngine.Object.DestroyImmediate(button.GetComponentInChildren<TMP_Text>().gameObject);
                var mask=Image(Rect("Icon Mask",button.transform,new Vector2(88,88),Vector2.zero),Color.white);
                mask.sprite=Circle; mask.raycastTarget=false;
                mask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
                var icon=Image(Rect("Icon",mask.transform,new Vector2(88,88),Vector2.zero),Color.white);
                icon.preserveAspect=false;
                button.gameObject.AddComponent<SkillTooltip>();
                var overlay=Image(Rect("Cooldown",button.transform,new Vector2(86,86),Vector2.zero),new Color(0,0,0,.7f));overlay.sprite=Circle;
                var timer=Text("Timer",button.transform,"",new Vector2(70,34),new Vector2(0,3),28);timer.alignment=TextAlignmentOptions.Center;
                var label=Text("SkillName",button.transform,"",new Vector2(145,25),new Vector2(0,-62),17);label.alignment=TextAlignmentOptions.Center;
                var key=Text("Index",button.transform,i==0?"Q":"E",new Vector2(30,27),new Vector2(-41,44),19);key.color=Cyan;key.alignment=TextAlignmentOptions.Center;
                var skill=button.gameObject.AddComponent<SkillUI>();Ref(skill,"_root",button.transform);Ref(skill,"_baseImage",icon);Ref(skill,"_overlayImage",overlay);Ref(skill,"_timerText",timer);Ref(skill,"_nameText",label);Ref(skill,"_indexText",key);Bool(skill,"_useDirectKeyLabel",true);
                slots[i]=skill;keys[i]=key;buttons[i]=button;
            }
            var attack=Button("Attack",arc,"공격",new Vector2(84,84),Vector2.zero);Place(attack.transform,new Vector2(84,84),new Vector2(-30,26),new Vector2(1,0));
            attack.GetComponent<Image>().sprite=Circle;attack.gameObject.AddComponent<CombatHudAttackInput>();
            UnityEngine.Object.DestroyImmediate(attack.transform.Find("Neon Border").gameObject);
            Refs(controller,"_slots",slots);Refs(controller,"_keys",keys);Refs(controller,"_buttons",buttons);
            Place(root.Find("Player Icon Button"),new Vector2(120,46),new Vector2(-330,-42),Vector2.one);
            CustomizerLayout(root.Find("Stat_Setting/Canvas_Customizer"));
            InfoLayout(root.Find("CharacterInfo_Panel"));
            EnlargePanels(root);
        }

        private static void CustomizerLayout(Transform panel)
        {
            if(panel==null)return;
            Place(panel,new Vector2(1020,690),Vector2.zero); Image((RectTransform)panel,Panel); Outline((RectTransform)panel);
            foreach(var layout in panel.GetComponentsInChildren<LayoutGroup>(true))UnityEngine.Object.DestroyImmediate(layout);
            var bg=panel.Find("Background");if(bg!=null)bg.gameObject.SetActive(false);
            var group=panel.Find("Stat_Group");Fill(group);if(group.TryGetComponent<Image>(out var image)){image.color=Color.clear;image.raycastTarget=false;}
            var left=group.Find("Left_PrimaryStats");Place(left,new Vector2(410,340),new Vector2(-240,5));
            string[] stats={"STR","AGI","CON","DEF"};
            for(int i=0;i<stats.Length;i++)
            {
                var row=left.Find(stats[i]+"_Row");Place(row,new Vector2(370,26),new Vector2(0,112-i*76));
                Place(row.Find("Text"),new Vector2(370,30),new Vector2(0,32));
                var label=row.Find("Text").GetComponent<TMP_Text>();label.fontSize=21;label.alignment=TextAlignmentOptions.MidlineLeft;
                Place(row.Find("Background"),new Vector2(370,7),Vector2.zero);
                Image((RectTransform)row.Find("Background"),new Color(.14f,.23f,.31f));
                Place(row.Find("Fill Area"),new Vector2(370,7),Vector2.zero);
                Place(row.Find("Handle Slide Area"),new Vector2(360,22),Vector2.zero);
                var handle=(RectTransform)row.Find("Handle Slide Area/Handle");handle.sizeDelta=new Vector2(10,0);handle.GetComponent<Image>().color=Cyan;
                var item=row.Find(stats[i]+"_Row_Item");Place(item,new Vector2(370,7),Vector2.zero);
                foreach(var graphic in item.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            }
            var right=group.Find("Right_DerivedStats");Place(right,new Vector2(390,340),new Vector2(255,10));
            int index=0;foreach(Transform child in right){Place(child,new Vector2(370,34),new Vector2(0,144-index++*44));var text=child.GetComponent<TMP_Text>();if(text!=null){text.fontSize=21;text.alignment=TextAlignmentOptions.MidlineLeft;}}
            var preview=panel.Find("Identity_Preview");Place(preview,new Vector2(900,122),new Vector2(0,-204));Image((RectTransform)preview,new Color(.045f,.10f,.16f));
            Place(preview.Find("Icon"),new Vector2(66,66),new Vector2(-383,0));
            Place(preview.Find("Icon/Text (TMP)"),new Vector2(250,32),new Vector2(175,15));
            Place(preview.Find("Description"),new Vector2(480,52),new Vector2(-30,-14));
            Place(preview.Find("RemainStatus"),new Vector2(230,36),new Vector2(310,5));
            foreach(var text in preview.GetComponentsInChildren<TMP_Text>(true)){text.fontSize=20;text.alignment=TextAlignmentOptions.MidlineLeft;}
            string[] presets={"Preset1","Preset2","Preset3","Strategist_Preset"};
            for(int i=0;i<presets.Length;i++){var button=panel.Find(presets[i]);Place(button,new Vector2(i==3?235:196,46),new Vector2(i==3?331:-342+i*215,244));button.GetComponentInChildren<TMP_Text>().text=i==3?"전략가 프리셋":"프리셋 "+(i+1);}
            Place(panel.Find("ApplyButton"),new Vector2(200,46),new Vector2(353,-304));
            if(panel.Find("Heading")==null)Text("Heading",panel,"능력치",new Vector2(600,40),new Vector2(-154,305),28);
            if(panel.Find("Close")==null)
            {
                var close=Button("Close",panel,"닫기",new Vector2(90,38),new Vector2(397,304));
                UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(close.onClick,panel.gameObject.SetActive,false);
            }
        }

        private static void InfoLayout(Transform root)
        {
            if(root==null)return;
            if(root.TryGetComponent<Image>(out var overlay)){overlay.color=new Color(0,0,0,.5f);overlay.sprite=null;}
            var info=root.Find("PlayerINFO");Place(info,new Vector2(800,570),Vector2.zero);Image((RectTransform)info,Panel);Outline((RectTransform)info);
            string[] left={"PlayerID","STR","AGI","CON","DEF","KillCount","DeathCouont","KDA"};
            string[] right={"ATK","DEF_Rate","MaxHP","Pene","Regen","MoveSpd","AtkSpd"};
            for(int i=0;i<left.Length;i++)Place(info.Find(left[i]),new Vector2(310,38),new Vector2(-175,213-i*56));
            for(int i=0;i<right.Length;i++)Place(info.Find(right[i]),new Vector2(310,38),new Vector2(175,157-i*56));
            foreach(var text in info.GetComponentsInChildren<TMP_Text>(true)){text.fontSize=22;text.alignment=TextAlignmentOptions.MidlineLeft;}
            if(info.Find("Close")==null)
            {
                var close=Button("Close",info,"닫기",new Vector2(90,38),new Vector2(310,236));
                UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(close.onClick,root.gameObject.SetActive,false);
            }
        }

        [MenuItem("Battle PvP/Remodel/Apply Approved UI")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode before applying UI.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save existing scene changes before applying UI.");
            foreach(string pipelinePath in new[]{"Assets/Settings/PC_RPAsset.asset","Assets/Settings/Mobile_RPAsset.asset"})
            {
                var pipeline=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(pipelinePath);
                var settings=new SerializedObject(pipeline);settings.FindProperty("m_AdditionalLightsRenderingMode").intValue=2;
                settings.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=4;settings.FindProperty("m_MSAA").intValue=2;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            string previous=SceneManager.GetActiveScene().path;
            Circle=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            foreach(string name in new[]{"UI_Root","Lobby_UI","_Room","RankingEntry","Start_button"})
            {
                string path="Assets/Prefabs/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try {Style(root.transform);if(name=="UI_Root")HudLayout(root.transform);if(name=="Lobby_UI")LobbyLayout(root.transform);if(name=="_Room")RoomRowLayout(root.transform);if(name=="RankingEntry")RankingRowLayout(root.transform);PrefabUtility.SaveAsPrefabAsset(root,path);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var player=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                var uiRoot=player.GetComponentsInChildren<Canvas>(true).First(c=>c.name=="UI_Root").transform;
                HudLayout(uiRoot);
                if(player.GetComponent<SkillInputSettings>()==null)player.AddComponent<SkillInputSettings>();
                ConfigureAttackEffect(player);
                var arc=player.GetComponentInChildren<SkillArcHud>(true);
                foreach(var view in player.GetComponentsInChildren<PlayerHudView>(true))BindHud(view,uiRoot,arc);
                RecordPrefabChanges(player);
                PrefabUtility.SaveAsPrefabAsset(player,"Assets/Prefabs/Player.prefab");
            }
            finally{PrefabUtility.UnloadPrefabContents(player);}
            foreach(string name in new[]{"Login","Lobby","Battle_waiting","Battle"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
                foreach(var root in scene.GetRootGameObjects())
                {
                    if(root.GetComponent<Canvas>()!=null)Style(root.transform);
                    if(root.name=="Lobby_UI")LobbyLayout(root.transform);
                    if(root.name=="Remodel Environment"&&root.GetComponent<NeonEnvironmentSettings>()==null)root.AddComponent<NeonEnvironmentSettings>();
                    if(root.GetComponent<PlayerCombat>()!=null)
                    {
                        var hud=root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c=>c.name=="UI_Root");
                        if(hud!=null)HudLayout(hud.transform);
                    }
                    foreach(var view in root.GetComponentsInChildren<PlayerHudView>(true))
                    {
                        var hud=root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c=>c.name=="UI_Root");
                        if(hud!=null)BindHud(view,hud.transform,root.GetComponentInChildren<SkillArcHud>(true));
                    }
                }
                if(name=="Login")Login(scene);else CreateSettings(scene);
                if(name=="Battle_waiting")
                {
                    WaitingCameraHint(scene);
                    var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas_Battle");
                    var banner=canvas.GetComponentInChildren<BattleRoomInfoBanner>(true);
                    if(banner!=null)
                    {
                        var fields=new SerializedObject(banner);
                        var bar=fields.FindProperty("_bannerRoot").objectReferenceValue as RectTransform;
                        if(bar!=null)
                        {
                            foreach(var layout in bar.GetComponents<LayoutGroup>())UnityEngine.Object.DestroyImmediate(layout);
                            Place(bar,new Vector2(1490,56),new Vector2(0,-107),new Vector2(.5f,1));Image(bar,Panel);
                            Place(((Component)fields.FindProperty("_roomNameText").objectReferenceValue).transform,new Vector2(500,42),new Vector2(-450,0));
                            Place(((Component)fields.FindProperty("_playerCountText").objectReferenceValue).transform,new Vector2(180,42),new Vector2(0,0));
                            Place(((Component)fields.FindProperty("_masterNameText").objectReferenceValue).transform,new Vector2(370,42),new Vector2(330,0));
                            Place(((Component)fields.FindProperty("_leaveButton").objectReferenceValue).transform,new Vector2(130,40),new Vector2(650,0));
                        }
                    }
                    var old=canvas.transform.Find("Map Choice");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                    var choice=Button("Map Choice",canvas.transform,"전장 · 연구 구역",new Vector2(330,52),Vector2.zero);
                    Place(choice.transform,new Vector2(330,52),new Vector2(-45,70),new Vector2(.5f,0));choice.gameObject.AddComponent<WaitingMapChoice>();
                    var start=canvas.transform.Find("Start_button");Place(start,new Vector2(230,52),new Vector2(-164,70),new Vector2(1,0));
                    WaitingStartHint(scene);
                }
                if(name=="Battle")
                {
                    var lobby=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Lobby_UI");
                    if(lobby!=null&&lobby.transform.Find("GameTitle")!=null)lobby.transform.Find("GameTitle").gameObject.SetActive(false);
                    var canvas=scene.GetRootGameObjects().First(g=>g.name=="Canvas_Battle");
                    var start=canvas.transform.Find("Start_button");
                    if(start!=null)
                    {
                        start.gameObject.SetActive(true);start.GetComponent<Button>().enabled=false;
                        var startController=start.GetComponent<BattleStartController>();if(startController!=null)startController.enabled=false;
                        Place(start,new Vector2(240,86),new Vector2(0,-62),new Vector2(.5f,1));Image((RectTransform)start,Panel).raycastTarget=false;
                        foreach(string labelName in new[]{"Timer","State"})
                        {
                            var label=start.Find(labelName);if(label==null)continue;
                            Place(label,new Vector2(230,labelName=="Timer"?40:26),new Vector2(0,labelName=="Timer"?12:-24));
                            var text=label.GetComponent<TMP_Text>();text.fontSize=labelName=="Timer"?32:17;text.enableAutoSizing=false;text.alignment=TextAlignmentOptions.Center;
                            text.text=labelName=="Timer"?"00:00":string.Empty;
                        }
                    }
                    var tester=canvas.transform.Find("Button (Legacy)");if(tester!=null)tester.gameObject.SetActive(false);
                    ResultLayout(canvas.transform.Find("Result"));
                    Place(canvas.transform.Find("RankingUIManager"),new Vector2(390,320),new Vector2(225,-240),new Vector2(0,1));
                    var state=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BattlePvp.Networking.BattleStateMachine>(true)).First();
                    var labels=new SerializedObject(state);labels.FindProperty("_restartPrompt").stringValue="Enter · 대기실로 돌아가기";labels.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach(var root in scene.GetRootGameObjects())RecordPrefabChanges(root);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);AssetDatabase.SaveAssets();
        }

        private static void RecordPrefabChanges(GameObject root)
        {
            foreach(var component in root.GetComponentsInChildren<Component>(true))
                if(component!=null&&PrefabUtility.IsPartOfPrefabInstance(component))PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        private static void BindHud(PlayerHudView view,Transform root,SkillArcHud arc)
        {
            Ref(view,"_skillArcHud",arc);
            Ref(view,"_identityText",root.Find("Canvas_HUD/Identity_Widget/IdentityName").GetComponent<TMP_Text>());
        }

        private static void RoomRowLayout(Transform row)
        {
            Place(row,new Vector2(795,62),Vector2.zero);Image((RectTransform)row,new Color(.06f,.12f,.19f));
            Place(row.Find("RoomName"),new Vector2(335,50),new Vector2(-215,0));
            Place(row.Find("Master"),new Vector2(280,50),new Vector2(105,0));
            Place(row.Find("PlayerCount"),new Vector2(120,50),new Vector2(325,0));
            foreach(var text in row.GetComponentsInChildren<TMP_Text>(true)){text.fontSize=20;text.alignment=TextAlignmentOptions.MidlineLeft;}
            var select=row.Find("SelectButton");Fill(select);select.GetComponent<Image>().color=Color.clear;
        }

        private static void ResultLayout(Transform panel)
        {
            if(panel==null)return;
            Place(panel,new Vector2(980,660),Vector2.zero);Image((RectTransform)panel,Panel);Outline((RectTransform)panel);
            string[] names={"Result","Winner","PlayerName","Ranking","HitDamage","TakeDamage","ManyKill","ManyDie","RestartText"};
            Vector2[] positions={new Vector2(0,265),new Vector2(0,181),new Vector2(-220,90),new Vector2(220,90),new Vector2(-220,10),new Vector2(220,10),new Vector2(-220,-86),new Vector2(220,-86),new Vector2(0,-260)};
            for(int i=0;i<names.Length;i++)
            {
                var child=panel.Find(names[i]);if(child==null)continue;
                Place(child,new Vector2(i<2||i==8?850:400,i==8?52:60),positions[i]);
                var text=child.GetComponent<TMP_Text>();text.fontSize=i==0?42:i==1?30:23;text.enableAutoSizing=false;text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.Normal;
                if(i==0)text.text="경기 결과";
            }
        }

        private static void RankingRowLayout(Transform row)
        {
            Place(row,new Vector2(390,38),Vector2.zero);
            var background=row.Find("Background");Fill(background);
            string[] panels={"Panel","Name_Panel","Kill_Panel","Death_Panel"};
            float[] widths={36,234,50,50},xs={-172,-37,105,160};
            for(int i=0;i<panels.Length;i++)
            {
                var panel=row.Find(panels[i]);Place(panel,new Vector2(widths[i],38),new Vector2(xs[i],0));
                if(panel.TryGetComponent<Image>(out var image))image.color=Color.clear;
                foreach(Transform child in panel)
                {
                    if(child.GetComponent<TMP_Text>()==null)continue;
                    Fill(child,2);var text=child.GetComponent<TMP_Text>();text.fontSize=18;text.enableAutoSizing=false;text.alignment=i==1?TextAlignmentOptions.MidlineLeft:TextAlignmentOptions.Center;
                    foreach(Transform nested in child)if(nested.GetComponent<TMP_Text>()!=null)nested.gameObject.SetActive(false);
                }
            }
        }
    }
}
