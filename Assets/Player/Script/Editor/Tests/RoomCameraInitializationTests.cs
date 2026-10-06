using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Logic;
using BattlePvp.Stats;
using BattlePvp.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.EditorTests
{
    public sealed class RoomCameraInitializationTests
    {
        private Scene _scene;
        [SetUp] public void Setup()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _scene.name = "Battle_waiting";
        }
        [TearDown] public void Cleanup()
        {
            foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void AssigningSpawnTargetPositionsCameraBeforePointerLockOrMenuClose(bool pointerMissing)
        {
            var input = new GameObject("Input").AddComponent<GameInputController>();
            EditorTestLifecycle.Invoke(input, "Awake");
            var field = typeof(GameInputController).GetField(pointerMissing ? "_webPointerMissing" : "_paused", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = field.GetValue(null);
            try
            {
                field.SetValue(null, true);
                var target = new GameObject("Spawned player").transform;
                target.SetPositionAndRotation(new Vector3(45, 2, -17), Quaternion.Euler(0, 135, 0));
                var camera = new GameObject("Camera").AddComponent<FollowCamera>();
                camera.transform.position = new Vector3(0, 20, 0);
                camera.SetTarget(target);
                Assert.That(GameInputController.IsPaused, Is.True);
                Assert.That(Vector3.Distance(camera.transform.position, camera.GetAimRay().origin), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, target.rotation), Is.LessThan(.001f));
            }
            finally { field.SetValue(null, previous); }
        }
        [Test] public void CameraCreatedAfterLocalPlayerBindsOnEnable()
        {
            var player = new GameObject("Local player", typeof(Mirror.NetworkIdentity)).AddComponent<StatManager>();
            var setLocal = typeof(StatManager).GetMethod("SetLocal", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = StatManager.Local;
            try
            {
                setLocal.Invoke(null, new object[] { player });
                var camera = new GameObject("Camera").AddComponent<FollowCamera>();
                EditorTestLifecycle.Invoke(camera, "OnEnable");
                Assert.That(camera.Target, Is.SameAs(player.transform));
                Assert.That(Vector3.Distance(camera.transform.position, camera.GetAimRay().origin), Is.LessThan(.001f));
                EditorTestLifecycle.Invoke(camera, "OnDisable");
            }
            finally { setLocal.Invoke(null, new object[] { previous }); }
        }

        [TestCase("default")]
        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void GiantMonostatScalesEyeHeightOnceAndRestoresItWhenSwitchingBack(string character)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(player);
            player.transform.position = new Vector3(10, 3, -7);
            var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0);
            var camera = new GameObject("Monostat camera").AddComponent<FollowCamera>();
            camera.SetTarget(player.transform);
            var manager = player.GetComponent<StatManager>();
            typeof(StatManager).GetField("_followCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, camera);
            var balanced = new StatContainer
            {
                STR = new StatSlot { Invested = 8 }, CON = new StatSlot { Invested = 8 },
                AGI = new StatSlot { Invested = 7 }, DEF = new StatSlot { Invested = 7 }
            };
            manager.ApplyLocalSceneStats(balanced);
            using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character), out var error), Is.True, error);
            float normalHeight = camera.GetAimRay().origin.y - player.transform.position.y;

            foreach (var stat in new[] { StatKind.STR, StatKind.CON })
            {
                var monostat = new StatContainer();
                if (stat == StatKind.STR) monostat.STR = new StatSlot { Invested = 30 };
                else monostat.CON = new StatSlot { Invested = 30 };
                manager.ApplyLocalSceneStats(monostat);
                Assert.That(player.transform.localScale.y, Is.EqualTo(1.2f));
                Assert.That(camera.GetAimRay().origin.y - player.transform.position.y,
                    Is.EqualTo(normalHeight * 1.2f).Within(.001f), character + " / " + stat + " must not add a second height bonus.");
                EditorTestLifecycle.Invoke(camera, "ApplyTargetPose");
                Assert.That(Vector3.Distance(camera.transform.position, camera.GetAimRay().origin), Is.LessThan(.001f));
                manager.ApplyLocalSceneStats(balanced);
                Assert.That(camera.GetAimRay().origin.y - player.transform.position.y, Is.EqualTo(normalHeight).Within(.001f));
            }
        }
    }
}
