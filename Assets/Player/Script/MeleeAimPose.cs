using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Mesh-space additive FK after animation, before melee queries. The authored swing is retained.</summary>
    public sealed class MeleeAimPose
    {
        private readonly Transform _root;
        public Avatar Avatar { get; }
        private readonly Transform[] _bones;
        private readonly Quaternion[] _baseRotations;
        private bool _applied;
        public Quaternion CorrectionRotation { get; private set; } = Quaternion.identity;

        public MeleeAimPose(Transform root, Animator animator)
        {
            _root = root;
            Avatar = animator != null ? animator.avatar : null;
            _bones = animator != null && animator.isHuman ? new[] {
                animator.GetBoneTransform(HumanBodyBones.Spine),
                animator.GetBoneTransform(HumanBodyBones.Chest),
                animator.GetBoneTransform(HumanBodyBones.UpperChest) } : new Transform[0];
            _baseRotations = new Quaternion[_bones.Length];
        }

        public Vector3 Pivot => _bones.Length > 0 && _bones[0] != null ? _bones[0].position :
            _root.position + _root.up * 1.2f;
        public Vector3 ReferenceVector(Vector3 reference, bool rootSpace = false) => !rootSpace && _bones.Length > 0 && _bones[0] != null ?
            _bones[0].parent.TransformVector(reference) : _root.TransformVector(reference);

        public Vector3 SelectReference(AttackData attack, float reach)
        {
            if (attack == null) return Vector3.forward;
            var visual = attack.FindVisualAim(Avatar);
            Vector3 bladeBase = visual != null ? visual.bladeBase : attack.aimBladeBase;
            Vector3 bladeTip = visual != null ? visual.bladeTip : attack.aimBladeTip;
            if (bladeTip.sqrMagnitude < .001f || reach <= 0) return ReferencePoint(attack);
            // The calibrated blade extends away from the pivot. Clamp to its physical ends.
            float low = 0, high = 1;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * .5f;
                if (ReferenceVector(Vector3.Lerp(bladeBase, bladeTip, middle), attack.aimInRootSpace).magnitude < reach) low = middle;
                else high = middle;
            }
            return Vector3.Lerp(bladeBase, bladeTip, (low + high) * .5f);
        }

        public Vector3 ReferencePoint(AttackData attack)
        {
            var visual = attack.FindVisualAim(Avatar);
            return visual != null ? Vector3.Lerp(visual.bladeBase, visual.bladeTip, .7f) : attack.aimBladePoint;
        }

        public void Restore()
        {
            if (!_applied) return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = _baseRotations[i];
            _applied = false;
            CorrectionRotation = Quaternion.identity;
        }

        public void Apply(Vector3 direction)
        {
            if (!CombatValidation.IsFinite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .001f) return;
            int count = 0;
            for (int i = 0; i < _bones.Length; i++) if (_bones[i] != null) count++;
            if (count == 0) return;
            // Animator has evaluated since Restore. Save its pose, not last frame's corrected rotations.
            Quaternion delta = Quaternion.FromToRotation(_root.forward, direction.normalized);
            CorrectionRotation = delta;
            Quaternion part = Quaternion.Slerp(Quaternion.identity, delta, 1f / count);
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _baseRotations[i] = _bones[i].localRotation;
                _bones[i].rotation = part * _bones[i].rotation;
            }
            _applied = true;
        }

        // Map the calibrated blade point onto the crosshair without replacing the authored swing.
        public void ApplyCalibrated(Vector3 direction, Vector3 reference, float phase, float weight, Vector3 lookDirection = default, float maxCalibration = 180f,
            bool rootSpace = false, float crossingPhase = .6f)
        {
            if (_bones.Length == 0 || _bones[0] == null || !CombatValidation.IsFinite(direction) ||
                direction.sqrMagnitude < .001f || float.IsInfinity(direction.sqrMagnitude)) return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _baseRotations[i] = _bones[i].localRotation;
            CorrectionRotation = Correction(_root.forward, ReferenceVector(reference, rootSpace), direction, lookDirection, phase, weight, maxCalibration, crossingPhase);
            _bones[0].rotation = CorrectionRotation * _bones[0].rotation;
            _applied = true;
        }

        internal static Quaternion Correction(Vector3 forward, Vector3 reference, Vector3 direction, Vector3 lookDirection, float phase, float weight, float maxCalibration, float crossingPhase = .6f)
        {
            // Late overhead strikes must retain their calibration through contact.
            float recovery = Mathf.Clamp(1f - crossingPhase, .025f, .16f);
            float calibrationWeight = Mathf.SmoothStep(0, 1, phase / Mathf.Clamp(crossingPhase, .025f, .35f)) *
                Mathf.SmoothStep(0, 1, (1f - phase) / recovery);
            Vector3 source = Vector3.Slerp(forward, reference.normalized, calibrationWeight);
            Quaternion delta = Quaternion.FromToRotation(source, direction.normalized);
            Quaternion look = lookDirection.sqrMagnitude > .001f ? Quaternion.FromToRotation(forward, lookDirection.normalized) : Quaternion.identity;
            // A two-handed swing can pass behind the body. Do not twist the waist
            // 180 degrees to drag that wind-up/recovery onto the crosshair.
            delta = Quaternion.RotateTowards(look, delta, maxCalibration);
            // Blend two absolute corrections, so looking up is not added twice during an attack.
            return Quaternion.Slerp(look, delta, Mathf.Clamp01(weight));
        }

        public static Vector3 ReachablePoint(Ray ray, Vector3 pivot, float reach)
        {
            Vector3 center = pivot - ray.origin;
            float along = Vector3.Dot(center, ray.direction);
            float perpendicularSquared = Mathf.Max(0, center.sqrMagnitude - along * along);
            float distance = along + Mathf.Sqrt(Mathf.Max(0, reach * reach - perpendicularSquared));
            return ray.GetPoint(Mathf.Max(.01f, distance));
        }
    }
}
