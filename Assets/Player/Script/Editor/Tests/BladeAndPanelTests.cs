using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
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

        [TestCase("default", 1f)] [TestCase("security-officer", 1f)] [TestCase("megumi", 1f)]
        [TestCase("casual-1", 1f)] [TestCase("picochan", 1f)] [TestCase("brute", 1f)]
        [TestCase("default", 1.2f)] [TestCase("security-officer", 1.2f)] [TestCase("megumi", 1.2f)]
        [TestCase("casual-1", 1.2f)] [TestCase("picochan", 1.2f)] [TestCase("brute", 1.2f)]
        public void EveryComboTrailEndsOnTheRenderedSword(string id, float scale)
        {
            _scene.name = "Lobby";
            var player = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(player);
            player.transform.SetPositionAndRotation(new Vector3(8, 3, -12), Quaternion.Euler(0, 65, 0));
            player.transform.localScale = Vector3.one * scale;
            var animator = player.GetComponent<Animator>(); animator.fireEvents = false; animator.Rebind(); animator.Update(0);
            var combat = player.GetComponent<PlayerCombat>(); Set(combat, "animator", animator); Set(combat, "_currentAttackSpeed", 1f);
            var fx = player.GetComponent<BlockAttackVfx>(); EditorTestLifecycle.Invoke(fx, "Awake");
            var sword = (Transform)Get(fx, "_blade"); sword.gameObject.SetActive(true);
            using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(id), out var error), Is.True, error);
            var visible = sword.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null).transform;
            var bladeBase = (Vector3)Get(fx, "_bladeBase"); var bladeTip = (Vector3)Get(fx, "_bladeTip");
            for (int combo = 0; combo < 3; combo++)
            {
                EditorTestLifecycle.Invoke(fx, "OnDisable");
                typeof(PlayerCombat).GetMethod("PlayAttackAnimation", Private).Invoke(combat, new object[] { combo });
                var data = (AttackData)Get(combat, "_meleeAnimationData");
                float end = (float)Get(fx, "_emissionEndPhase");
                for (int sample = 0; sample < 3; sample++)
                {
                    animator.Play(data.animationName, 1, end * sample * .3f); animator.Update(0);
                    skin.SyncPose();
                    typeof(BlockAttackVfx).GetMethod("UpdateBladeTrail", Private).Invoke(fx, new object[] { sample * .04f });
                }
                var vertices = (List<Vector3>)Get(fx, "_bladeVertices");
                Assert.That(vertices.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(Vector3.Distance(vertices[vertices.Count - 1], visible.TransformPoint(bladeBase)), Is.LessThan(.002f),
                    id + " combo " + combo + ": the trailing edge must meet the visible blade base.");
                Assert.That(Vector3.Distance(vertices[vertices.Count - 2], visible.TransformPoint(bladeTip)), Is.LessThan(.002f),
                    id + " combo " + combo + ": the trailing edge must meet the visible blade tip.");
                var light = (Light)Get(fx, "_light");
                if (light.enabled)
                    Assert.That(Vector3.Distance(light.transform.position, visible.TransformPoint((bladeBase + bladeTip) * .5f)), Is.LessThan(.002f));
            }
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
