using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Mesh-space additive FK after animation, before melee queries. The authored swing is retained.</summary>
    public sealed class MeleeAimPose
    {
        private readonly Transform _root;
        private readonly Transform[] _bones;
        private readonly Quaternion[] _baseRotations;
        private bool _applied;

        public MeleeAimPose(Transform root, Animator animator)
        {
            _root = root;
            _bones = animator != null && animator.isHuman ? new[] {
                animator.GetBoneTransform(HumanBodyBones.Spine),
                animator.GetBoneTransform(HumanBodyBones.Chest),
                animator.GetBoneTransform(HumanBodyBones.UpperChest) } : new Transform[0];
            _baseRotations = new Quaternion[_bones.Length];
        }

        public Vector3 Pivot => _bones.Length > 0 && _bones[0] != null ? _bones[0].position :
            _root.position + _root.up * 1.2f;
        public Vector3 ReferenceVector(Vector3 reference) => _bones.Length > 0 && _bones[0] != null ?
            _bones[0].parent.TransformVector(reference) : _root.TransformVector(reference);

        public Vector3 SelectReference(AttackData attack, float reach)
        {
            if (attack == null) return Vector3.forward;
            if (attack.aimBladeTip.sqrMagnitude < .001f || reach <= 0) return attack.aimBladePoint;
            // The calibrated blade extends away from the pivot. Clamp to its physical ends.
            float low = 0, high = 1;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * .5f;
                if (ReferenceVector(Vector3.Lerp(attack.aimBladeBase, attack.aimBladeTip, middle)).magnitude < reach) low = middle;
                else high = middle;
            }
            return Vector3.Lerp(attack.aimBladeBase, attack.aimBladeTip, (low + high) * .5f);
        }

        public void Restore()
        {
            if (!_applied) return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = _baseRotations[i];
            _applied = false;
        }

        public void Apply(Vector3 direction)
        {
            if (!CombatValidation.IsFinite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .001f) return;
            int count = 0;
            for (int i = 0; i < _bones.Length; i++) if (_bones[i] != null) count++;
            if (count == 0) return;
            // Animator has evaluated since Restore. Save its pose, not last frame's corrected rotations.
            Quaternion delta = Quaternion.FromToRotation(_root.forward, direction.normalized);
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
        public void ApplyCalibrated(Vector3 direction, Vector3 reference, float phase, float weight, Vector3 lookDirection = default)
        {
            if (_bones.Length == 0 || _bones[0] == null || !CombatValidation.IsFinite(direction) ||
                direction.sqrMagnitude < .001f || float.IsInfinity(direction.sqrMagnitude)) return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _baseRotations[i] = _bones[i].localRotation;
            float calibrationWeight = Mathf.SmoothStep(0, 1, phase / .35f) *
                Mathf.SmoothStep(0, 1, (1f - phase) / .16f);
            Vector3 source = Vector3.Slerp(_root.forward, ReferenceVector(reference).normalized, calibrationWeight);
            Quaternion delta = Quaternion.FromToRotation(source, direction.normalized);
            Quaternion look = lookDirection.sqrMagnitude > .001f ? Quaternion.FromToRotation(_root.forward, lookDirection.normalized) : Quaternion.identity;
            // Blend two absolute corrections, so looking up is not added twice during an attack.
            _bones[0].rotation = Quaternion.Slerp(look, delta, Mathf.Clamp01(weight)) * _bones[0].rotation;
            _applied = true;
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
