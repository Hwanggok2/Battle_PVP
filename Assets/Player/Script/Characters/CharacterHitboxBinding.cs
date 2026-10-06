using System;
using System.Collections.Generic;
using BattlePvp.Combat;
using UnityEngine;

namespace BattlePvp.Characters
{
    // Reuse the authored damage colliders and body-part IDs on the selected skeleton.
    // Keep them under the gameplay root so health ownership and network lookup stay unchanged.
    internal sealed class CharacterHitboxBinding : IDisposable
    {
        private sealed class Binding
        {
            public CapsuleCollider Collider;
            public Transform Bone;
            public Vector3 Center, Axis;
            public float Radius, Height;
            public Vector3 OriginalPosition, OriginalScale, OriginalCenter;
            public Quaternion OriginalRotation;
            public float OriginalRadius, OriginalHeight;
            public int OriginalDirection;
        }

        private readonly List<Binding> _bindings = new();
        private readonly ServerPoseHistory _history;
        public bool HasBindings => _bindings.Count != 0;

        public CharacterHitboxBinding(Animator driver, SkinnedMeshRenderer original, Mesh originalMesh,
            Animator native, SkinnedMeshRenderer nativeBody, float bodyScale)
        {
            _history = driver.GetComponent<ServerPoseHistory>();
            foreach (var part in driver.GetComponentsInChildren<HitBodyPart>(true))
            {
                var capsule = part.GetComponent<CapsuleCollider>();
                if (capsule == null || part.transform.IsChildOf(native.transform)) continue;
                var parentBone = FindBone(driver, part.transform.parent);
                if (parentBone == HumanBodyBones.LastBone) continue;
                var anchor = part.Part == BodyPart.Head ? HumanBodyBones.Head :
                    part.Part == BodyPart.Body ? HumanBodyBones.Spine : parentBone;
                var target = native.GetBoneTransform(anchor);
                if (target == null || !TryBindPose(driver, original, originalMesh, parentBone, out var parentBind) ||
                    !TryBindPose(driver, original, originalMesh, anchor, out var sourceBind) ||
                    !TryBindPose(native, nativeBody, nativeBody.sharedMesh, anchor, out var targetBind)) continue;

                // All bind matrices use the common player coordinate system, independent of the current pose.
                var root = driver.transform;
                parentBind = root.worldToLocalMatrix * parentBind;
                sourceBind = root.worldToLocalMatrix * sourceBind;
                targetBind = root.worldToLocalMatrix * targetBind;
                var shapeBind = parentBind * Matrix4x4.TRS(part.transform.localPosition, part.transform.localRotation, part.transform.localScale);
                var sourceCenter = shapeBind.MultiplyPoint3x4(capsule.center);
                var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
                var bindAxis = shapeBind.MultiplyVector(axis).normalized;
                var targetCenter = (Vector3)targetBind.GetColumn(3) + (sourceCenter - (Vector3)sourceBind.GetColumn(3)) * bodyScale;
                _bindings.Add(new Binding
                {
                    Collider = capsule, Bone = target,
                    Center = targetBind.inverse.MultiplyPoint3x4(targetCenter),
                    Axis = targetBind.inverse.MultiplyVector(bindAxis),
                    Radius = capsule.radius * bodyScale, Height = capsule.height * bodyScale,
                    OriginalPosition = part.transform.localPosition, OriginalRotation = part.transform.localRotation,
                    OriginalScale = part.transform.localScale, OriginalCenter = capsule.center,
                    OriginalRadius = capsule.radius, OriginalHeight = capsule.height, OriginalDirection = capsule.direction
                });
            }
            foreach (var binding in _bindings)
            {
                binding.Collider.center = Vector3.zero; binding.Collider.direction = 1;
                binding.Collider.radius = binding.Radius; binding.Collider.height = binding.Height;
            }
            Sync();
            if (HasBindings && _history != null) _history.RefreshBodyParts();
        }

        public void Sync()
        {
            foreach (var binding in _bindings)
            {
                if (binding.Collider == null || binding.Bone == null) continue;
                var capsule = binding.Collider;
                capsule.transform.SetPositionAndRotation(binding.Bone.TransformPoint(binding.Center),
                    Quaternion.FromToRotation(Vector3.up, binding.Bone.TransformVector(binding.Axis).normalized));
            }
        }

        private static HumanBodyBones FindBone(Animator animator, Transform transform)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                if (animator.GetBoneTransform((HumanBodyBones)i) == transform) return (HumanBodyBones)i;
            return HumanBodyBones.LastBone;
        }

        private static bool TryBindPose(Animator animator, SkinnedMeshRenderer body, Mesh mesh,
            HumanBodyBones bone, out Matrix4x4 pose)
        {
            int index = Array.IndexOf(body.bones, animator.GetBoneTransform(bone));
            if (index < 0 || index >= mesh.bindposes.Length) { pose = default; return false; }
            pose = body.transform.localToWorldMatrix * mesh.bindposes[index].inverse;
            return true;
        }

        public void Dispose()
        {
            foreach (var binding in _bindings)
            {
                if (binding.Collider == null) continue;
                var capsule = binding.Collider;
                capsule.transform.localPosition = binding.OriginalPosition;
                capsule.transform.localRotation = binding.OriginalRotation;
                capsule.transform.localScale = binding.OriginalScale;
                capsule.center = binding.OriginalCenter; capsule.direction = binding.OriginalDirection;
                capsule.radius = binding.OriginalRadius; capsule.height = binding.OriginalHeight;
            }
            if (HasBindings && _history != null) _history.RefreshBodyParts();
            _bindings.Clear();
        }
    }
}
