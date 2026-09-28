using System.Linq;
using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Logic;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class FpsAndChatRestoreTests
    {
        private Scene _scene;
        private string _originalSceneName;
        [SetUp] public void SetUp()
        {
            _scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            _originalSceneName=_scene.name;
        }
        [TearDown] public void TearDown()
        {
            foreach(var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            _scene.name=_originalSceneName;
        }

        [TestCase("Battle",GameInputMode.Gameplay,true)]
        [TestCase("Battle_waiting",GameInputMode.Gameplay,true)]
        [TestCase("Lobby",GameInputMode.Gameplay,false)]
        [TestCase("Login",GameInputMode.Gameplay,false)]
        [TestCase("Battle_waiting",GameInputMode.Menu,false)]
        [TestCase("Battle_waiting",GameInputMode.TextInput,false)]
        [TestCase("Battle",GameInputMode.Results,false)]
        public void CursorPolicyFollowsSceneAndModalState(string scene,GameInputMode mode,bool locked) =>
            Assert.That(InputModeRules.CanLockCursor(scene,mode),Is.EqualTo(locked));

        [TestCase("Battle",1f,0f)]
        [TestCase("Battle",-1f,0f)]
        [TestCase("Battle",0f,-1f)]
        [TestCase("Battle_waiting",1f,0f)]
        [TestCase("Battle_waiting",-1f,0f)]
        [TestCase("Battle_waiting",0f,-1f)]
        public void StrafeAndBackpedalKeepTheCameraHeading(string scene,float x,float y)
        {
            _scene.name=scene;
            var camera=new GameObject("Local camera").AddComponent<FollowCamera>();
            var player=new GameObject("Local player");
            player.transform.position=Vector3.up*10;
            var movement=EditorTestLifecycle.AddNetwork<PlayerManager>(player);
            EditorTestLifecycle.Invoke(movement,"Awake");
            Set(movement,"animator",null);Set(movement,"followCamera",camera);Set(movement,"inputVector",new Vector2(x,y));
            camera.SetTarget(player.transform);Set(camera,"_yaw",73f);
            EditorTestLifecycle.Invoke(movement,"ApplyMovement");
            Assert.That(Quaternion.Angle(player.transform.rotation,Quaternion.Euler(0,73,0)),Is.LessThan(.01f));
        }

        [Test]
        public void CrouchingLowersBothCameraAndAimOriginAndRestoresOnStanding()
        {
            _scene.name = "Battle_waiting";
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake"); input.ResetToPlayMode();
            var player = new GameObject("Crouch camera target");
            var movement = EditorTestLifecycle.AddNetwork<PlayerManager>(player);
            EditorTestLifecycle.Invoke(movement, "Awake");
            Set(movement, "animator", null);
            var camera = new GameObject("Camera").AddComponent<FollowCamera>();
            camera.SetTarget(player.transform);
            Vector3 standing = camera.GetAimRay().origin;
            var crouch = typeof(PlayerManager).GetMethod("SetCrouchState", BindingFlags.Instance | BindingFlags.NonPublic);
            crouch.Invoke(movement, new object[] { true, false });
            var smooth = typeof(FollowCamera).GetMethod("UpdateCrouchHeight", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 120; i++) smooth.Invoke(camera, new object[] { 1f / 60 });
            EditorTestLifecycle.Invoke(camera, "LateUpdate");
            Assert.That(movement.CrouchCameraDrop, Is.GreaterThan(.5f));
            Assert.That(camera.GetAimRay().origin.y, Is.EqualTo(standing.y - movement.CrouchCameraDrop).Within(.002f));
            Assert.That(Vector3.Distance(camera.transform.position, camera.GetAimRay().origin), Is.LessThan(.001f));
            crouch.Invoke(movement, new object[] { false, false });
            for (int i = 0; i < 120; i++) smooth.Invoke(camera, new object[] { 1f / 60 });
            EditorTestLifecycle.Invoke(camera, "LateUpdate");
            Assert.That(Vector3.Distance(standing, camera.GetAimRay().origin), Is.LessThan(.002f));
        }

        [Test]
        public void GameplayCannotActivateTheRoomLeaveButton()
        {
            _scene.name="Battle_waiting";
            var input=new GameObject("Input").AddComponent<GameInputController>();EditorTestLifecycle.Invoke(input,"Awake");
            var root=new GameObject("Banner");var banner=root.AddComponent<BattleRoomInfoBanner>();
            var button=new GameObject("Leave",typeof(RectTransform),typeof(Button)).GetComponent<Button>();
            Set(banner,"_leaveButton",button);
            EditorTestLifecycle.Invoke(banner,"OnLeaveButtonClicked");
            Assert.That(button.interactable,Is.True,"Rejected gameplay input must not start the leave routine.");
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("Battle_waiting"));
        }

        [TestCase(.5f)]
        [TestCase(1f)]
        [TestCase(2f)]
        public void ChatResizeKeepsBottomAndLatestMessagesPinned(float scale)
        {
            var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var panel=Rect("Chat",canvas.transform);panel.sizeDelta=new Vector2(460,210);panel.anchoredPosition=new Vector2(250,130);panel.localScale=Vector3.one*scale;
            var chat=panel.gameObject.AddComponent<BattleChatUI>();
            var viewport=Rect("Viewport",panel);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.sizeDelta=Vector2.zero;
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();
            var content=Rect("Content",viewport);var text=content.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");text.fontSize=18;
            text.text=string.Join("\n",Enumerable.Range(1,30).Select(i=>"메시지 "+i));
            Set(chat,"_panelRect",panel);Set(chat,"_viewportRect",viewport);Set(chat,"_contentRect",content);Set(chat,"_logText",text);Set(chat,"_scrollRect",scroll);
            float bottom=Bottom(panel);
            EditorTestLifecycle.Invoke(chat,"ConfigureRuntimeComponents");
            foreach(float height in new[]{420f,150f,300f,210f})
            {
                chat.ResizeHeight(height);EditorTestLifecycle.Invoke(chat,"LateUpdate");
                Assert.That(Bottom(panel),Is.EqualTo(bottom).Within(.02f));
                Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(0).Within(.001f));
                Assert.That(Bottom(content),Is.EqualTo(Bottom(viewport)).Within(.1f));
                Assert.That(text.text.EndsWith("메시지 30"),Is.True);
            }
        }

        [Test]
        public void IdentityEffectFillsItsCanvasAndDoesNotInterceptInput()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI_Root.prefab");
            var binder=prefab.GetComponentsInChildren<UIIdentityGlitchBinder>(true).Single(b=>b.name=="Status_VFX");
            var rect=(RectTransform)binder.transform;var graphic=binder.GetComponent<RawImage>();
            Assert.That(rect.parent.GetComponent<Canvas>(),Is.Not.Null);
            Assert.That(rect.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(rect.anchorMax,Is.EqualTo(Vector2.one));
            Assert.That(rect.sizeDelta,Is.EqualTo(Vector2.zero));Assert.That(graphic.raycastTarget,Is.False);
            Assert.That(graphic.material.HasProperty("_StatColor"),Is.True);
            Assert.That(graphic.color.r,Is.EqualTo(1));
        }

        [TestCase(.5f)]
        [TestCase(1f)]
        [TestCase(2f)]
        public void ChatDragUsesPanelCoordinatesAtAnyScale(float scale)
        {
            var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var panel=Rect("Chat",canvas.transform);panel.sizeDelta=new Vector2(460,210);panel.localScale=Vector3.one*scale;
            var handle=Rect("ResizeHandle",panel);
            var chat=panel.gameObject.AddComponent<BattleChatUI>();
            Set(chat,"_panelRect",panel);Set(chat,"_resizeHandle",handle);
            EditorTestLifecycle.Invoke(chat,"ConfigureRuntimeComponents");
            var events=new GameObject("Events").AddComponent<EventSystem>();
            var pointer=new PointerEventData(events);
            pointer.position=RectTransformUtility.WorldToScreenPoint(null,panel.TransformPoint(new Vector3(0,210,0)));
            var trigger=handle.GetComponent<EventTrigger>();
            trigger.OnBeginDrag(pointer);
            pointer.position=RectTransformUtility.WorldToScreenPoint(null,panel.TransformPoint(new Vector3(0,260,0)));
            trigger.OnDrag(pointer);trigger.OnEndDrag(pointer);
            Assert.That(panel.rect.height,Is.EqualTo(260).Within(.02f));
        }

        [TestCase("Lobby")]
        [TestCase("Battle_waiting")]
        public void CameraSetupAddsMissingFollowComponent(string sceneName)
        {
            _scene.name=sceneName;
            var camera=new GameObject("Main Camera",typeof(Camera));
            var player=new GameObject("Player");
            EditorTestLifecycle.AddNetwork<BattlePvp.Stats.StatManager>(player);
            BattlePvp.Remodel.Editor.RemodelMapBuilder.ConfigureCamera(_scene,sceneName);
            var follow=camera.GetComponent<FollowCamera>();
            Assert.That(follow,Is.Not.Null);
            Assert.That(follow.enabled,Is.True);
            if(sceneName=="Lobby") Assert.That(follow.Target,Is.EqualTo(player.transform));
        }

        [TestCase(1f,-.75f)]
        [TestCase(-1f,.75f)]
        public void LobbyWheelUsesTheInputSystemsNormalizedNotch(float wheel,float expectedChange)
        {
            _scene.name="Lobby";
            var input=new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input,"Awake");
            var camera=new GameObject("Camera").AddComponent<FollowCamera>();
            camera.transform.position=new Vector3(0,1.2f,-6);
            camera.SetTarget(new GameObject("Player").transform);
            var previous=UnityEngine.InputSystem.Mouse.current;
            var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            try
            {
                mouse.MakeCurrent();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,
                    new UnityEngine.InputSystem.LowLevel.MouseState{scroll=new Vector2(0,wheel)});
                UnityEngine.InputSystem.InputSystem.Update();
                EditorTestLifecycle.Invoke(camera,"LateUpdate");
                float before=Vector3.Distance(camera.transform.position,Vector3.up*1.2f);
                EditorTestLifecycle.Invoke(camera,"Update");
                EditorTestLifecycle.Invoke(camera,"LateUpdate");
                Assert.That(Vector3.Distance(camera.transform.position,Vector3.up*1.2f)-before,
                    Is.EqualTo(expectedChange).Within(.001f));
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                if(previous!=null)previous.MakeCurrent();
            }
        }

        private static RectTransform Rect(string name,Transform parent)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
        private static float Bottom(RectTransform rect)=>rect.TransformPoint(new Vector3(0,rect.rect.yMin,0)).y;
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    }
}
