using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterBowAlignmentTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase("brute")]
        [TestCase("megumi")]
        [TestCase("security-officer")]
        [TestCase("casual-1")]
        [TestCase("picochan")]
        public void NativeBowAimAndBackEquipmentFollowTheEvaluatedGameplaySkeleton(string id)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            try
            {
                EditorTestLifecycle.BindNetwork(player);
                player.transform.SetPositionAndRotation(new Vector3(31, 2, -24), Quaternion.Euler(0, 115, 0));
                var driver = player.GetComponent<Animator>();
                driver.Rebind(); driver.Update(0);
                driver.SetLayerWeight(1, 1);
                driver.Play("Bow_AimHold", 1, .5f); driver.Update(0);
                var rig = player.GetComponent<BowAimRigTarget>();
                EditorTestLifecycle.Invoke(rig, "Awake");
                typeof(BowAimRigTarget).GetField("_weight", Private).SetValue(rig, 1f);
                rig.SetYawOffsetActive(true);
                var quiver = player.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Quiver_Arrows_01");
                var quiverParent = quiver.parent;
                var quiverPosition = quiver.localPosition;
                var sourceMesh = quiver.GetComponentsInChildren<MeshFilter>(true)[0].sharedMesh;
                using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
                var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>("Assets/Resources/CharacterCatalog.asset");
                Assert.That(skin.Apply(catalog.Find(id), out var error), Is.True, error);
                var visual = player.GetComponentInChildren<CharacterPoseFollower>().GetComponentInChildren<Animator>();
                foreach (var group in quiver.GetComponentsInChildren<LODGroup>(true))
                    foreach (var lod in group.GetLODs())
                        foreach (var renderer in lod.renderers)
                            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null,
                                "LOD selection must render the fitted equipment instead of its empty original renderer.");
                foreach (float pitch in new[] { -30f, 0f, 30f })
                {
                    EditorTestLifecycle.Invoke(rig, "Update");
                    driver.Update(0);
                    rig.SetNetworkAimDirection(player.transform.TransformDirection(Quaternion.Euler(pitch, 0, 0) * Vector3.forward));
                    EditorTestLifecycle.Invoke(rig, "LateUpdate");
                    skin.SyncPose();
                    Vector3 sourceAim = HandDirection(driver), visibleAim = HandDirection(visual);
                    Assert.That(Vector3.Angle(sourceAim, visibleAim), Is.LessThan(.1f), id + " at pitch " + pitch);
                    var nativeQuiver = quiver.GetComponentsInChildren<MeshFilter>(true).Single(t => t.sharedMesh == sourceMesh);
                    Vector3 feet = (visual.GetBoneTransform(HumanBodyBones.LeftFoot).position + visual.GetBoneTransform(HumanBodyBones.RightFoot).position) * .5f;
                    float bodyHeight = Vector3.Distance(visual.GetBoneTransform(HumanBodyBones.Head).position, feet);
                    Assert.That(nativeQuiver.GetComponent<Renderer>().bounds.size.magnitude, Is.LessThan(bodyHeight * .9f),
                        "The quiver must not become larger than its wearer's body, even when the avatar maps hips to its root.");
                    Assert.That(nativeQuiver.transform, Is.Not.SameAs(quiver.GetComponentsInChildren<MeshFilter>(true)[0].transform),
                        "The back quiver must be fitted to the visible torso rather than left on the gameplay rig.");
                    float nativeTorso = TorsoLength(visual);
                    var center = nativeQuiver.transform.TransformPoint(sourceMesh.bounds.center);
                    Assert.That(Vector3.Distance(center, visual.GetBoneTransform(HumanBodyBones.Chest).position),
                        Is.LessThan(nativeTorso), "The quiver must stay within the native torso's reach.");
                }
                Assert.That(quiver.parent, Is.SameAs(quiverParent));
                Assert.That(quiver.localPosition, Is.EqualTo(quiverPosition));
                skin.Restore();
                Assert.That(quiver.GetComponentsInChildren<MeshFilter>(true)[0].sharedMesh, Is.SameAs(sourceMesh));
            }
            finally { Object.DestroyImmediate(player); }
        }

        private static Vector3 HandDirection(Animator animator) =>
            animator.GetBoneTransform(HumanBodyBones.LeftHand).position - animator.GetBoneTransform(HumanBodyBones.RightHand).position;
        private static float TorsoLength(Animator animator) => Vector3.Distance(
            animator.GetBoneTransform(HumanBodyBones.Head).position, animator.GetBoneTransform(HumanBodyBones.Hips).position);
    }
}
