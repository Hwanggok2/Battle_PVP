using UnityEngine;
using BattlePvp.Combat;

namespace BattlePvp.DebugTools
{
    /// <summary>
    /// 플레이어 사망 테스트를 위해 화면에 데미지 버튼을 생성하는 유틸리티 스크립트입니다.
    /// 플레이어 오브젝트나 씬의 빈 오브젝트에 부착하여 사용하세요.
    /// </summary>
    public class DamageTester : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _damageAmount = 50f;
        [SerializeField] private bool _useLocalPlayer = true;
        [SerializeField] private HealthSystem _targetHealth;

        private void OnGUI()
        {
            // 화면 왼쪽 상단에 버튼 배치
            GUILayout.BeginArea(new Rect(20, 20, 200, 300));
            GUILayout.Space(10);
            
            GUI.color = Color.red;
            if (GUILayout.Button($"DEAL {_damageAmount} DAMAGE", GUILayout.Height(50)))
            {
                ApplyTestDamage();
            }

            GUI.color = Color.white;
            if (GUILayout.Button("FULL REFILL HP", GUILayout.Height(30)))
            {
                RefillTestHp();
            }

            GUILayout.EndArea();
        }

        private void ApplyTestDamage()
        {
            HealthSystem target = GetTarget();
            if (target != null)
            {
                Debug.Log($"[DamageTester] Applying {_damageAmount} damage to {target.gameObject.name}");
                target.ApplyDamage(_damageAmount, DamageSource.Physical, Vector3.zero);
            }
            else
            {
                Debug.LogWarning("[DamageTester] No target HealthSystem found!");
            }
        }

        private void RefillTestHp()
        {
            HealthSystem target = GetTarget();
            if (target != null)
            {
                target.Revive(1.0f);
                target.RefillHealth();
                Debug.Log($"[DamageTester] HP Refilled for {target.gameObject.name}");
            }
        }

        private HealthSystem GetTarget()
        {
            // 1. 수동 지정된 타겟 우선
            if (_targetHealth != null) return _targetHealth;

            // 2. 이 컴포넌트가 붙은 오브젝트의 HealthSystem
            if (TryGetComponent<HealthSystem>(out var hs)) return hs;

            // 3. 로컬 플레이어 찾기 (Mirror 대응)
            if (_useLocalPlayer)
            {
                var players = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
                foreach (var p in players)
                {
                    if (p.isLocalPlayer) return p;
                }
            }

            return null;
        }
    }
}
