using UnityEngine;
using BattlePvp.Combat;
using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 무기나 손의 콜라이더에 부착하여 충돌을 감지하는 스크립트입니다.
    /// </summary>
    public class MeleeHitBox : MonoBehaviour
    {
        [SerializeField] private AttackProcessor _attackProcessor;
        [SerializeField] private AttackData _currentAttackData;

        private Collider _collider;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_attackProcessor == null) _attackProcessor = GetComponentInParent<AttackProcessor>();
            
            // 처음에는 꺼둠
            DisableHitBox();
        }

        public void SetAttackData(AttackData data)
        {
            _currentAttackData = data;
        }

        public void EnableHitBox()
        {
            if (_collider != null) _collider.enabled = true;
        }

        public void DisableHitBox()
        {
            if (_collider != null) _collider.enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            // IDamageReceiver를 가진 오브젝트인지 확인
            IDamageReceiver defender = other.GetComponent<IDamageReceiver>();
            if (defender == null) defender = other.GetComponentInParent<IDamageReceiver>();

            if (defender != null)
            {
                // 자기 자신 제외
                if (other.gameObject == transform.root.gameObject) return;

                StatManager defenderStats = other.GetComponent<StatManager>();
                if (defenderStats == null) defenderStats = other.GetComponentInParent<StatManager>();

                if (defenderStats != null && _attackProcessor != null)
                {
                    _attackProcessor.ProcessHit(_currentAttackData, defenderStats, defender);
                }
            }
        }
    }
}
