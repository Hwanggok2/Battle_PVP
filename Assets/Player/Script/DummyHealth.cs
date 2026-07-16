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
        private const float BaseAttackPower = 22f;
        private const float BaseStatTotal = 5f;
        private const float AttackPowerPerStr = 3f;
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

            // HealthSystem/AttackProcessor/PlayerManager/PlayerCombat의 공식과 동일하게 계산
            float con = _statManager.GetFinalTotal(StatKind.CON);
            float str = _statManager.GetFinalTotal(StatKind.STR);
            float agi = _statManager.GetFinalTotal(StatKind.AGI);
            float def = _statManager.GetFinalTotal(StatKind.DEF);
            
            Identity id = _statManager.CurrentIdentity;

            // 1. HP 공식 (Base 100 + CON * 15 + DEF * 2)
            float baseMax = 100f + (con * 15f) + (def * 2f);
            if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.CON) baseMax *= 1.6f;
            else if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.AGI) baseMax *= 0.7f;
            _maxHp = baseMax;

            // 2. Regen 공식 (CON * 0.15)
            _currentRegen = con * 0.15f; 
            if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.CON) _currentRegen += 5f;

            // 3. Attack Power 공식 (Base 22 + STR * 3)
            _attackPower = BaseAttackPower + (Mathf.Max(0f, str - BaseStatTotal) * AttackPowerPerStr); 
            if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.STR) _attackPower *= 1.4f;

            // 4. Physical Penetration 공식 (STR * 0.3)
            _physicalPenetration = str * 0.3f;
            if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.STR) _physicalPenetration += 18f;

            // 5. Move Speed 공식 (Base 3.0 + AGI * 0.04)
            _moveSpeed = 3.0f + (agi * 0.04f);
            if (id.Type == IdentityType.Monostat)
            {
                if (id.PrimaryStat == StatKind.AGI) _moveSpeed *= 1.2f;
                else if (id.PrimaryStat == StatKind.STR) _moveSpeed *= 0.75f;
                else if (id.PrimaryStat == StatKind.DEF) _moveSpeed *= 0.8f;
            }

            // 6. Attack Speed 공식 (Base 0.6 + AGI * 0.02)
            _attackSpeed = 0.6f + (agi * 0.02f);
            if (id.Type == IdentityType.Monostat)
            {
                if (id.PrimaryStat == StatKind.AGI) _attackSpeed *= 3f;
                else if (id.PrimaryStat == StatKind.STR) _attackSpeed *= 0.75f;
            }

            _defenseRate = def;
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
