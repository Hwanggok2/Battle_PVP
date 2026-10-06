using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Logic;
using BattlePvp.Stats;
using NUnit.Framework;
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
    }
}
