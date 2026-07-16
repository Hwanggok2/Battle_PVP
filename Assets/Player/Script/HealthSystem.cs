using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class HealthSystem : Mirror.NetworkBehaviour, IDamageReceiverWithContext, IPlayerStatusSource
    {
        [Header("Networking")]
        [SyncVar] public bool isInvincible = false;
        [Header("References")]
        [SerializeField] private StatManager _statManager;
        [SerializeField] private Animator _animator; // [추가] 캐릭터 애니메이터

        [Header("Runtime")]
        [SyncVar(hook = nameof(OnHpChangedInternal))]
        [SerializeField] private float _currentHp = 100f;

        private void OnHpChangedInternal(float oldHp, float newHp)
        {
            RaiseHpChanged();
            UpdateOverflowState();
            EvaluateDeath(); // [추가] 네트워크 동기화 시에도 사망 판정
        }

        public float CurrentHp => _currentHp;
        public float MaxHp => _maxHp;
        public float CurrentRegen => _currentRegen;
        public float CurrentShield => _currentShield;
        public bool IsDead { get; private set; }

        public event Action<float, float> HpChanged;
        public event Action<float> ShieldChanged;
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
        private static readonly Color DealtDamagePopupColor = new Color(1f, 0.12f, 0.12f, 1f);
        private static readonly Color PoisonPopupColor = new Color(0.25f, 1f, 0.25f, 1f);
        private static readonly Color ThornsPopupColor = new Color(0.25f, 0.65f, 1f, 1f);
        private const float ReceivedDamagePopupFontSize = 5f;
        private const float PoisonDealtPopupFontSizeDelta = -16f;
        private const float AroundCharacterPopupRadius = 0.65f;
        private const float AroundCharacterPopupHeight = 1.35f;
        private static readonly Vector3 ThornsPopupOffset = new Vector3(0.65f, 1.25f, 0f);
        private static readonly Dictionary<uint, float> PredictedPopupExpiryByVictim = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, int> PredictedPopupCountByVictim = new Dictionary<uint, int>();
        private const float PredictedPopupSuppressSeconds = 5f;

        private void Awake()
        {
            if (_statManager == null)
                _statManager = GetComponent<StatManager>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();

            _damageCalculator = new DamageCalculator();
            _strategistRules = new StrategistRules();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // HP 갱신은 StatManager.CmdUpdateStats → StatsChanged 이벤트 체인으로 처리됩니다.
            // 씬 로드 직후 별도 처리 불필요.
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            // 로비 혹은 비네트워크 개체라면 초기 HP를 실시간 최대치로 강제 동기화 (100 고정 방지)
            bool isNetworkActive = Mirror.NetworkServer.active || Mirror.NetworkClient.active;
            
            // [수정] 로비의 경우 무조건 초기화 시 최대 체력으로 가득 채웁니다.
            bool isLobbyScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";
            bool shouldForceRefill = !isNetworkActive || isLobbyScene || _currentHp <= 1000f;
            
            RefreshFromStats(keepCurrentHpFlat: !shouldForceRefill);
            if (shouldForceRefill) SetCurrentHp(_maxHp);

            if (_statManager != null)
                _statManager.StatsChanged += OnStatsChanged;

            EnsureRegenRoutine();
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_statManager != null)
                _statManager.StatsChanged -= OnStatsChanged;

            StopRegenRoutine();
            StopOverflowRoutine();
        }

        private void OnStatsChanged(StatContainer newStats)
        {
            if (this == null) return;
            
            bool isStrategist = _statManager != null && _statManager.CurrentIdentity.Type == IdentityType.Strategist;
            float oldHp = _currentHp;
            float oldMax = _maxHp;

            RefreshFromStats(keepCurrentHpFlat: true);

            // 스탯 변경 시 체력 수치 보정 (로비 더미 플레이어 포함)
            bool isNetworkActive = Mirror.NetworkServer.active || Mirror.NetworkClient.active;
            if (isServer || isLocalPlayer || !isNetworkActive)
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
            EvaluateDeath(); // [추가] 강제 체력 설정 시에도 사망 판정
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;

            if (NetworkClient.active && !NetworkServer.active)
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
            if (amount <= 0f || durationSeconds <= 0f || IsDead)
                return;

            if (_shieldRoutine != null)
            {
                StopCoroutine(_shieldRoutine);
                _shieldRoutine = null;
            }

            float existingShield = _currentShield > 0.5f ? _currentShield : 0f;
            _currentShield = existingShield + amount;
            _shieldExpiresAt = NetworkTime.time + durationSeconds;
            RaiseShieldChanged();
            _shieldRoutine = StartCoroutine(CoDecayShield());
        }

        public void SetSkillInvulnerable(float durationSeconds)
        {
            _skillInvulnerableUntil = Math.Max(_skillInvulnerableUntil, NetworkTime.time + Math.Max(0f, durationSeconds));
        }

        public void SetTauntDefense(float durationSeconds, float incomingDamageMultiplier, float reflectMultiplier, float reflectHealthCapRatio)
        {
            _tauntDefenseUntil = NetworkTime.time + Math.Max(0f, durationSeconds);
            _tauntIncomingDamageMultiplier = Mathf.Clamp01(incomingDamageMultiplier);
            _tauntReflectMultiplier = Mathf.Max(0f, reflectMultiplier);
            _tauntReflectHealthCapRatio = Mathf.Clamp01(reflectHealthCapRatio);
        }

        private IEnumerator CoDecayShield()
        {
            while (_currentShield > 0f && NetworkTime.time < _shieldExpiresAt)
            {
                float remaining = Mathf.Max(0.001f, (float)(_shieldExpiresAt - NetworkTime.time));
                float deltaSeconds = Mathf.Max(Time.deltaTime, _shieldSyncIntervalSeconds);
                _currentShield = Mathf.Max(0f, _currentShield - ((_currentShield / remaining) * deltaSeconds));
                RaiseShieldChanged();
                yield return new WaitForSeconds(Mathf.Max(0.01f, _shieldSyncIntervalSeconds));
            }

            _currentShield = 0f;
            _shieldExpiresAt = 0d;
            RaiseShieldChanged();
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

        public void ApplyDamageWithPopupSource(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker, Vector3 hitPosition, DamageSource popupSource)
        {
            if (NetworkClient.active && !NetworkServer.active)
                return;

            if (isInvincible || NetworkTime.time < _skillInvulnerableUntil || amount <= 0f)
                return;

            bool tauntDefenseActive = NetworkTime.time < _tauntDefenseUntil;
            if (tauntDefenseActive)
                amount *= _tauntIncomingDamageMultiplier;

            if (attacker != null)
            {
                _lastAttacker = attacker;
            }

            float absorbedByShield = Mathf.Min(_currentShield, amount);
            _currentShield -= absorbedByShield;
            if (absorbedByShield > 0f)
                RaiseShieldChanged();
            float hpDamage = amount - absorbedByShield;
            float next = _currentHp - hpDamage;
            
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Battle_waiting")
            {
                if (next < 1f) next = 1f;
            }

            _currentHp = next < 0f ? 0f : next;
            ShowDamagePopup(hitPosition, amount, popupSource, attacker);

            EvaluateDeath(); // [공통 로직으로 교체]

            RaiseHpChanged();

            // Thorns 처리(재반사 금지)
            // - Monostat DEF일 때만
            // - Physical 피해일 때만
            // - attacker 정보가 있어야 반사 가능
            if (source == DamageSource.Physical && attacker != null && attackerAttackPower > 0f && IsMonostatDef())
            {
                float thorns = _damageCalculator.PredictThornsReflectDamage(attackerAttackPower, attacker.MaxHp);
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
                            ctx.ApplyDamage(thorns, DamageSource.Thorns, attackerAttackPower: 0f, attacker: null, GetThornsPopupPosition(attackerMb));
                        else
                            attacker.ApplyDamage(thorns, DamageSource.Thorns, GetThornsPopupPosition(attackerMb));
                    }
                }
            }

            UpdateOverflowState();
        }

        private void ShowDamagePopup(Vector3 hitPosition, float amount, DamageSource source, IDamageReceiver attacker)
        {
            Vector3 popupPosition = ResolveDamagePopupPosition(hitPosition, source);
            uint attackerNetId = GetDamageReceiverNetId(attacker);
            uint victimNetId = netIdentity != null ? netIdentity.netId : 0;

            if (isServer)
            {
                RpcShowDamagePopup(popupPosition, amount, source, attackerNetId, victimNetId);
                return;
            }

            CreateDamagePopupLocal(popupPosition, amount, source, attackerNetId, victimNetId);
        }

        [ClientRpc]
        private void RpcShowDamagePopup(Vector3 position, float amount, DamageSource source, uint attackerNetId, uint victimNetId)
        {
            CreateDamagePopupLocal(position, amount, source, attackerNetId, victimNetId);
        }

        private void CreateDamagePopupLocal(Vector3 position, float amount, DamageSource source, uint attackerNetId, uint victimNetId)
        {
            DamagePopupManager popupManager = DamagePopupManager.Instance;
            if (popupManager == null)
                return;

            Color popupColor = GetDamagePopupColor(source);
            bool localVictim = IsLocalPlayerNetId(victimNetId) || isLocalPlayer;
            bool localAttacker = attackerNetId != 0
                && IsLocalPlayerNetId(attackerNetId);

            if (localAttacker && source == DamageSource.Physical && ConsumePredictedPopup(victimNetId))
                return;

            if (localVictim)
            {
                if (source == DamageSource.Physical)
                    popupManager.CreateReceivedDamagePopup(amount, popupColor, ReceivedDamagePopupFontSize, transform.position + Vector3.up);
                else
                    popupManager.CreatePopup(GetAroundCharacterPopupPosition(), amount, false, popupColor, ReceivedDamagePopupFontSize);
                return;
            }

            if (localAttacker)
            {
                if (source == DamageSource.Poison)
                    popupManager.CreatePopupWithFontDelta(position, amount, false, popupColor, PoisonDealtPopupFontSizeDelta);
                else
                    popupManager.CreatePopup(position, amount, false, popupColor);
                return;
            }

            if (source == DamageSource.Poison)
                popupManager.CreatePopupWithFontDelta(position, amount, false, popupColor, PoisonDealtPopupFontSizeDelta);
            else
                popupManager.CreatePopup(position, amount, false, popupColor);
        }

        public void ShowPredictedPhysicalDamagePopup(Vector3 position, float amount, uint attackerNetId)
        {
            if (amount <= 0f || attackerNetId == 0 || !IsLocalPlayerNetId(attackerNetId))
                return;

            uint victimNetId = netIdentity != null ? netIdentity.netId : 0;
            CreateDamagePopupLocal(position, amount, DamageSource.Physical, attackerNetId, victimNetId);
            if (victimNetId != 0)
            {
                PredictedPopupExpiryByVictim[victimNetId] = Time.unscaledTime + PredictedPopupSuppressSeconds;
                PredictedPopupCountByVictim.TryGetValue(victimNetId, out int count);
                PredictedPopupCountByVictim[victimNetId] = count + 1;
            }
        }

        private static bool ConsumePredictedPopup(uint victimNetId)
        {
            if (victimNetId == 0 ||
                !PredictedPopupExpiryByVictim.TryGetValue(victimNetId, out float expiry) ||
                !PredictedPopupCountByVictim.TryGetValue(victimNetId, out int count))
                return false;

            if (Time.unscaledTime > expiry)
            {
                PredictedPopupExpiryByVictim.Remove(victimNetId);
                PredictedPopupCountByVictim.Remove(victimNetId);
                return false;
            }

            if (count <= 1)
            {
                PredictedPopupExpiryByVictim.Remove(victimNetId);
                PredictedPopupCountByVictim.Remove(victimNetId);
            }
            else
            {
                PredictedPopupCountByVictim[victimNetId] = count - 1;
            }

            return true;
        }

        private Vector3 ResolveDamagePopupPosition(Vector3 hitPosition, DamageSource source)
        {
            if (source == DamageSource.Poison || source == DamageSource.Thorns)
                return GetAroundCharacterPopupPosition();

            return hitPosition == Vector3.zero ? transform.position + Vector3.up : hitPosition;
        }

        private Vector3 GetAroundCharacterPopupPosition()
        {
            Vector2 random = UnityEngine.Random.insideUnitCircle * AroundCharacterPopupRadius;
            return transform.position + transform.right * random.x + transform.forward * random.y + Vector3.up * AroundCharacterPopupHeight;
        }

        private static Color GetDamagePopupColor(DamageSource source)
        {
            return source switch
            {
                DamageSource.Poison => PoisonPopupColor,
                DamageSource.Thorns => ThornsPopupColor,
                _ => DealtDamagePopupColor
            };
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

        private static Vector3 GetThornsPopupPosition(MonoBehaviour attackerMb)
        {
            Transform attackerTransform = attackerMb.transform;
            return attackerTransform.position
                + (attackerTransform.right * ThornsPopupOffset.x)
                + (Vector3.up * ThornsPopupOffset.y)
                + (attackerTransform.forward * ThornsPopupOffset.z);
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
            if (_overflowRoutineRunning)
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
                if (_maxHp <= 0f || _currentHp <= _maxHp || !IsStrategist())
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
            if (_regenRoutine != null) return;
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
            float lastFullHealTime = 0f;

            while (true)
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // "Battle_wait" 또는 "Battle_waiting" 모두 대응하도록 수정
                bool isWaitingScene = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
                bool isPreMatch = (BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState == BattleState.PreMatch);
                
                float effectiveRegen = _currentRegen;
                bool isLobby = sceneName.Contains("Lobby");
                // [수정] 초강력 재생은 오직 Battle_waiting 또는 Battle_wait 씬에서만 작동합니다.
                bool isWaitingSceneOnly = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");

                if (isWaitingSceneOnly || isPreMatch)
                {
                    // 대기실에서는 초당 최대 체력의 50%씩 고속 회복 (사용자 요청)
                    effectiveRegen = Mathf.Max(effectiveRegen, _maxHp * 0.5f);
                }

                if (effectiveRegen > 0f && _currentHp < _maxHp)
                {
                    // 일반 재생 로직 (로비도 로컬 환경이므로 허용)
                    if (isServer || isLobby || isWaitingSceneOnly)
                    {
                        float next = _currentHp + (effectiveRegen * Time.deltaTime);
                        _currentHp = Mathf.Min(next, _maxHp);
                        
                        // UI 즉시 반영을 위한 강제 호출 (로비/대기실)
                        if (isLobby || isWaitingSceneOnly) RaiseHpChanged();
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
            if (_currentHp <= 0f && !IsDead)
            {
                IsDead = true;
                if (_animator != null)
                {
                    _animator.SetTrigger("Die");
                    Debug.Log($"[HealthSystem:{gameObject.name}] Die trigger set on Animator.");
                }
                
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

                var combat = GetComponent<PlayerCombat>();
                if (combat != null)
                    combat.CancelCurrentAttack();

                OnDied?.Invoke();
                if (isServer)
                {
                    var playerManager = GetComponent<PlayerManager>();
                    if (playerManager != null)
                        playerManager.NotifyDeathFromServer();

                    if (connectionToClient != null)
                        TargetForceDeath(connectionToClient);
                }
                Debug.Log($"[HealthSystem:{gameObject.name}] IsDead set to true.");
            }
            else if (_currentHp > 0f && IsDead)
            {
                // [선택 사항] 체력이 다시 생겼을 때 자동으로 IsDead를 해제할 수도 있으나, 
                // 보통 Revive()를 호출하므로 여기서는 명시적으로 로그만 남깁니다.
                // IsDead = false; 
            }
        }

        [TargetRpc]
        private void TargetForceDeath(NetworkConnection target)
        {
            if (_currentHp > 0f)
                _currentHp = 0f;

            if (!IsDead)
            {
                EvaluateDeath();
            }
            else
            {
                OnDied?.Invoke();
            }

            RaiseHpChanged();
        }

        public void Revive(float ratio = 1f)
        {
            IsDead = false;
            _currentHp = _maxHp * Mathf.Clamp01(ratio);
            RaiseHpChanged();
            UpdateOverflowState();
            OnRevived?.Invoke();

            var playerManager = GetComponent<PlayerManager>();
            if (playerManager != null)
            {
                playerManager.PlayReviveVisual();
                if (isServer)
                    playerManager.RpcPlayReviveVisual();
            }

            if (isServer && connectionToClient != null)
                TargetForceRevive(connectionToClient, _currentHp);
        }

        public void RequestRevive(float ratio = 1f)
        {
            if (isServer)
            {
                Revive(ratio);
                return;
            }

            if (!isLocalPlayer)
                return;

            Revive(ratio);
            CmdRequestRevive(ratio);
        }

        [Command]
        private void CmdRequestRevive(float ratio)
        {
            Revive(ratio);
        }

        [TargetRpc]
        private void TargetForceRevive(NetworkConnection target, float hp)
        {
            IsDead = false;
            _currentHp = hp;
            RaiseHpChanged();
            UpdateOverflowState();
            OnRevived?.Invoke();

            var playerManager = GetComponent<PlayerManager>();
            if (playerManager != null)
                playerManager.PlayReviveVisual();
        }

        /// <summary>
        /// 체력을 즉시 최대치로 회복시킵니다. (주로 로비/대기씬 스탯 적용 시 호출)
        /// </summary>
        public void RefillHealth()
        {
            if (isServer)
            {
                _currentHp = _maxHp;
            }
            else if (isLocalPlayer)
            {
                _currentHp = _maxHp;
                RaiseHpChanged();
            }
            Debug.Log($"[HealthSystem] Health refilled to {_maxHp} (Server={isServer}, Local={isLocalPlayer})");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 인스펙터에서 수동으로 체력을 깎았을 때 UI 및 사망 애니메이션이 즉시 반영되도록 합니다.
            if (Application.isPlaying)
            {
                EvaluateDeath();
                RaiseHpChanged();
                UpdateOverflowState();
            }
        }
#endif
    }
}

