using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace BattlePvp.Characters
{
    /// <summary>Retargets the evaluated gameplay pose without changing either skeleton's proportions.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class CharacterPoseFollower : MonoBehaviour
    {
        private HumanPoseHandler _source, _target;
        private HumanPose _pose;
        private SkinnedMeshRenderer _original;
        private Renderer[] _renderers;
        private Animator _driver;
        private BowAimRigTarget _bowAim;
        private CharacterEquipmentVisual _equipment;
        private Transform _driverRoot;
        private Transform _sourceLeft, _sourceRight, _targetLeft, _targetRight, _targetSpine;
        private Mirror.NetworkIdentity _identity;
        private static readonly ProfilerMarker PoseMarker = new("BattlePvp.CharacterPose");
        private static readonly Dictionary<Transform, CharacterPoseFollower> Active = new();
        public float ViewScale { get; private set; } = 1f;
        public static float GetViewScale(Transform player) => player != null && Active.TryGetValue(player, out var visual) && visual != null
            ? visual.ViewScale : 1f;

        public void Initialize(Animator driver, SkinnedMeshRenderer original, Mesh originalMesh)
        {
            var animator = GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
            _driver = driver;
            _bowAim = driver.GetComponent<BowAimRigTarget>();
            _driverRoot = driver.transform;
            _identity = driver.GetComponent<Mirror.NetworkIdentity>();
            _sourceLeft = driver.GetBoneTransform(HumanBodyBones.LeftHand); _sourceRight = driver.GetBoneTransform(HumanBodyBones.RightHand);
            _targetLeft = animator.GetBoneTransform(HumanBodyBones.LeftHand); _targetRight = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _targetSpine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _original = original;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _source = new HumanPoseHandler(driver.avatar, driver.transform);
            _target = new HumanPoseHandler(animator.avatar, animator.transform);
            // Bind-pose heights stay stable if the selection changes while crouching or attacking.
            float sourceHeight = HeadHeight(driver, original, originalMesh, driver.transform);
            var nativeBody = GetComponentInChildren<SkinnedMeshRenderer>();
            float targetHeight = HeadHeight(animator, nativeBody, nativeBody.sharedMesh, driver.transform);
            if (sourceHeight > .1f) ViewScale = Mathf.Clamp(targetHeight / sourceHeight, .5f, 2f);
            Active[driver.transform] = this;
            SyncPose();
            _equipment = new CharacterEquipmentVisual(driver, animator, ViewScale);
            _equipment.Sync();
        }

        public void SyncPose()
        {
            if (_source == null || _original == null) return;
            using var sample = PoseMarker.Auto();
            _source.GetHumanPose(ref _pose);
            // Use evaluated bones for rotation: Animator.bodyRotation still contains the
            // animation pose before BowAimRigTarget turns the hips toward the crosshair.
            var inverseRotation = Quaternion.Inverse(_driver.transform.rotation);
            _pose.bodyPosition = _driver.transform.InverseTransformPoint(_driver.bodyPosition) / _driver.humanScale;
            _pose.bodyRotation = inverseRotation * _pose.bodyRotation;
            _target.SetHumanPose(ref _pose);
            if (_bowAim != null && _bowAim.IsPosing)
            {
                // Different arm proportions slightly change the hand-to-hand aim after
                // Humanoid retargeting. Correct the visible upper body without stretching it.
                Vector3 sourceAim = _sourceLeft.position - _sourceRight.position, visibleAim = _targetLeft.position - _targetRight.position;
                if (_targetSpine != null && sourceAim.sqrMagnitude > .001f && visibleAim.sqrMagnitude > .001f)
                    _targetSpine.rotation = Quaternion.FromToRotation(visibleAim, sourceAim) * _targetSpine.rotation;
            }
            _equipment?.Sync();
            short meshLod = (short)(_identity != null && _identity.isLocalPlayer ? 0 : -1);
            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.enabled = _original.enabled;
                renderer.forceRenderingOff = _original.forceRenderingOff;
                if (renderer.forceMeshLod != meshLod) renderer.forceMeshLod = meshLod;
            }
        }
        private void LateUpdate()
        {
            // Native low-latency sessions simulate at 120 Hz but render at 30/60 Hz.
            // Only the visual copy is skipped; the authoritative rig keeps its normal cadence.
            if (OnDemandRendering.willCurrentFrameRender) SyncPose();
        }
        private static float HeadHeight(Animator animator, SkinnedMeshRenderer body, Mesh mesh, Transform root)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return 0f;
            int index = System.Array.IndexOf(body.bones, head);
            if (index < 0 || index >= mesh.bindposes.Length) return root.InverseTransformPoint(head.position).y;
            Vector3 bindPosition = mesh.bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero);
            return root.InverseTransformPoint(body.transform.TransformPoint(bindPosition)).y;
        }
        public void Release()
        {
            if (!ReferenceEquals(_driverRoot, null) && Active.TryGetValue(_driverRoot, out var current) && current == this)
                Active.Remove(_driverRoot);
            _equipment?.Dispose(); _equipment = null;
            _source?.Dispose(); _source = null;
            _target?.Dispose(); _target = null;
        }
        private void OnDestroy() => Release();
    }
}
