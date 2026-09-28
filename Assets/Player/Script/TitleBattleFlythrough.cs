using UnityEngine;

namespace BattlePvp.CameraLogic
{
    /// <summary>제목/로그인 화면에서 전장 상공을 천천히 순회하는 배경 카메라.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class TitleBattleFlythrough : MonoBehaviour
    {
        [SerializeField, Min(10f)] private float _loopSeconds = 75f;
        private float _elapsed;

        private void OnEnable()
        {
            _elapsed = 0f;
            ApplyPose(0f);
        }

        private void LateUpdate()
        {
            _elapsed = Mathf.Repeat(_elapsed + Time.unscaledDeltaTime, Mathf.Max(10f, _loopSeconds));
            ApplyPose(_elapsed / Mathf.Max(10f, _loopSeconds));
        }

        public void ApplyPose(float phase)
        {
            float angle = phase * Mathf.PI * 2f - Mathf.PI * .5f;
            Vector3 position = new Vector3(Mathf.Cos(angle) * 23f, 11f + Mathf.Sin(angle * 2f) * 2f, Mathf.Sin(angle) * 19f);
            Vector3 focus = new Vector3(Mathf.Cos(angle + .35f) * 3f, 1.6f, Mathf.Sin(angle + .35f) * 3f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }
    }
}
