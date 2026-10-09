using UnityEngine;

namespace BattlePvp.Combat
{
    [System.Serializable]
    public struct MeleeMotionSample
    {
        public Vector3 position, pivot, referenceBase, referenceTip;
        public Quaternion rotation;

        public static bool TryEvaluate(AttackData attack, Transform root, float phase, Vector3 aim,
            Vector3 lookDirection, float weight, out Pose pose, Avatar avatar = null)
        {
            pose = default;
            var track = attack != null ? attack.FindVisualAim(avatar)?.samples ?? attack.motionSamples : null;
            if (track == null || track.Length < 2) return false;
            float index = Mathf.Clamp01(phase) * (track.Length - 1);
            int first = Mathf.Min(Mathf.FloorToInt(index), track.Length - 2);
            float t = index - first;
            var a = track[first]; var b = track[first + 1];
            Vector3 referenceBase = root.TransformVector(Vector3.Lerp(a.referenceBase, b.referenceBase, t));
            Vector3 referenceTip = root.TransformVector(Vector3.Lerp(a.referenceTip, b.referenceTip, t));
            float low = 0, high = 1, reach = aim.magnitude;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * .5f;
                if (Vector3.Lerp(referenceBase, referenceTip, middle).magnitude < reach) low = middle;
                else high = middle;
            }
            Vector3 reference = Vector3.Lerp(referenceBase, referenceTip, (low + high) * .5f);
            Quaternion correction = MeleeAimPose.Correction(root.forward, reference, aim, lookDirection, phase, weight, attack.maxAimCalibration, attack.CrossingPhase(avatar));
            Vector3 pivot = root.TransformPoint(Vector3.Lerp(a.pivot, b.pivot, t));
            Vector3 position = root.TransformPoint(Vector3.Lerp(a.position, b.position, t));
            pose = new Pose(pivot + correction * (position - pivot),
                correction * root.rotation * Quaternion.Slerp(a.rotation, b.rotation, t));
            return true;
        }

        // Retain each visible endpoint (crouch, movement and aim), while recovering the authored
        // arc between them. Quaternion.Slerp alone loses rotations exceeding 180 degrees per frame.
        public static Pose MatchEndpoints(Pose sample, Pose expectedFrom, Pose expectedTo, Pose actualFrom, Pose actualTo, float t)
        {
            sample.position += Vector3.Lerp(actualFrom.position - expectedFrom.position, actualTo.position - expectedTo.position, t);
            sample.rotation = Quaternion.Slerp(actualFrom.rotation * Quaternion.Inverse(expectedFrom.rotation),
                actualTo.rotation * Quaternion.Inverse(expectedTo.rotation), t) * sample.rotation;
            return sample;
        }
    }
}
