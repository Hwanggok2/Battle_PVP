using System;
using System.Reflection;
using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    public static class MeleeMotionBaker
    {
        [MenuItem("Battle PvP/Combat/Bake Melee Motion")]
        public static void Bake()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before baking.");
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                var animator = root.GetComponent<Animator>();
                animator.fireEvents = false; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Update(0);
                var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                var sword = root.GetComponentInChildren<MeleeHitBox>(true).transform;
                var combat = root.GetComponent<PlayerCombat>();
                var attacks = (AttackData[])typeof(PlayerCombat).GetField("comboList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
                foreach (var attack in attacks)
                {
                    animator.Rebind(); animator.speed = 1; animator.Play("Movement", 0, 0);
                    var track = new MeleeMotionSample[241];
                    for (int i = 0; i < track.Length; i++)
                    {
                        animator.Play(attack.animationName, 1, i / (float)(track.Length - 1));
                        animator.Update(0);
                        track[i] = new MeleeMotionSample {
                            position = root.transform.InverseTransformPoint(sword.position),
                            rotation = Quaternion.Inverse(root.transform.rotation) * sword.rotation,
                            pivot = root.transform.InverseTransformPoint(spine.position),
                            referenceBase = root.transform.InverseTransformVector(spine.parent.TransformVector(attack.aimBladeBase)),
                            referenceTip = root.transform.InverseTransformVector(spine.parent.TransformVector(attack.aimBladeTip))
                        };
                    }
                    attack.motionSamples = track; EditorUtility.SetDirty(attack);
                }
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
