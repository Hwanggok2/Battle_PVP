using UnityEngine;
using BattlePvp.Combat;
using BattlePvp.UI;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 훈련용 허수아비의 체력을 관리하는 스크립트입니다.
    /// 실제로 죽지 않고 피격 시 데미지만 팝업으로 띄웁니다.
    /// </summary>
    public class DummyHealth : MonoBehaviour, IDamageReceiver
    {
        [SerializeField] private float _maxHp = 99999f;
        [SerializeField] private float _currentHp = 99999f;

        public float CurrentHp => _currentHp;
        public float MaxHp => _maxHp;

        public void ApplyDamage(float amount, DamageSource source)
        {
            // 실제 체력을 깎지는 않거나, 혹은 기록만 남깁니다.
            // 데미지 팝업을 띄웁니다.
            if (DamagePopupManager.Instance != null)
            {
                // 허수아비 머리 위쪽쯤에 팝업
                DamagePopupManager.Instance.CreatePopup(transform.position + Vector3.up * 2f, amount);
            }

            Debug.Log($"[Dummy] Received {amount} damage from {source}");
        }
    }
}
