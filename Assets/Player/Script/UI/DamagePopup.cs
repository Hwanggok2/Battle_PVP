using UnityEngine;
using TMPro;

namespace BattlePvp.UI
{
    /// <summary>
    /// 데미지 숫자를 화면(World Space)에 띄우고 애니메이션하며 사라지게 하는 스크립트입니다.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        [SerializeField] private TMP_Text _textMesh;
        [SerializeField] private float _moveYSpeed = 1f;
        [SerializeField] private float _disappearSpeed = 3f;
        [SerializeField] private float _lifetime = 0.7f;

        private Color _textColor;
        private float _disappearTimer;
        private float _defaultFontSize;
        private Color _defaultColor;
        private System.Action<DamagePopup> _release;
        private System.Action<DamagePopup> _destroyed;
        private bool _returned;
        private Camera _camera;

        internal void SetPoolRelease(System.Action<DamagePopup> release, System.Action<DamagePopup> destroyed = null)
        {
            _release = release;
            _destroyed = destroyed;
        }

        private void OnDisable()
        {
            if (_release != null) ReturnToPool();
        }

        private void OnDestroy()
        {
            _destroyed?.Invoke(this);
            _release = null;
            _destroyed = null;
        }

        internal void ReturnToPool()
        {
            if (_returned) return;
            _returned = true;
            if (_release != null) _release(this);
            else Destroy(gameObject);
        }
 
        [Header("Critical Hit Settings")]
        [SerializeField] private float _criticalFontScale = 1.6f;
        [SerializeField] private Color _criticalColor = Color.red;
 
        private void Awake()
        {
            // 인스펙터에서 깜빡하고 연결 안 했을 때를 위한 자동 찾기
            if (_textMesh == null) _textMesh = GetComponent<TMP_Text>();
            if (_textMesh == null) _textMesh = GetComponentInChildren<TMP_Text>();
 
            if (_textMesh != null)
            {
                _defaultFontSize = _textMesh.fontSize;
                _defaultColor = _textMesh.color;
            }
        }
 
        public void Setup(float damageAmount, bool isCritical = false)
        {
            Setup(damageAmount, isCritical, false, Color.white);
        }

        public void Setup(float damageAmount, bool isCritical, Color color)
        {
            Setup(damageAmount, isCritical, true, color);
        }

        public void Setup(float damageAmount, bool isCritical, Color color, float fontSize)
        {
            Setup(damageAmount, isCritical, true, color, fontSize);
        }

        public void SetupWithFontDelta(float damageAmount, bool isCritical, Color color, float fontSizeDelta)
        {
            float fontSize = Mathf.Max(1f, _defaultFontSize + fontSizeDelta);
            Setup(damageAmount, isCritical, true, color, fontSize);
        }

        private void Setup(float damageAmount, bool isCritical, bool useColorOverride, Color colorOverride)
        {
            Setup(damageAmount, isCritical, useColorOverride, colorOverride, -1f);
        }

        private void Setup(float damageAmount, bool isCritical, bool useColorOverride, Color colorOverride, float fontSizeOverride)
        {
            _returned = false;
            _disappearTimer = Mathf.Max(0f, _lifetime);
            if (_textMesh == null)
            {
                Debug.LogError($"[DamagePopup] _textMesh가 할당되지 않았습니다! 프리팹을 확인해 주세요.", gameObject);
                return;
            }

            _textMesh.SetText(damageAmount.ToString("F0"));
            
            if (isCritical)
            {
                _textMesh.fontSize = _defaultFontSize * _criticalFontScale;
                _textColor = _criticalColor;
            }
            else
            {
                _textMesh.fontSize = fontSizeOverride > 0f ? fontSizeOverride : _defaultFontSize;
                _textColor = useColorOverride ? colorOverride : _defaultColor;
            }

            _textMesh.color = _textColor;
            _disappearTimer = _lifetime;
        }

        private void Update()
        {
            _disappearTimer -= Time.deltaTime;
            if (_disappearTimer < 0)
            {
                // 알파값 감소 (사라지기)
                _textColor.a -= _disappearSpeed * Time.deltaTime;
                if (_textMesh != null)
                {
                    _textMesh.color = _textColor;
                }

                if (_textColor.a < 0)
                {
                    ReturnToPool();
                }
            }
        }

        private void LateUpdate()
        {
            // 빌보드 기능: 카메라를 항상 정면으로 바라보게 함
            if (_camera == null || !_camera.isActiveAndEnabled || !_camera.CompareTag("MainCamera"))
                _camera = Camera.main;
            if (_camera != null)
            {
                transform.LookAt(transform.position + _camera.transform.rotation * Vector3.forward,
                                 _camera.transform.rotation * Vector3.up);
            }
        }
    }
}
