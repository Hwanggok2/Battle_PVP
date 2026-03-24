using System;
using System.Collections;
using BattlePvp.Combat;
using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 플레이어의 HP를 관리하는 런타임 시스템.
    /// - CON에 따라 MaxHP가 동적으로 변한다. (FinalTotal(CON) 기반)
    /// - ApplyDamage는 "최종 피해"를 적용한다. (계산은 AttackProcessor/DamageCalculator에서 선행)
    /// - Monostat(DEF)일 때 Physical 피해를 받으면 Thorns를 반사한다. (재반사 방지: Thorns source는 반사 트리거 금지)
    /// - Strategist일 때 HP overflow(현재 HP > MaxHP)는 overflow 상태에서만 코루틴으로 틱 감소한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthSystem : MonoBehaviour, IDamageReceiverWithContext, IPlayerStatusSource
    {
        [Header("References")]
        [SerializeField] private StatManager _statManager;

        [Header("HP Model")]
        [SerializeField] private float _baseMaxHp = 100f;
        [Tooltip("FinalTotal(CON) 1당 증가하는 최대 HP")]
        [SerializeField] private float _maxHpPerCon = 5f;

        [Header("Runtime")]
        [SerializeField] private float _currentHp = 100f;

        public float CurrentHp => _currentHp;
        public float MaxHp => _maxHp;

        public event Action<float, float> HpChanged;
        public event Action<bool, float> OverflowChanged;

        private float _maxHp;
        private float _lastOverlapPercent;
        private bool _isOverflowActive;

        private DamageCalculator _damageCalculator;
        private StrategistRules _strategistRules;
        private Coroutine _overflowRoutine;

        private void Awake()
        {
            if (_statManager == null)
                _statManager = GetComponent<StatManager>();

            _damageCalculator = new DamageCalculator();
            _strategistRules = new StrategistRules();
        }

        private void OnEnable()
        {
            RefreshFromStats(keepCurrentHpFlat: true);

            if (_statManager != null)
                _statManager.StatsChanged += OnStatsChanged;
        }

        private void OnDisable()
        {
            if (_statManager != null)
                _statManager.StatsChanged -= OnStatsChanged;
        }

        private void OnStatsChanged(StatContainer _)
        {
            RefreshFromStats(keepCurrentHpFlat: true);
        }

        /// <summary>
        /// 스탯 변경(재분배/장비 변경 등) 이후 호출하여 MaxHP를 재계산합니다.
        /// "Flat HP Logic": 현재 HP는 비율이 아닌 고정 수치로 유지됩니다.
        /// </summary>
        public void RefreshFromStats(bool keepCurrentHpFlat)
        {
            float newMax = PredictMaxHp();
            if (newMax <= 1f) newMax = 1f;

            _maxHp = newMax;

            if (!keepCurrentHpFlat)
                _currentHp = Mathf.Min(_currentHp, _maxHp);

            RaiseHpChanged();
            UpdateOverflowState();
        }

        /// <summary>
        /// 외부에서 강제 회복/세팅 시 사용.
        /// </summary>
        public void SetCurrentHp(float hp)
        {
            _currentHp = hp < 0f ? 0f : hp;
            RaiseHpChanged();
            UpdateOverflowState();
        }

        public void ApplyDamage(float amount, DamageSource source)
        {
            ApplyDamage(amount, source, attackerAttackPower: 0f, attacker: null);
        }

        public void ApplyDamage(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker)
        {
            if (amount <= 0f)
                return;

            float next = _currentHp - amount;
            _currentHp = next < 0f ? 0f : next;

            RaiseHpChanged();

            // Thorns 처리(재반사 금지)
            // - Monostat DEF일 때만
            // - Physical 피해일 때만
            // - attacker 정보가 있어야 반사 가능
            if (source == DamageSource.Physical && attacker != null && attackerAttackPower > 0f && IsMonostatDef())
            {
                float thorns = _damageCalculator.PredictThornsReflectDamage(attackerAttackPower, MaxHp);
                if (thorns > 0f)
                {
                    // attacker가 context 인터페이스를 구현하면 그대로, 아니면 기본 ApplyDamage로 적용
                    if (attacker is IDamageReceiverWithContext ctx)
                        ctx.ApplyDamage(thorns, DamageSource.Thorns, attackerAttackPower: 0f, attacker: null);
                    else
                        attacker.ApplyDamage(thorns, DamageSource.Thorns);
                }
            }

            UpdateOverflowState();
        }

        private float PredictMaxHp()
        {
            if (_statManager == null)
                return _baseMaxHp;

            float conFinal = _statManager.GetFinalTotal(StatKind.CON);
            float max = _baseMaxHp + (conFinal * _maxHpPerCon);

            // Monostat CON: 최대 체력 +60% (스펙 반영)
            Identity id = _statManager.CurrentIdentity;
            if (id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.CON)
                max *= 1.6f;

            return max;
        }

        private bool IsMonostatDef()
        {
            if (_statManager == null)
                return false;

            Identity id = _statManager.CurrentIdentity;
            return id.Type == IdentityType.Monostat && id.PrimaryStat == StatKind.DEF;
        }

        private void UpdateOverflowState()
        {
            bool shouldOverflow = _currentHp > _maxHp && _maxHp > 0f;
            float overlap = shouldOverflow ? Mathf.Clamp01((_currentHp - _maxHp) / _maxHp) : 0f;

            if (Math.Abs(overlap - _lastOverlapPercent) > 0.0001f || shouldOverflow != _isOverflowActive)
            {
                _lastOverlapPercent = overlap;
                _isOverflowActive = shouldOverflow;
                OverflowChanged?.Invoke(_isOverflowActive, _lastOverlapPercent);
            }

            // Strategist overflow는 시간 기반이므로, strategist + overflow일 때만 틱을 돌린다.
            if (shouldOverflow && IsStrategist())
                EnsureOverflowRoutine();
            else
                StopOverflowRoutine();
        }

        private bool IsStrategist()
        {
            if (_statManager == null)
                return false;
            return _statManager.CurrentIdentity.Type == IdentityType.Strategist;
        }

        private void EnsureOverflowRoutine()
        {
            if (_overflowRoutine != null)
                return;
            _overflowRoutine = StartCoroutine(CoOverflowTick());
        }

        private void StopOverflowRoutine()
        {
            if (_overflowRoutine == null)
                return;
            StopCoroutine(_overflowRoutine);
            _overflowRoutine = null;
        }

        private IEnumerator CoOverflowTick()
        {
            // GC 최소화를 위해 WaitForEndOfFrame/WaitForSeconds 할당 없이 프레임 기반으로 처리
            while (true)
            {
                // overflow가 해소되었으면 종료
                if (_maxHp <= 0f || _currentHp <= _maxHp || !IsStrategist())
                {
                    _overflowRoutine = null;
                    yield break;
                }

                float next = _strategistRules.TickOverflow(_currentHp, _maxHp, Time.deltaTime);
                if (Math.Abs(next - _currentHp) > 0.0001f)
                {
                    _currentHp = next;
                    RaiseHpChanged();
                    UpdateOverflowState();
                }

                yield return null;
            }
        }

        private void RaiseHpChanged()
        {
            HpChanged?.Invoke(_currentHp, _maxHp);
        }
    }
}

