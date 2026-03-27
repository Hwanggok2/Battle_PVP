using UnityEngine;
using BattlePvp.Combat;
using BattlePvp.Stats;
using System.Collections.Generic;

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
        private readonly HashSet<IDamageReceiver> _hitTargets = new HashSet<IDamageReceiver>();

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null) _collider.isTrigger = true; // [버그 수정] 물리적 충돌로 인한 공중제비 방지

            if (_attackProcessor == null) _attackProcessor = GetComponentInParent<AttackProcessor>();
            
            // [버그 수정] 무기 히트박스에 Rigidbody가 있으면 Kinematic으로 설정
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

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
            _hitTargets.Clear(); // [기능 개선] 애니메이션 1회당 중복 타격 방지를 위해 리스트 초기화
        }

        public void DisableHitBox()
        {
            if (_collider != null) _collider.enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            // [디버그] 충돌 감지 확인
            Debug.Log($"[HitBox] Trigger entered with: {other.gameObject.name} (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");

            // IDamageReceiver를 가진 오브젝트인지 확인
            IDamageReceiver defender = other.GetComponent<IDamageReceiver>();
            if (defender == null) defender = other.GetComponentInParent<IDamageReceiver>();

            if (defender != null)
            {
                // 자기 자신 제외
                if (other.gameObject == transform.root.gameObject) return;

                // [기능 개선] 이미 이번 애니메이션에서 맞은 대상이면 스킵
                if (_hitTargets.Contains(defender)) return;

                StatManager defenderStats = other.GetComponent<StatManager>();
                if (defenderStats == null) defenderStats = other.GetComponentInParent<StatManager>();

                if (defenderStats != null && _attackProcessor != null)
                {
                    // [기능 추가] 피격 지점 계산 (트리거이므로 ClosestPoint 사용)
                    Vector3 hitPosition = other.ClosestPoint(transform.position);
                    _attackProcessor.ProcessHit(_currentAttackData, defenderStats, defender, hitPosition);
 
                    // [기능 개선] 타격 완료된 대상으로 기록
                    _hitTargets.Add(defender);
                }
            }
        }
    }
}
