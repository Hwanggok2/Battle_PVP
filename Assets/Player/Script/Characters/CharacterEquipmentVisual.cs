using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Characters
{
    /// <summary>Fits visible equipment to the native skeleton without moving gameplay colliders or projectile origins.</summary>
    internal sealed class CharacterEquipmentVisual : IDisposable
    {
        private sealed class Part
        {
            public Transform Weapon;
            public HumanBodyBones Hand;
            public MeshFilter Source;
            public Mesh Mesh;
            public MeshRenderer Owner, Visual;
            public bool Arrow, Held, Back;
        }
        private readonly Animator _driver, _visual;
        private readonly float _bodyScale;
        private readonly List<Part> _parts = new();
        private readonly Dictionary<LODGroup, LOD[]> _originalLods = new();

        public CharacterEquipmentVisual(Animator driver, Animator visual, float bodyScale)
        {
            _driver = driver; _visual = visual;
            _bodyScale = bodyScale;
            foreach (var weapon in driver.GetComponentsInChildren<Transform>(true))
            {
                if (weapon.IsChildOf(visual.transform)) continue;
                bool sword = weapon.name == "Sword", bow = weapon.name == "Bow_hand", arrow = weapon.name == "Arrow_hand";
                bool back = weapon.name == "Bow_01" || weapon.name == "Quiver_Arrows_01";
                if (!sword && !bow && !arrow && !back) continue;
                foreach (var filter in weapon.GetComponentsInChildren<MeshFilter>(true))
                {
                    var owner = filter.GetComponent<MeshRenderer>();
                    if (owner == null || filter.sharedMesh == null) continue;
                    var go = new GameObject("Native grip visual"); go.layer = filter.gameObject.layer;
                    go.transform.SetParent(filter.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = owner.sharedMaterials;
                    renderer.shadowCastingMode = owner.shadowCastingMode; renderer.receiveShadows = owner.receiveShadows;
                    renderer.lightProbeUsage = owner.lightProbeUsage; renderer.reflectionProbeUsage = owner.reflectionProbeUsage;
                    _parts.Add(new Part { Weapon = weapon, Hand = back ? TorsoBone(driver, weapon.parent) : bow ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                        Source = filter, Mesh = filter.sharedMesh, Owner = owner, Visual = renderer, Arrow = arrow, Held = sword || bow, Back = back });
                    filter.sharedMesh = null;
                }
            }
            // Keep each weapon's existing distance-based LOD selection on the fitted meshes.
            foreach (var group in driver.GetComponentsInChildren<LODGroup>(true))
            {
                var original = group.GetLODs();
                var replacement = new LOD[original.Length];
                bool changed = false;
                for (int i = 0; i < original.Length; i++)
                {
                    replacement[i] = original[i];
                    replacement[i].renderers = (Renderer[])original[i].renderers.Clone();
                    for (int j = 0; j < replacement[i].renderers.Length; j++)
                        foreach (var part in _parts)
                            if (replacement[i].renderers[j] == part.Owner)
                            { replacement[i].renderers[j] = part.Visual; changed = true; break; }
                }
                if (changed) { _originalLods.Add(group, original); group.SetLODs(replacement); }
            }
        }

        public void Sync()
        {
            foreach (var part in _parts)
            {
                if (part.Source == null || part.Visual == null) continue;
                var sourceHand = _driver.GetBoneTransform(part.Hand);
                var targetHand = _visual.GetBoneTransform(part.Hand);
                if (part.Back && targetHand == null)
                    targetHand = _visual.GetBoneTransform(HumanBodyBones.Chest) ?? _visual.GetBoneTransform(HumanBodyBones.Spine);
                if (sourceHand == null || targetHand == null) continue;
                float sourceSize = 1f, targetSize = 1f;
                Quaternion sourceFrame = part.Back ? TorsoFrame(_driver) : HandFrame(_driver, part.Hand, out sourceSize);
                Quaternion targetFrame = part.Back ? TorsoFrame(_visual) : HandFrame(_visual, part.Hand, out targetSize);
                var rotation = targetFrame * Quaternion.Inverse(sourceFrame);
                float gripScale = part.Back ? _bodyScale : sourceSize > .001f && targetSize > .001f ? targetSize / sourceSize : 1f;
                Vector3 position = targetHand.position + rotation * (part.Source.transform.position - sourceHand.position) * gripScale;
                Quaternion orientation = rotation * part.Source.transform.rotation;
                if (part.Held)
                {
                    // Held weapons keep their original size. Scale changes belong to the palm,
                    // not to the authored point along the handle that the palm is gripping.
                    Vector3 sourceGrip = part.Source.transform.InverseTransformPoint(PalmCenter(_driver, part.Hand));
                    position = PalmCenter(_visual, part.Hand) - orientation * Vector3.Scale(sourceGrip, part.Source.transform.lossyScale);
                }
                if (part.Arrow)
                {
                    var sourceBow = _driver.GetBoneTransform(HumanBodyBones.LeftHand);
                    var targetBow = _visual.GetBoneTransform(HumanBodyBones.LeftHand);
                    var sourceDirection = (sourceBow.position - sourceHand.position).normalized;
                    var direction = (targetBow.position - targetHand.position).normalized;
                    if (sourceDirection.sqrMagnitude > .5f && direction.sqrMagnitude > .5f)
                    {
                        // BowAttackController keeps the nock at the gameplay string hand.
                        // Preserve the arrow length while moving that nock to the visible hand.
                        float tail = Vector3.Dot(part.Weapon.position - sourceHand.position, sourceDirection);
                        var arrowRotation = Quaternion.LookRotation(-direction, _driver.transform.up);
                        position = targetHand.position + direction * tail + arrowRotation *
                            (Quaternion.Inverse(part.Weapon.rotation) * (part.Source.transform.position - part.Weapon.position));
                        orientation = arrowRotation * Quaternion.Inverse(part.Weapon.rotation) * part.Source.transform.rotation;
                    }
                }
                part.Visual.transform.SetPositionAndRotation(position, orientation);
                part.Visual.transform.localScale = Vector3.one * (part.Back ? gripScale : 1f);
                part.Visual.enabled = part.Owner.enabled;
                part.Visual.forceRenderingOff = part.Owner.forceRenderingOff;
                if (part.Visual.sharedMaterial != part.Owner.sharedMaterial) part.Visual.sharedMaterials = part.Owner.sharedMaterials;
            }
        }

        private static HumanBodyBones TorsoBone(Animator animator, Transform parent)
        {
            foreach (var bone in new[] { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine })
                if (animator.GetBoneTransform(bone) == parent) return bone;
            return HumanBodyBones.Chest;
        }

        private static Quaternion TorsoFrame(Animator animator)
        {
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            // Some imported avatars place their mapped hips at ground level.
            // The upper spine gives a stable anatomical frame on those rigs too.
            Vector3 up = head.position - spine.position;
            Vector3 right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position -
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            return Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }

        internal static Quaternion HandFrame(Animator animator, HumanBodyBones handBone, out float size)
        {
            bool left = handBone == HumanBodyBones.LeftHand;
            var hand = animator.GetBoneTransform(handBone);
            var middle = Middle(animator, handBone);
            var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) ?? FingerChild(hand, "index");
            var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal) ?? FingerChild(hand, "pinky") ?? middle;
            size = middle != null ? Vector3.Distance(middle.position, hand.position) : 0f;
            if (middle == null || index == null || little == null || size < .001f) return hand.rotation;
            return Quaternion.LookRotation(middle.position - hand.position, index.position - little.position);
        }

        internal static Vector3 PalmCenter(Animator animator, HumanBodyBones handBone)
        {
            var hand = animator.GetBoneTransform(handBone);
            var middle = Middle(animator, handBone);
            return middle != null ? Vector3.Lerp(hand.position, middle.position, .65f) : hand.position;
        }

        private static Transform Middle(Animator animator, HumanBodyBones handBone)
        {
            var mapped = animator.GetBoneTransform(handBone == HumanBodyBones.LeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (mapped != null) return mapped;
            // Some authored rigs group three fingers under one bone outside the Humanoid mapping.
            var hand = animator.GetBoneTransform(handBone);
            return FingerChild(hand, "middle") ?? FingerChild(hand, "fingers1");
        }

        private static Transform FingerChild(Transform hand, string name)
        {
            foreach (Transform child in hand)
                if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return child;
            return null;
        }

        public void Dispose()
        {
            foreach (var entry in _originalLods) if (entry.Key != null) entry.Key.SetLODs(entry.Value);
            _originalLods.Clear();
            foreach (var part in _parts)
            {
                if (part.Source != null) part.Source.sharedMesh = part.Mesh;
                if (part.Visual == null) continue;
                part.Visual.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(part.Visual.gameObject);
                else UnityEngine.Object.DestroyImmediate(part.Visual.gameObject);
            }
            _parts.Clear();
        }
    }
}
