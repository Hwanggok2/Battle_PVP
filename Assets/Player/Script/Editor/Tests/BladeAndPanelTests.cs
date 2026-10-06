using System;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.UI;
using BattlePvp.Stats;
using Mirror;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class BladeAndPanelTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene _scene;
        private string _name;
        [SetUp] public void SetUp()
        {
            _scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);_name=_scene.name;
        }
        [TearDown] public void TearDown()
        {
            foreach(var root in _scene.GetRootGameObjects())Object.DestroyImmediate(root);
            _scene.name=_name;
        }
        [TestCase(.1f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        [TestCase(2f)]
        public void TrailDurationMatchesTheActualClipAtDifferentAttackSpeeds(float speed)
        {
            _scene.name = "Lobby";
            var player = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            player.SetActive(true);
            var combat = player.GetComponent<PlayerCombat>();
            var animator = player.GetComponent<Animator>();
            Set(combat, "animator", animator);
            Set(combat, "_currentAttackSpeed", speed);
            typeof(PlayerCombat).GetMethod("PlayAttackAnimation", Private).Invoke(combat, new object[] { 0 });
            var state = animator.GetCurrentAnimatorStateInfo(1);
            var clip = animator.GetCurrentAnimatorClipInfo(1)[0].clip;
            float duration = clip.length / (speed * state.speed * state.speedMultiplier);
            Assert.That((float)Get(player.GetComponent<BlockAttackVfx>(), "_swingDuration"),
                Is.EqualTo(duration).Within(.001f), "Attack speed must be applied once, without truncating slow swings.");
        }

        [TestCase(1600,900)]
        [TestCase(1200,900)]
        [TestCase(900,1600)]
        public void EnlargedDialogFitsCanvasAndDoesNotAccumulateScaling(float width,float height)
        {
            var root=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(width,height);
            var panel=new GameObject("Dialog",typeof(RectTransform));panel.transform.SetParent(root.transform,false);
            var rect=panel.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1020,690);
            var sizing=panel.AddComponent<ExpandedPanelLayout>();sizing.Refresh();
            float scale=rect.localScale.x;
            Assert.That(rect.rect.width*scale,Is.LessThanOrEqualTo(width-63.9f));
            Assert.That(rect.rect.height*scale,Is.LessThanOrEqualTo(height-63.9f));
            if(width>=1200)Assert.That(scale,Is.GreaterThan(1));
            sizing.Refresh();Assert.That(rect.localScale.x,Is.EqualTo(scale));
        }
        [TestCase("Battle_waiting",false,true)]
        [TestCase("Battle",false,false)]
        [TestCase("Lobby",false,false)]
        [TestCase("Battle_waiting",true,false)]
        public void StartRequiresWaitingRoomAndRejectsRepeatedCountdown(string scene,bool busy,bool allowed)
        {
            _scene.name=scene;
            var stats=EditorTestLifecycle.AddNetwork<StatManager>(new GameObject("Allocated local player"));
            var allocation=new StatContainer(); allocation.STR.Invested=30;
            Set(stats,"_stats",allocation); stats.BindAsLocalScenePlayer();
            var root=new GameObject("Start",typeof(RectTransform),typeof(Button));
            var controller=root.AddComponent<BattleStartController>();Set(controller,"_isCountingDown",busy);
            Assert.That(typeof(BattleStartController).GetProperty("CanStart",Private).GetValue(controller),Is.EqualTo(allowed));
        }
        [Test]
        public void DisabledWaitingControllerDoesNotReplaceBattleTimerText()
        {
            _scene.name="Battle";
            var root=new GameObject("Timer",typeof(RectTransform),typeof(Button));
            var label=new GameObject("Label",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(root.transform,false);
            var text=label.GetComponent<TMPro.TextMeshProUGUI>();text.text="00:00";
            var controller=root.AddComponent<BattleStartController>();controller.enabled=false;
            EditorTestLifecycle.Invoke(controller,"Awake");EditorTestLifecycle.Invoke(controller,"OnDisable");
            Assert.That(text.text,Is.EqualTo("00:00"));
        }
        [Test]
        public void ClientAndEmptyServerCannotStartAMatch()
        {
            _scene.name="Battle_waiting";
            var controller=new GameObject("Start",typeof(RectTransform),typeof(Button)).AddComponent<BattleStartController>();
            var state=typeof(NetworkClient).GetField("connectState",BindingFlags.Static|BindingFlags.NonPublic);
            var server=typeof(NetworkServer).GetProperty("active",BindingFlags.Static|BindingFlags.Public);
            object previousClient=state.GetValue(null),previousServer=server.GetValue(null);
            try
            {
                state.SetValue(null,ConnectState.Connected);server.SetValue(null,false);
                Assert.That(typeof(BattleStartController).GetProperty("CanStart",Private).GetValue(controller),Is.False);
                server.SetValue(null,true);
                Assert.That(typeof(BattleStartController).GetProperty("CanStart",Private).GetValue(controller),Is.False);
            }
            finally{state.SetValue(null,previousClient);server.SetValue(null,previousServer);}
        }
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        private static object Get(object target,string field)=>target.GetType().GetField(field,Private).GetValue(target);
    }
}
