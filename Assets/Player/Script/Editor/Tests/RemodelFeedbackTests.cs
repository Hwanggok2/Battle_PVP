using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class RemodelFeedbackTests
    {
        [TestCase("Login", false)]
        [TestCase("Lobby", true)]
        [TestCase("Battle_waiting", true)]
        [TestCase("Battle", true)]
        [TestCase("Unknown", false)]
        public void AttackGlowStartsInPlayableScenesAndUsesAttackerPosition(string sceneName, bool allowed)
        {
            var previous = SceneManager.GetActiveScene();
            // The Test Runner owns/restores the user scene; its untitled test scene cannot be extended additively.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneName; SceneManager.SetActiveScene(scene);
            try
            {
                var player = new GameObject("VFX attacker"); player.transform.position = new Vector3(4,0,3);
                player.transform.rotation=Quaternion.Euler(0,45,0);
                var camera = new GameObject("Remote lobby camera",typeof(Camera)); camera.tag="MainCamera";camera.transform.position=new Vector3(20,12,-30);
                camera.transform.rotation=Quaternion.Euler(20,-90,0);
                var fx = player.AddComponent<BlockAttackVfx>();
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Set(fx,"_cube",cube.GetComponent<MeshFilter>().sharedMesh);
                Set(fx,"_material",AssetDatabase.LoadAssetAtPath<Material>("Assets/Remodel/Materials/AttackGlow.mat"));
                fx.Play();
                float started = (float)Get(fx,"_start");
                Assert.That(started, allowed ? Is.EqualTo(Time.time) : Is.EqualTo(-10));
                if(allowed)
                {
                    Assert.That((Vector3)Get(fx,"_origin"),Is.EqualTo(player.transform.position+Vector3.up*1.2f));
                    var expectedRotation=sceneName=="Lobby"?player.transform.rotation:camera.transform.rotation;
                    Assert.That(Quaternion.Angle((Quaternion)Get(fx,"_rotation"),expectedRotation),Is.LessThan(.001f));
                }
            }
            finally
            {
                foreach(var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        [Test]
        public void LoginBannerCanRecoverFromHiddenStateAndDisplaysErrorsAsPlainText()
        {
            var root = new GameObject("Login feedback");
            try
            {
                var label = new GameObject("Message",typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(root.transform);
                var banner = root.AddComponent<LoginStatusBanner>();Set(banner,"_message",label.GetComponent<TMP_Text>());
                banner.Show("", "", LoginStatusTone.Notice);
                Assert.That(root.activeSelf,Is.False);
                banner.Show("신원 확인 중","계정 정보를 확인하고 있습니다.",LoginStatusTone.Pending);
                Assert.That(root.activeSelf,Is.True);
                banner.Show("접속 오류","<size=200>failed</size>",LoginStatusTone.Error);
                Assert.That(label.GetComponent<TMP_Text>().richText,Is.False);
                Assert.That(label.GetComponent<TMP_Text>().text,Is.EqualTo("<size=200>failed</size>"));
                banner.Show("접속 승인","인증 완료. 작전실로 이동합니다.",LoginStatusTone.Success);
                Assert.That(label.GetComponent<TMP_Text>().text,Does.Contain("작전실"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static object Get(object target,string field) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    }
}
