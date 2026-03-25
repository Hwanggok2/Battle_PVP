using UnityEngine;
using TMPro;

namespace BattlePvp.UI
{
    /// <summary>
    /// 데미지 숫자를 화면(World Space)에 띄우고 애니메이션하며 사라지게 하는 스크립트입니다.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _textMesh;
        [SerializeField] private float _moveYSpeed = 1f;
        [SerializeField] private float _disappearSpeed = 3f;
        [SerializeField] private float _lifetime = 0.7f;

        private Color _textColor;
        private float _disappearTimer;

        public void Setup(float damageAmount, bool isCritical = false)
        {
            _textMesh.SetText(damageAmount.ToString("F0"));
            
            if (isCritical)
            {
                _textMesh.fontSize = 6;
                _textColor = Color.red;
            }
            else
            {
                _textMesh.fontSize = 4;
                _textColor = Color.white;
            }

            _textMesh.color = _textColor;
            _disappearTimer = _lifetime;
        }

        private void Update()
        {
            // 상단으로 이동
            transform.position += new Vector3(0, _moveYSpeed) * Time.deltaTime;

            _disappearTimer -= Time.deltaTime;
            if (_disappearTimer < 0)
            {
                // 알파값 감소 (사라지기)
                _textColor.a -= _disappearSpeed * Time.deltaTime;
                _textMesh.color = _textColor;

                if (_textColor.a < 0)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
