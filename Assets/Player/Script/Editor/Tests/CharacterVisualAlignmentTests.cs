using System.Linq;
using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterVisualAlignmentTests
    {
        private static CharacterCatalog Catalog => AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void ControllerDrivenPreviewPoseRemainsAtItsDistantStage(string id)
        {
            var rig = Object.Instantiate(Catalog.PreviewRig);
            var mesh = new Mesh();
            try
            {
                rig.transform.SetPositionAndRotation(new Vector3(10000, -10000, 10000), Quaternion.Euler(0, 70, 0));
                var animator = rig.GetComponent<Animator>();
                animator.runtimeAnimatorController = Catalog.PreviewController;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Update(0f);
                using var skin = new CharacterSkin(rig.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                skin.SyncPose(); skin.VisibleBody.BakeMesh(mesh); mesh.RecalculateBounds();
                Assert.That(mesh.bounds.center.magnitude, Is.LessThan(2f), "Root-relative poses must not subtract the stage translation twice.");
                Assert.That(mesh.bounds.size.magnitude, Is.InRange(1f, 4f));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(rig); }
        }

        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void NativeSwordGripsVisibleHandWithoutMovingAttackColliderAndCameraTracksSize(string id)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var camera = new GameObject("Size camera").AddComponent<FollowCamera>();
            try
            {
                var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0f);
                var sword = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "Sword");
                var position = sword.position; var rotation = sword.rotation;
                var collider = sword.GetComponent<Collider>(); var parent = sword.parent;
                var filter = sword.GetComponent<MeshFilter>(); var originalMesh = filter.sharedMesh;
                var sourcePalm = Palm(animator);
                Vector3 authoredGrip = sword.InverseTransformPoint(sourcePalm);
                camera.SetTarget(player.transform);
                var originalCamera = camera.GetAimRay().origin - player.transform.position;
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                skin.SyncPose();
                var follower = player.GetComponentInChildren<CharacterPoseFollower>();
                var targetHand = follower.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
                var visual = sword.GetComponentsInChildren<MeshFilter>(true).Single(x => x.sharedMesh == originalMesh);
                Assert.That(Vector3.Distance(visual.transform.position, targetHand.position), Is.LessThan(.22f), "The grip must stay inside the visible palm's reach.");
                var nativeAnimator = follower.GetComponentInChildren<Animator>();
                Assert.That(Vector3.Distance(visual.transform.TransformPoint(authoredGrip),
                    Palm(nativeAnimator) + targetHand.TransformVector(Catalog.Find(id).SwordGripOffset)), Is.LessThan(.001f),
                    "The original point along the handle must remain in the native palm, including larger and unmapped hands.");
                Assert.That(sword.parent, Is.SameAs(parent)); Assert.That(sword.GetComponent<Collider>(), Is.SameAs(collider));
                Assert.That(Vector3.Distance(position, sword.position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(rotation, sword.rotation), Is.LessThan(.001f));
                var cameraOffset = camera.GetAimRay().origin - player.transform.position;
                Assert.That(Vector3.Distance(cameraOffset, originalCamera * follower.ViewScale), Is.LessThan(.001f));
                skin.Restore();
                Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));
                Assert.That(sword.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(1));
                Assert.That(CharacterPoseFollower.GetViewScale(player.transform), Is.EqualTo(1f));
            }
            finally { Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(player); }
        }

        [TestCase("security-officer")] [TestCase("megumi")] [TestCase("casual-1")]
        public void RequestedCharactersStandFivePercentShorterThanDefault(string id)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var graph = PlayableGraph.Create("Standing character height");
            var mesh = new Mesh();
            try
            {
                var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim");
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                playable.SetTime(.2); graph.Play(); graph.Evaluate(0);
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                float StandingHeight()
                {
                    var body = skin.VisibleBody;
                    body.BakeMesh(mesh, true);
                    var vertices = mesh.vertices.Select(v => player.transform.InverseTransformPoint(body.transform.TransformPoint(v)).y);
                    return vertices.Max() - vertices.Min();
                }
                float originalHeight = StandingHeight();
                Assert.That(skin.Apply(Catalog.Find(id), out var error), Is.True, error);
                skin.SyncPose();
                Assert.That(StandingHeight() / originalHeight, Is.EqualTo(.95f).Within(.015f),
                    "Measure the rendered standing silhouette, including hair and shoes.");
                player.transform.localScale = Vector3.one * 1.2f;
                graph.Evaluate(0); skin.SyncPose();
                Assert.That(StandingHeight() / originalHeight, Is.EqualTo(.95f).Within(.015f),
                    "The same relative height must survive a larger preset.");
                skin.Restore();
                Assert.That(StandingHeight(), Is.EqualTo(originalHeight).Within(.002f));
            }
            finally { graph.Destroy(); Object.DestroyImmediate(mesh); Object.DestroyImmediate(player); }
        }

        private static Vector3 Palm(Animator animator)
        {
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal) ?? hand.GetComponentsInChildren<Transform>()
                .FirstOrDefault(t => t.name == "Fingers1R");
            return middle != null ? Vector3.Lerp(hand.position, middle.position, .65f) : hand.position;
        }

        [TestCase("Attack", 1f)]
        [TestCase("Attack2", 1f)]
        [TestCase("Attack3", 1f)]
        [TestCase("Attack", 1.2f)]
        [TestCase("Attack2", 1.2f)]
        [TestCase("Attack3", 1.2f)]
        public void BarbarianSwordHandleSitsInsideTheCurledFingers(string motion, float scale)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var graph = PlayableGraph.Create("Barbarian sword grip");
            try
            {
                player.transform.SetPositionAndRotation(new Vector3(7, 3, -5), Quaternion.Euler(0, 70, 0));
                player.transform.localScale = Vector3.one * scale;
                var driver = player.GetComponent<Animator>(); driver.Rebind(); driver.Update(0);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Attack/" + motion + ".anim");
                Assert.That(clip, Is.Not.Null);
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Pose", driver).SetSourcePlayable(playable);
                playable.SetTime(.2); graph.Play(); graph.Evaluate(0);
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find("brute"), out var error), Is.True, error);
                var sword = player.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Sword");
                sword.gameObject.SetActive(true); skin.SyncPose();
                var fitted = sword.GetComponentsInChildren<MeshFilter>().Single(f => f.sharedMesh != null);
                var native = skin.VisibleBody.GetComponentInParent<Animator>();
                var knuckle = native.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                var finger = native.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                // This sword's handle runs down local Z with its grip centered 6.15 cm behind the guard.
                var handle = fitted.transform.TransformPoint(new Vector3(0, 0, -.06154f));
                Assert.That(Vector3.Distance(handle, (knuckle.position + finger.position) * .5f),
                    Is.LessThan(.025f * scale), "The handle must sit in the fist, not above the knuckles.");
            }
            finally { graph.Destroy(); Object.DestroyImmediate(player); }
        }

        [TestCase(360, 365)]
        [TestCase(400, 345)]
        public void PreviewRendersEverySelectionAndReleasesSnapshotsWhenClosed(float width, float height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Preview test", typeof(RectTransform), typeof(RawImage));
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            var preview = go.AddComponent<CharacterPreview>();
            Texture2D readback = null;
            try
            {
                preview.Initialize(go.GetComponent<RawImage>());
                var camera = (Camera)typeof(CharacterPreview).GetField("_camera", Private).GetValue(preview);
                var rig = (GameObject)typeof(CharacterPreview).GetField("_rig", Private).GetValue(preview);
                var texture = camera.targetTexture;
                readback = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                Assert.That(camera.aspect, Is.EqualTo(width / height).Within(.0001f));
                Assert.That(texture.width / (float)texture.height, Is.EqualTo(width / height).Within(.002f));
                foreach (var definition in Catalog.Characters.Concat(Catalog.Characters.Reverse()))
                {
                    Assert.That(preview.Show(definition, out var error), Is.True, error);
                    camera.Render();
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = texture; readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readback.Apply(); }
                    finally { RenderTexture.active = previous; }
                    var pixels = readback.GetPixels32(); var background = pixels[0];
                    int visiblePixels = pixels.Count(p => Mathf.Abs(p.r - background.r) + Mathf.Abs(p.g - background.g) + Mathf.Abs(p.b - background.b) > 20);
                    Assert.That(visiblePixels, Is.GreaterThan(1000), definition.Id + " must draw an actual model, not only the clear color.");
                    Assert.That(rig.GetComponentsInChildren<MeshFilter>(true).Count(x => x.name == "Preview pose"), Is.EqualTo(1));
                    Assert.That(rig.GetComponent<Animator>().enabled, Is.False, "The static selector should not keep evaluating gameplay animation.");
                    preview.Rotate(30);
                }
                EditorTestLifecycle.Invoke(preview, "OnDisable");
                Assert.That(camera.gameObject.activeInHierarchy, Is.False);
            }
            finally
            {
                EditorTestLifecycle.Invoke(preview, "OnDestroy");
                Object.DestroyImmediate(go); Object.DestroyImmediate(readback);
            }
        }

        [Test] public void ChoosingWhileCrouchedDoesNotChangeCharacterCameraScale()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            try
            {
                var animator = player.GetComponent<Animator>(); animator.Rebind(); animator.Update(0f);
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.That(skin.Apply(Catalog.Find("brute"), out var error), Is.True, error);
                float standing = CharacterPoseFollower.GetViewScale(player.transform);
                skin.Restore();
                animator.GetBoneTransform(HumanBodyBones.Hips).position -= Vector3.up * .45f;
                Assert.That(skin.Apply(Catalog.Find("brute"), out error), Is.True, error);
                Assert.That(CharacterPoseFollower.GetViewScale(player.transform), Is.EqualTo(standing).Within(.001f));
            }
            finally { Object.DestroyImmediate(player); }
        }
    }
}
