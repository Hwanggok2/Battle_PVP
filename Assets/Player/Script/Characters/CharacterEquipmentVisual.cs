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
            public bool Arrow, Held, Back, Sword;
            public Transform SourceHand, TargetHand;
        }
        private sealed class Hand
        {
            public Transform Bone, Middle, Index, Little;
            public Vector3 Palm;
            public Quaternion Frame;
            public float Size;
            public Hand(Animator animator, HumanBodyBones bone)
            {
                bool left = bone == HumanBodyBones.LeftHand;
                Bone = animator.GetBoneTransform(bone);
                Middle = CharacterEquipmentVisual.Middle(animator, bone);
                Index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) ?? FingerChild(Bone, "index");
                Little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal) ?? FingerChild(Bone, "pinky") ?? Middle;
            }
            public void Sample()
            {
                Size = Middle != null ? Vector3.Distance(Middle.position, Bone.position) : 0f;
                Frame = Middle == null || Index == null || Little == null || Size < .001f ? Bone.rotation :
                    Quaternion.LookRotation(Middle.position - Bone.position, Index.position - Little.position);
                Palm = Middle != null ? Vector3.Lerp(Bone.position, Middle.position, .65f) : Bone.position;
            }
        }
        private sealed class Torso
        {
            private readonly Transform _spine, _head, _left, _right;
            public Quaternion Frame;
            public Torso(Animator animator)
            {
                _spine = animator.GetBoneTransform(HumanBodyBones.Spine); _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); _right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
            public void Sample()
            {
                Vector3 up = _head.position - _spine.position;
                Frame = Quaternion.LookRotation(Vector3.Cross(_right.position - _left.position, up), up);
            }
        }
        private readonly Animator _driver;
        private readonly Hand _sourceLeft, _sourceRight, _targetLeft, _targetRight;
        private readonly Torso _sourceTorso, _targetTorso;
        private readonly float _bodyScale;
        private readonly Vector3 _swordGripOffset;
        private readonly List<Part> _parts = new();
        private readonly Dictionary<LODGroup, LOD[]> _originalLods = new();

        public CharacterEquipmentVisual(Animator driver, Animator visual, float bodyScale, Vector3 swordGripOffset)
        {
            _driver = driver;
            _bodyScale = bodyScale;
            _swordGripOffset = swordGripOffset;
            _sourceLeft = new Hand(driver, HumanBodyBones.LeftHand); _sourceRight = new Hand(driver, HumanBodyBones.RightHand);
            _targetLeft = new Hand(visual, HumanBodyBones.LeftHand); _targetRight = new Hand(visual, HumanBodyBones.RightHand);
            _sourceTorso = new Torso(driver); _targetTorso = new Torso(visual);
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
                    var hand = back ? TorsoBone(driver, weapon.parent) : bow ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                    var targetHand = visual.GetBoneTransform(hand);
                    if (back && targetHand == null) targetHand = visual.GetBoneTransform(HumanBodyBones.Chest) ?? visual.GetBoneTransform(HumanBodyBones.Spine);
                    _parts.Add(new Part { Weapon = weapon, Hand = hand, SourceHand = driver.GetBoneTransform(hand), TargetHand = targetHand,
                        Source = filter, Mesh = filter.sharedMesh, Owner = owner, Visual = renderer, Arrow = arrow, Held = sword || bow, Back = back, Sword = sword });
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
            bool handsSampled = false, torsoSampled = false;
            foreach (var part in _parts)
            {
                if (part.Source == null || part.Visual == null) continue;
                part.Visual.enabled = part.Owner.enabled;
                part.Visual.forceRenderingOff = part.Owner.forceRenderingOff;
                if (part.Visual.sharedMaterial != part.Owner.sharedMaterial) part.Visual.sharedMaterials = part.Owner.sharedMaterials;
                // Inactive weapons have no visible pose. LOD renderers still get fitted before culling.
                if (!part.Source.gameObject.activeInHierarchy || !part.Owner.enabled || part.Owner.forceRenderingOff) continue;
                var sourceHand = part.SourceHand;
                var targetHand = part.TargetHand;
                if (sourceHand == null || targetHand == null) continue;
                if (part.Back && !torsoSampled) { _sourceTorso.Sample(); _targetTorso.Sample(); torsoSampled = true; }
                if (!part.Back && !handsSampled)
                { _sourceLeft.Sample(); _sourceRight.Sample(); _targetLeft.Sample(); _targetRight.Sample(); handsSampled = true; }
                var source = part.Hand == HumanBodyBones.LeftHand ? _sourceLeft : _sourceRight;
                var target = part.Hand == HumanBodyBones.LeftHand ? _targetLeft : _targetRight;
                float sourceSize = source.Size, targetSize = target.Size;
                Quaternion sourceFrame = part.Back ? _sourceTorso.Frame : source.Frame;
                Quaternion targetFrame = part.Back ? _targetTorso.Frame : target.Frame;
                var rotation = targetFrame * Quaternion.Inverse(sourceFrame);
                float gripScale = part.Back ? _bodyScale : sourceSize > .001f && targetSize > .001f ? targetSize / sourceSize : 1f;
                Vector3 position = targetHand.position + rotation * (part.Source.transform.position - sourceHand.position) * gripScale;
                Quaternion orientation = rotation * part.Source.transform.rotation;
                if (part.Held)
                {
                    // Held weapons keep their original size. Scale changes belong to the palm,
                    // not to the authored point along the handle that the palm is gripping.
                    Vector3 sourceGrip = part.Source.transform.InverseTransformPoint(source.Palm);
                    position = target.Palm - orientation * Vector3.Scale(sourceGrip, part.Source.transform.lossyScale);
                    if (part.Sword) position += targetHand.TransformVector(_swordGripOffset);
                }
                if (part.Arrow)
                {
                    var sourceBow = _sourceLeft.Bone;
                    var targetBow = _targetLeft.Bone;
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
            }
        }

        private static HumanBodyBones TorsoBone(Animator animator, Transform parent)
        {
            foreach (var bone in new[] { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine })
                if (animator.GetBoneTransform(bone) == parent) return bone;
            return HumanBodyBones.Chest;
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
