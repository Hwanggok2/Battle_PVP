using UnityEngine;
using BattlePvp.Combat;
using BattlePvp.UI;
using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 훈련용 허수아비의 체력을 관리하는 스크립트입니다.
    /// 플레이어와 동일한 StatManager를 통해 방어력 및 간접 수치(피해 감소 등)를 적용받습니다.
    /// </summary>
    [RequireComponent(typeof(StatManager))]
    public class DummyHealth : MonoBehaviour, IDamageReceiverWithContext
    {
        private static readonly Color PoisonPopupColor = new Color(0.25f, 1f, 0.25f, 1f);
        private const float PoisonPopupFontSizeDelta = -16f;

        [Header("Stat Configuration")]
        [SerializeField] private DummyStatData _statData;

        [Header("Runtime Status (Read Only)")]
        [SerializeField] private float _currentHp;
        [SerializeField] private float _maxHp;
        [SerializeField] private float _currentRegen;
        [SerializeField] private float _attackPower;
        [SerializeField] private float _physicalPenetration;
        [SerializeField] private float _moveSpeed;
        [SerializeField] private float _attackSpeed;
        [SerializeField] private float _defenseRate;
        [SerializeField] private IdentityType _identity;

        public float CurrentHp => _currentHp;
        public float MaxHp => _maxHp;

        private StatManager _statManager;
        private IDamageReceiver _lastAttacker;

        private void Awake()
        {
            _statManager = GetComponent<StatManager>();
            
            if (_statData != null)
            {
                ApplyStatData();
            }
        }

        private void OnEnable()
        {
            if (_statManager != null)
            {
                _statManager.StatsChanged += OnStatsChanged;
                RefreshInspectorStats();
            }
        }

        private void OnDisable()
        {
            if (_statManager != null)
                _statManager.StatsChanged -= OnStatsChanged;
        }

        private void OnStatsChanged(StatContainer _) => RefreshInspectorStats();

        public void ApplyStatData()
        {
            if (_statData == null || _statManager == null) return;

            StatContainer stats = new StatContainer();
            stats.STR.Invested = _statData.STR;
            stats.CON.Invested = _statData.CON;
            stats.AGI.Invested = _statData.AGI;
            stats.DEF.Invested = _statData.DEF;

            _statManager.ApplyStats(stats);
            
            // 초기 체력 설정
            _currentHp = _maxHp;
        }

        private void RefreshInspectorStats()
        {
            if (_statManager == null) return;

            Identity id = _statManager.CurrentIdentity;
            DerivedCombatStats derived = _statManager.GetDerivedStats();
            _maxHp = derived.MaxHp;
            _currentRegen = derived.RegenPerSecond;
            _attackPower = derived.AttackPower;
            _physicalPenetration = derived.PenetrationPercent;
            _moveSpeed = derived.MoveSpeed;
            _attackSpeed = derived.AttackSpeed;
            _defenseRate = derived.DefenseEfficiencyPercent;
            _identity = id.Type;

            // MaxHp가 바뀌었을 때 현재 체력이 Max를 넘지 않도록 조정
            _currentHp = Mathf.Min(_currentHp, _maxHp);
        }

        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition)
        {
            ApplyDamage(amount, source, 0f, null, hitPosition);
        }

        public void ApplyDamage(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker, Vector3 hitPosition)
        {
            if (attacker != null)
            {
                _lastAttacker = attacker;
            }

            // 실제 체력 차감
            _currentHp = Mathf.Clamp(_currentHp - amount, 0f, _maxHp);
            
            // 데미지 팝업을 피격 지점에 띄웁니다.
            Vector3 popupPosition = hitPosition == Vector3.zero ? transform.position + Vector3.up : hitPosition;
            if (DamagePopupManager.Instance != null)
            {
                if (source == DamageSource.Poison)
                    DamagePopupManager.Instance.CreatePopupWithFontDelta(popupPosition, amount, false, PoisonPopupColor, PoisonPopupFontSizeDelta);
                else
                    DamagePopupManager.Instance.CreatePopup(popupPosition, amount);
            }
            CombatHitFeedback.PlayStatusDamageForAttacker(source, attacker);

            Debug.Log($"[Dummy] Received {amount} damage from {source} at {hitPosition}. Current HP: {_currentHp}/{_maxHp}");

            // 더미가 사망 시 점수 부여 및 부활 처리
            if (_currentHp <= 0f)
            {
                if (_lastAttacker != null && _lastAttacker is MonoBehaviour attackerMb && attackerMb != null)
                {
                    var attackerScore = attackerMb.GetComponent<ScoreSystem>();
                    if (attackerScore != null)
                    {
                        // 서버라면 직접 AddPoint, 클라이언트라면 Command를 통해 서버에 요청
                        if (Mirror.NetworkServer.active)
                        {
                            attackerScore.AddPoint(0);
                        }
                        else
                        {
                            attackerScore.CmdAddPoint(0);
                        }
                        Debug.Log($"[DummyHealth] {attackerMb.gameObject.name} killed Dummy. No score awarded.");
                    }
                }

                // 사망 후 즉시 체력 회복 (훈련용 더미 특성)
                _currentHp = _maxHp;
                _lastAttacker = null;
                Debug.Log("[DummyHealth] Dummy respawned (HP restored).");
            }
        }
    }
}
