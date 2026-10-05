using System;
using System.Collections;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.Networking;
using BattlePvp.UI;
using Mirror;
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
    [RequireComponent(typeof(Mirror.NetworkIdentity))]
    public sealed class HealthSystem : Mirror.NetworkBehaviour, IDamageReceiverWithResult, IPlayerStatusSource
    {
        [Header("Networking")]
        [SyncVar] public bool isInvincible = false;
        [Header("References")]
        [SerializeField] private StatManager _statManager;
        [SerializeField] private Animator _animator; // 기존 프리팹의 생명 연출 참조를 바인딩에 전달한다.
        internal Animator LifeAnimator
        {
            get
            {
                // PlayerManager.Awake가 먼저 실행되어도 기존 자식 Animator 참조를 전달한다.
                if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
                return _animator;
            }
        }

        [Header("Runtime")]
        [SyncVar(hook = nameof(OnHpChangedInternal))]
        [SerializeField] private float _currentHp = 100f;

        private void OnHpChangedInternal(float oldHp, float newHp)
        {
            RaiseHpChanged();
            UpdateOverflowState();
        }

        public float CurrentHp => _currentHp;
        public float MaxHp => _maxHp;
        public float CurrentRegen => _currentRegen;
        public float CurrentShield => _currentShield;
        [SyncVar] private bool _hasDebuff;
        public bool HasDebuff => _hasDebuff;
        internal void SetDebuffPresentation(bool active) { if (CanChangeHealth) _hasDebuff = active; }
        private double SkillTime => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        public bool HasDefensiveSkillBuff => SkillTime < _tauntDefenseUntil &&
            (_tauntIncomingDamageMultiplier < 1f || _tauntReflectMultiplier > 1f);
        [SyncVar] private double _reviveAllowedAt;
        public double ReviveAllowedAt => _reviveAllowedAt;
        [SyncVar(hook = nameof(OnLifeStateSynced))]
        private bool _isDead;
        private bool _lastNotifiedIsDead;
        public bool IsDead => _isDead;
        public uint DeathSequence { get; private set; }
        // Mirror binds netIdentity in NetworkIdentity.Awake, after deserialization/OnValidate.
        private bool CanChangeHealth => netIdentity != null &&
            (isServer || (!NetworkServer.active && !NetworkClient.active));

        public event Action<float, float> HpChanged;
        public event Action<float> ShieldChanged;
        public event Action<Vector3> ShieldHit;
        public event Action<bool, float> OverflowChanged;
        public event Action OnDied;
        public event Action OnRevived;

        [Header("Runtime Status (Read Only)")]
        [SerializeField] private float _maxHp;
        [SerializeField] private float _currentRegen;
        [SerializeField] private float _defenseRate;
        [SyncVar(hook = nameof(OnShieldSynced))] [SerializeField] private float _currentShield;
        [SyncVar] [SerializeField] private double _shieldExpiresAt;
        [SerializeField] private float _shieldSyncIntervalSeconds = 0.05f;
        [SyncVar] [SerializeField] private double _skillInvulnerableUntil;
        [SyncVar] [SerializeField] private double _tauntDefenseUntil;
        [SyncVar] [SerializeField] private float _tauntIncomingDamageMultiplier = 1f;
        [SyncVar] [SerializeField] private float _tauntReflectMultiplier = 1f;
        [SyncVar] [SerializeField] private float _tauntReflectHealthCapRatio = 0.07f;
        private float _lastOverlapPercent;
        private bool _isOverflowActive;

        private DamageCalculator _damageCalculator;
        private StrategistRules _strategistRules;
        private Coroutine _overflowRoutine;
        private bool _overflowRoutineRunning;
        private Coroutine _regenRoutine;
        private Coroutine _shieldRoutine;

        private IDamageReceiver _lastAttacker;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ClearPopupPredictions() => HealthDamagePresentation.ClearCorrelations();

        private void Awake()
        {
            if (_statManager == null)
                _statManager = GetComponent<StatManager>();

            _damageCalculator = new DamageCalculator();
            _strategistRules = new StrategistRules();
            if (Application.isPlaying && GetComponent<DebuffIndicator>() == null) gameObject.AddComponent<DebuffIndicator>();
        }

        private void OnEnable()
        {
            RefreshFromStats(keepCurrentHpFlat: true);
            if (!NetworkServer.active && !NetworkClient.active && !IsDead)
                SetCurrentHp(_maxHp);

            if (_statManager != null)
            {
                _statManager.StatsChanged += OnStatsChanged;
                _statManager.DerivedStatsChanged += OnDerivedStatsChanged;
            }

            EnsureRegenRoutine();
            if (CanChangeHealth && _currentShield > 0f && _shieldRoutine == null)
                _shieldRoutine = StartCoroutine(CoDecayShield());
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            RefreshFromStats(keepCurrentHpFlat: true);
            if (!IsDead)
                SetCurrentHp(_maxHp);
            EnsureRegenRoutine();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshFromStats(keepCurrentHpFlat: true);
            PublishLifeState();
        }

        private void OnDisable()
        {
            if (_statManager != null)
            {
                _statManager.StatsChanged -= OnStatsChanged;
                _statManager.DerivedStatsChanged -= OnDerivedStatsChanged;
            }

            StopRegenRoutine();
            StopOverflowRoutine();
            if (_shieldRoutine != null)
                StopCoroutine(_shieldRoutine);
            _shieldRoutine = null;
        }

        private void OnDerivedStatsChanged() => RefreshFromStats(keepCurrentHpFlat: true);

        private void OnStatsChanged(StatContainer newStats)
        {
            if (this == null) return;
            
            bool isStrategist = _statManager != null && _statManager.CurrentIdentity.Type == IdentityType.Strategist;
            float oldHp = _currentHp;
            float oldMax = _maxHp;

            RefreshFromStats(keepCurrentHpFlat: true);

            // 스탯 변경 시 체력 수치 보정 (로비 더미 플레이어 포함)
            if (CanChangeHealth && !IsDead)
            {
                if (isStrategist)
                {
                    if (oldMax > 0f)
                    {
                        float ratio = oldHp / oldMax;
                        _currentHp = _maxHp * ratio;
                    }
                }
                else
                {
                    // [수정] 스탯 변경 시 최대 체력으로 회복
                    _currentHp = _maxHp;
                    Debug.Log($"[HealthSystem:{gameObject.name}] Health refilled to {_maxHp} due to stat change.");
                }
            }

            RaiseHpChanged();
            UpdateOverflowState();
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
            _currentRegen = PredictRegen();

            if (_statManager != null)
            {
                _defenseRate = _statManager.GetFinalTotal(StatKind.DEF);
            }

            if (!keepCurrentHpFlat && CanChangeHealth && !IsDead)
                _currentHp = Mathf.Min(_currentHp, _maxHp);

            RaiseHpChanged();
            UpdateOverflowState();
        }

        /// <summary>
        /// 외부에서 강제 회복/세팅 시 사용.
        /// </summary>
        public void SetCurrentHp(float hp)
        {
            if (!CanChangeHealth || IsDead || !float.IsFinite(hp))
                return;
            _currentHp = hp < 0f ? 0f : hp;
            RaiseHpChanged();
            UpdateOverflowState();
            EvaluateDeath(); // [추가] 강제 체력 설정 시에도 사망 판정
        }

        public void Heal(float amount)
        {
            if (!CanChangeHealth || !float.IsFinite(amount) || amount <= 0f || IsDead)
                return;

            float next = Mathf.Min(_currentHp + amount, _maxHp);
            if (Math.Abs(next - _currentHp) <= 0.0001f)
                return;

            _currentHp = next;
            RaiseHpChanged();
            UpdateOverflowState();
        }

        public void GrantDecayingShield(float amount, float durationSeconds)
        {
            if (!CanChangeHealth || !float.IsFinite(amount) || !float.IsFinite(durationSeconds) ||
                amount <= 0f || durationSeconds <= 0f || IsDead)
                return;

            if (_shieldRoutine != null)
            {
                StopCoroutine(_shieldRoutine);
                _shieldRoutine = null;
            }

            float existingShield = _currentShield > 0.5f ? _currentShield : 0f;
            _currentShield = existingShield + amount;
            _shieldExpiresAt = SkillTime + durationSeconds;
            RaiseShieldChanged();
            _shieldRoutine = StartCoroutine(CoDecayShield());
        }

        public void SetSkillInvulnerable(float durationSeconds)
        {
            if (!CanChangeHealth || IsDead || !float.IsFinite(durationSeconds))
                return;
            _skillInvulnerableUntil = Math.Max(_skillInvulnerableUntil, SkillTime + Math.Max(0f, durationSeconds));
        }

        public void SetTauntDefense(float durationSeconds, float incomingDamageMultiplier, float reflectMultiplier, float reflectHealthCapRatio)
        {
            if (!CanChangeHealth || IsDead || !float.IsFinite(durationSeconds) ||
                !float.IsFinite(incomingDamageMultiplier) || !float.IsFinite(reflectMultiplier) ||
                !float.IsFinite(reflectHealthCapRatio))
                return;
            _tauntDefenseUntil = SkillTime + Math.Max(0f, durationSeconds);
            _tauntIncomingDamageMultiplier = Mathf.Clamp01(incomingDamageMultiplier);
            _tauntReflectMultiplier = Mathf.Max(0f, reflectMultiplier);
            _tauntReflectHealthCapRatio = Mathf.Clamp01(reflectHealthCapRatio);
        }

        private IEnumerator CoDecayShield()
        {
            while (CanChangeHealth && _currentShield > 0f && SkillTime < _shieldExpiresAt)
            {
                float remaining = Mathf.Max(0.001f, (float)(_shieldExpiresAt - SkillTime));
                float deltaSeconds = Mathf.Max(Time.deltaTime, _shieldSyncIntervalSeconds);
                _currentShield = Mathf.Max(0f, _currentShield - ((_currentShield / remaining) * deltaSeconds));
                RaiseShieldChanged();
                yield return new WaitForSeconds(Mathf.Max(0.01f, _shieldSyncIntervalSeconds));
            }

            if (CanChangeHealth)
            {
                _currentShield = 0f;
                _shieldExpiresAt = 0d;
                RaiseShieldChanged();
            }
            _shieldRoutine = null;
        }

        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition)
        {
            ApplyDamage(amount, source, attackerAttackPower: 0f, attacker: null, hitPosition);
        }

        public void ApplyDamage(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker, Vector3 hitPosition)
        {
            ApplyDamageWithPopupSource(amount, source, attackerAttackPower, attacker, hitPosition, source);
        }

        public DamageResult ApplyDamage(DamageRequest request)
        {
            return ApplyDamageWithPopupSource(request.Amount, request.Source, request.AttackerAttackPower,
                request.Attacker, request.HitPosition, request.PopupSource, request.PopupPredictionId);
        }

        public DamageResult ApplyDamageWithPopupSource(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker, Vector3 hitPosition, DamageSource popupSource, uint popupPredictionId = 0)
        {
            if (!CanChangeHealth || IsDead || isInvincible || SkillTime < _skillInvulnerableUntil ||
                (NetworkServer.active && _statManager != null && !_statManager.HasServerStats) ||
                (NetworkServer.active && connectionToClient != null && !connectionToClient.isReady) ||
                !float.IsFinite(amount) || amount <= 0f || !float.IsFinite(attackerAttackPower) ||
                attackerAttackPower < 0f || !CombatValidation.IsFinite(hitPosition))
                return default;

            var expanded = GetComponent<ExpandedSkillController>();
            if (expanded != null) { expanded.NotifyDamaged(); if(source != DamageSource.Fixed) amount *= expanded.IncomingMultiplier; }
            float hpBeforeDamage = _currentHp;
            bool tauntDefenseActive = SkillTime < _tauntDefenseUntil;
            if (tauntDefenseActive && source != DamageSource.Fixed)
                amount *= _tauntIncomingDamageMultiplier;
            if (amount <= 0f)
                return default;

            if (attacker != null)
            {
                _lastAttacker = attacker;
            }

            float absorbedByShield = Mathf.Min(_currentShield, amount);
            _currentShield -= absorbedByShield;
            if (absorbedByShield > 0f)
            {
                RaiseShieldChanged();
                Vector3 localHit = transform.InverseTransformPoint(hitPosition);
                if (NetworkServer.active && netId != 0) RpcShieldHit(localHit);
                else ShieldHit?.Invoke(localHit);
            }
            float hpDamage = amount - absorbedByShield;
            float next = _currentHp - hpDamage;
            
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Battle_waiting")
            {
                if (next < 1f) next = 1f;
            }

            _currentHp = next < 0f ? 0f : next;
            float actualHpDamage = Mathf.Max(0f, hpBeforeDamage - _currentHp);
            DamageResult result = new DamageResult(true, actualHpDamage, absorbedByShield, _currentHp <= 0f);
            if (isServer)
                RecordMatchDamage(actualHpDamage, attacker);
            ShowDamagePopup(hitPosition, amount, popupSource, attacker, popupPredictionId);

            EvaluateDeath(); // [공통 로직으로 교체]

            RaiseHpChanged();

            // Thorns 처리(재반사 금지)
            // - Monostat DEF일 때만
            // - Physical 피해일 때만
            // - attacker 정보가 있어야 반사 가능
            if (source == DamageSource.Physical && attacker != null && attackerAttackPower > 0f &&
                (IsMonostatDef() || (expanded != null && expanded.Active(JobSkillKind.Thorns))))
            {
                float thorns = _damageCalculator.PredictThornsReflectDamage(attackerAttackPower, attacker.MaxHp) * (GetComponent<ExpandedSkillController>()?.ReflectMultiplier ?? 1f);
                if (tauntDefenseActive)
                {
                    thorns *= _tauntReflectMultiplier;
                    thorns = Mathf.Min(thorns, attacker.MaxHp * _tauntReflectHealthCapRatio);
                }
                if (thorns > 0f)
                {
                    // attacker가 여전히 유효한지 확인
                    if (attacker != null && (attacker is MonoBehaviour attackerMb && attackerMb != null))
                    {
                        // attacker가 context 인터페이스를 구현하면 그대로, 아니면 기본 ApplyDamage로 적용
                        if (attacker is IDamageReceiverWithContext ctx)
                            ctx.ApplyDamage(thorns, DamageSource.Thorns, attackerAttackPower: 0f, attacker: this, HealthDamagePresentation.GetThornsPosition(attackerMb.transform));
                        else
                            attacker.ApplyDamage(thorns, DamageSource.Thorns, HealthDamagePresentation.GetThornsPosition(attackerMb.transform));
                    }
                }
            }

            UpdateOverflowState();
            return result;
        }

        [ClientRpc]
        private void RpcShieldHit(Vector3 localHit) => ShieldHit?.Invoke(localHit);

        [Server]
        private void RecordMatchDamage(float actualHpDamage, IDamageReceiver attacker)
        {
            if (actualHpDamage <= 0f ||
                BattleStateMachine.Instance == null ||
                BattleStateMachine.Instance.CurrentState != BattleState.InBattle)
            {
                return;
            }

            ScoreSystem victimScore = GetComponent<ScoreSystem>();
            if (victimScore == null || GetComponent<PlayerManager>() == null)
                return;

            victimScore.RecordDamageTaken(actualHpDamage);

            if (attacker is not MonoBehaviour attackerBehaviour)
                return;

            ScoreSystem attackerScore = attackerBehaviour.GetComponent<ScoreSystem>();
            if (attackerScore == null || attackerScore == victimScore || attackerBehaviour.GetComponent<PlayerManager>() == null)
                return;

            attackerScore.RecordDamageDealt(actualHpDamage);
        }

        private void ShowDamagePopup(Vector3 hitPosition, float amount, DamageSource source, IDamageReceiver attacker, uint predictionId)
        {
            Vector3 popupPosition = HealthDamagePresentation.ResolvePosition(transform, hitPosition, source);
            uint attackerNetId = GetDamageReceiverNetId(attacker);
            uint victimNetId = netIdentity != null ? netIdentity.netId : 0;

            if (isServer)
            {
                RpcShowDamagePopup(popupPosition, amount, source, attackerNetId, victimNetId, predictionId);
                return;
            }

            CreateDamagePopupLocal(popupPosition, amount, source, attackerNetId, victimNetId, predictionId);
        }

        [ClientRpc]
        private void RpcShowDamagePopup(Vector3 position, float amount, DamageSource source, uint attackerNetId, uint victimNetId, uint predictionId)
        {
            CreateDamagePopupLocal(position, amount, source, attackerNetId, victimNetId, predictionId);
        }

        private void CreateDamagePopupLocal(
            Vector3 position,
            float amount,
            DamageSource source,
            uint attackerNetId,
            uint victimNetId,
            uint predictionId)
        {
            bool localVictim = IsLocalPlayerNetId(victimNetId) || isLocalPlayer;
            bool localAttacker = attackerNetId != 0
                && IsLocalPlayerNetId(attackerNetId);
            HealthDamagePresentation.ShowLocal(transform, position, amount, source,
                attackerNetId, victimNetId, predictionId, localVictim, localAttacker);
        }

        private static uint GetDamageReceiverNetId(IDamageReceiver receiver)
        {
            if (receiver is MonoBehaviour mb && mb != null && mb.TryGetComponent(out NetworkIdentity identity))
                return identity.netId;

            return 0;
        }

        private static bool IsLocalPlayerNetId(uint netId)
        {
            return netId != 0
                && NetworkClient.localPlayer != null
                && NetworkClient.localPlayer.netId == netId;
        }

        private float PredictMaxHp()
        {
            if (_statManager == null)
                return StatBalanceCalculator.Config.BaseMaxHp;

            return _statManager.GetDerivedStats().MaxHp;
        }

        private float PredictRegen()
        {
            if (_statManager == null)
                return 0f;

            return _statManager.GetDerivedStats().RegenPerSecond;
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
            if (!CanChangeHealth || IsDead || !isActiveAndEnabled || _overflowRoutineRunning)
                return;
            _overflowRoutineRunning = true;
            _overflowRoutine = StartCoroutine(CoOverflowTick());
        }

        private void StopOverflowRoutine()
        {
            _overflowRoutineRunning = false;
            if (_overflowRoutine != null)
                StopCoroutine(_overflowRoutine);
            _overflowRoutine = null;
        }

        private IEnumerator CoOverflowTick()
        {
            // GC 최소화를 위해 WaitForEndOfFrame/WaitForSeconds 할당 없이 프레임 기반으로 처리
            while (true)
            {
                // overflow가 해소되었으면 종료
                if (!CanChangeHealth || IsDead || _maxHp <= 0f || _currentHp <= _maxHp || !IsStrategist())
                {
                    _overflowRoutineRunning = false;
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

        private void EnsureRegenRoutine()
        {
            if (!CanChangeHealth || !isActiveAndEnabled || _regenRoutine != null) return;
            _regenRoutine = StartCoroutine(CoRegenTick());
        }

        private void StopRegenRoutine()
        {
            if (_regenRoutine == null) return;
            StopCoroutine(_regenRoutine);
            _regenRoutine = null;
        }

        private IEnumerator CoRegenTick()
        {
            while (true)
            {
                if (!CanChangeHealth || IsDead)
                {
                    yield return null;
                    continue;
                }
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                bool isPreMatch = (BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState == BattleState.PreMatch);
                
                float effectiveRegen = _currentRegen;
                // [수정] 초강력 재생은 오직 Battle_waiting 또는 Battle_wait 씬에서만 작동합니다.
                bool isWaitingSceneOnly = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");

                if (isWaitingSceneOnly || isPreMatch)
                {
                    // 대기실에서는 초당 최대 체력의 50%씩 고속 회복 (사용자 요청)
                    effectiveRegen = Mathf.Max(effectiveRegen, _maxHp * 0.5f);
                }

                effectiveRegen *= GetComponent<ExpandedSkillController>()?.RegenMultiplier ?? 1f;
                if (effectiveRegen > 0f && _currentHp < _maxHp)
                {
                    // 일반 재생 로직 (로비도 로컬 환경이므로 허용)
                    if (CanChangeHealth)
                    {
                        float next = _currentHp + (effectiveRegen * Time.deltaTime);
                        _currentHp = Mathf.Min(next, _maxHp);
                        
                        RaiseHpChanged();
                    }
                }
                yield return null;
            }
        }

        private void RaiseHpChanged()
        {
            HpChanged?.Invoke(_currentHp, _maxHp);
        }

        private void RaiseShieldChanged()
        {
            ShieldChanged?.Invoke(_currentShield);
        }

        private void OnShieldSynced(float oldValue, float newValue)
        {
            ShieldChanged?.Invoke(newValue);
        }

        /// <summary>
        /// 체력이 0 이하인 경우 사망 처리를 진행합니다. 
        /// 인스펙터 수정, 네트워크 동기화, 데미지 적용 등 모든 상황에서 호출됩니다.
        /// </summary>
        private void EvaluateDeath()
        {
            if (CanChangeHealth && _currentHp <= 0f && !IsDead)
            {
                unchecked { DeathSequence++; }
                _reviveAllowedAt = NetworkTime.time + HealthUiLifeRules.RespawnDelaySeconds;
                _isDead = true;
                StopOverflowRoutine();
                PublishLifeState();
                
                // 막타 점수 부여 로직
                if (isServer && _lastAttacker != null)
                {
                    if (_lastAttacker is MonoBehaviour attackerMb && attackerMb != null)
                    {
                        var attackerScore = attackerMb.GetComponent<ScoreSystem>();
                        if (attackerScore != null)
                        {
                            var victimScore = GetComponent<ScoreSystem>();
                            attackerScore.RecordKillAgainst(victimScore);
                            string killerName = string.IsNullOrWhiteSpace(attackerScore.PlayerName) ? "Unknown" : attackerScore.PlayerName;
                            string victimName = victimScore != null && !string.IsNullOrWhiteSpace(victimScore.PlayerName) ? victimScore.PlayerName : gameObject.name;
                            if (BattleStateMachine.Instance != null)
                                BattleStateMachine.Instance.AnnounceKill(killerName, victimName);
                            Debug.Log($"[HealthSystem] {_lastAttacker} killed {gameObject.name}. Awarded 1 point.");
                        }
                    }
                }

                if (isServer)
                {
                    var playerManager = GetComponent<PlayerManager>();
                    if (playerManager != null)
                        playerManager.NotifyDeathFromServer();

                }
                Debug.Log($"[HealthSystem:{gameObject.name}] IsDead set to true.");
            }
        }

        private void OnLifeStateSynced(bool oldValue, bool newValue)
        {
            PublishLifeState();
        }

        private void PublishLifeState()
        {
            // Host SyncVar hooks and the server setter can both enter this path.
            if (_lastNotifiedIsDead == _isDead)
                return;
            _lastNotifiedIsDead = _isDead;
            if (_isDead)
            {
                OnDied?.Invoke();
            }
            else
            {
                OnRevived?.Invoke();
            }
            RaiseHpChanged();
        }

        public void Revive(float ratio = 1f)
        {
            if (!CanChangeHealth || !float.IsFinite(ratio) || ratio <= 0f || ratio > 1f)
                return;
            _currentHp = _maxHp * ratio;
            _lastAttacker = null;
            _reviveAllowedAt = 0d;
            _isDead = false;
            RaiseHpChanged();
            UpdateOverflowState();
            PublishLifeState();
        }

        public void RequestRevive(float ratio = 1f)
        {
            if (netIdentity == null) return;
            if (!NetworkServer.active && !NetworkClient.active)
            {
                Revive(ratio);
                return;
            }
            if (isServer)
            {
                TryReviveFromRequest(ratio);
                return;
            }

            if (!isLocalPlayer)
                return;

            CmdRequestRevive(ratio);
        }

        [Command]
        private void CmdRequestRevive(float ratio)
        {
            TryReviveFromRequest(ratio);
        }

        [Server]
        private void TryReviveFromRequest(float ratio)
        {
            bool isInBattle = BattleStateMachine.Instance != null &&
                              BattleStateMachine.Instance.CurrentState == BattleState.InBattle;
            if (!HealthUiLifeRules.CanRequestRevive(IsDead, NetworkTime.time, _reviveAllowedAt, ratio, isInBattle))
                return;
            var playerManager = GetComponent<PlayerManager>();
            if (playerManager == null || !playerManager.ServerTeleportToSpawn())
                return;
            Revive(ratio);
        }

        /// <summary>
        /// 체력을 즉시 최대치로 회복시킵니다. (주로 로비/대기씬 스탯 적용 시 호출)
        /// </summary>
        public void RefillHealth()
        {
            if (!CanChangeHealth || IsDead)
                return;
            _currentHp = _maxHp;
            RaiseHpChanged();
            UpdateOverflowState();
        }

#if UNITY_EDITOR
        private int _pendingEditorValidation;

        protected override void OnValidate()
        {
            base.OnValidate();
            // Validation also runs during scene loading, before Awake and potentially off-thread.
            System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 1);
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || netIdentity == null)
                return;
            if (System.Threading.Interlocked.Exchange(ref _pendingEditorValidation, 0) == 0)
                return;

            // Preserve live Inspector HP edits, applying runtime side effects on the player loop.
            EvaluateDeath();
            RaiseHpChanged();
            UpdateOverflowState();
        }
#endif
    }
}

