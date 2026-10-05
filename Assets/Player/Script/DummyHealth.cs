using UnityEngine;
using BattlePvp.Combat;
using BattlePvp.UI;
using BattlePvp.Stats;
using System.Collections;
using Mirror;
using TMPro;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 훈련용 허수아비의 체력을 관리하는 스크립트입니다.
    /// 플레이어와 동일한 StatManager를 통해 방어력 및 간접 수치(피해 감소 등)를 적용받습니다.
    /// </summary>
    [RequireComponent(typeof(StatManager),typeof(NetworkIdentity))]
    public class DummyHealth : NetworkBehaviour, IDamageReceiverWithResult
    {
        private static readonly Color PoisonPopupColor = new Color(0.25f, 1f, 0.25f, 1f);
        private const float PoisonPopupFontSizeDelta = -16f;

        [Header("Stat Configuration")]
        [SerializeField] private DummyStatData _statData;
        [SerializeField] private bool _requireBodyPartHitboxes;
        public bool RequiresBodyPartHitboxes => _requireBodyPartHitboxes;

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
        private readonly DamageDpsWindow _dps=new();
        [SyncVar] private float _currentDps;
        [SyncVar] private double _stunnedUntil, _vulnerableUntil;
        [SyncVar(hook=nameof(OnPositionChanged))] private Vector3 _networkPosition;
        private double _nextDps;
        private Coroutine _pull;
        private bool Authority => NetworkServer.active ? isServer : !NetworkClient.active;
        private double Now => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        public float CurrentDps => _currentDps;
        public bool IsStunned => Now < _stunnedUntil;
        [SyncVar] private bool _hasDebuff;
        public bool HasDebuff => _hasDebuff;
        internal bool HasControlDebuff => IsStunned || Now < _vulnerableUntil || _pull != null;
        internal void SetDebuffPresentation(bool active) { if (Authority) _hasDebuff = active; }

        public void ApplyStun(float seconds, bool vulnerable)
        {
            if (!Authority || !isActiveAndEnabled || !float.IsFinite(seconds) || seconds <= 0) return;
            _stunnedUntil = System.Math.Max(_stunnedUntil, Now + seconds);
            if (vulnerable) _vulnerableUntil = System.Math.Max(_vulnerableUntil, Now + seconds);
        }

        private void Awake()
        {
            _statManager = GetComponent<StatManager>();
            
            if (_statData != null)
            {
                ApplyStatData();
            }
            if(Application.isPlaying)
            {
                if (GetComponent<StunIndicator>() == null) gameObject.AddComponent<StunIndicator>();
                if (GetComponent<DebuffIndicator>() == null) gameObject.AddComponent<DebuffIndicator>();
                var source=GetComponentInChildren<TMP_Text>();
                var label=new GameObject("Training DPS",typeof(TextMeshPro),typeof(TrainingDpsLabel)).GetComponent<TextMeshPro>();
                label.transform.SetParent(transform,false); label.transform.localPosition=Vector3.up*3.35f;
                label.font=source!=null ? source.font : TMP_Settings.defaultFontAsset;
                label.fontSize=2.5f; label.color=new Color(.4f,1,.9f); label.alignment=TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta=new Vector2(4,.6f);
                label.GetComponent<TrainingDpsLabel>().Bind(this);
            }
        }
        public override void OnStartServer() { base.OnStartServer(); _networkPosition=transform.position; }
        private void OnPositionChanged(Vector3 previous,Vector3 current) { if(!isServer) transform.position=current; }
        private void Update()
        {
            if(!Authority || Now<_nextDps) return;
            _nextDps=Now+.1; _currentDps=_dps.Sample(Now);
        }

        public void PullTo(Vector3 destination,float seconds)
        {
            if(!Authority || !isActiveAndEnabled || !CombatValidation.IsFinite(destination) || !float.IsFinite(seconds) || seconds<=0) return;
            if(_pull!=null) StopCoroutine(_pull);
            _pull=StartCoroutine(Pull(destination,seconds));
        }
        private IEnumerator Pull(Vector3 destination,float seconds)
        {
            Vector3 delta=destination-transform.position; delta.y=0;
            Vector3 velocity=delta/seconds;
            var colliders=GetComponentsInChildren<Collider>();
            Bounds bounds=new Bounds(transform.position+Vector3.up, new Vector3(.8f,1.8f,.5f));
            if(colliders.Length>0) { bounds=colliders[0].bounds; foreach(var collider in colliders) if(collider.enabled) bounds.Encapsulate(collider.bounds); }
            Vector3 center=bounds.center-transform.position;
            Vector3 half=Vector3.Max(bounds.extents-Vector3.one*.02f,Vector3.one*.01f);
            for(float elapsed=0;elapsed<seconds;)
            {
                float step=Mathf.Min(Time.deltaTime,seconds-elapsed); elapsed+=step;
                Vector3 move=velocity*step; float distance=move.magnitude;
                // Sweep the whole target so a hook cannot pull it through a wall.
                foreach(var hit in Physics.BoxCastAll(transform.position+center,half,move.normalized,Quaternion.identity,distance+.02f,~0,QueryTriggerInteraction.Ignore))
                    if(!hit.collider.transform.IsChildOf(transform)) distance=Mathf.Min(distance,Mathf.Max(0,hit.distance-.02f));
                transform.position+=move.normalized*distance;
                _networkPosition=transform.position;
                yield return null;
            }
            _pull=null;
        }

        private void OnEnable()
        {
            if (_statManager != null)
            {
                _statManager.StatsChanged += OnStatsChanged;
                RefreshInspectorStats();
                if (_currentHp <= 0f) _currentHp = _maxHp;
            }
        }

        private void OnDisable()
        {
            if (_statManager != null)
                _statManager.StatsChanged -= OnStatsChanged;
            if(_pull!=null) StopCoroutine(_pull);
            _pull=null; _dps.Clear();
            if(Authority) { _currentDps=0; _stunnedUntil=_vulnerableUntil=0; }
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
            ApplyDamage(new DamageRequest(amount, source, attackerAttackPower, attacker, hitPosition));
        }

        public DamageResult ApplyDamage(DamageRequest request)
        {
            float amount = request.Amount;
            DamageSource source = request.Source;
            Vector3 hitPosition = request.HitPosition;
            if (!Authority || !float.IsFinite(amount) || amount <= 0f || !CombatValidation.IsFinite(hitPosition))
                return default;

            if (source != DamageSource.Fixed && Now < _vulnerableUntil)
                amount *= ExpandedSkillController.Value(JobSkillKind.Bash,"IncomingMultiplier",2);

            // 실제 체력 차감
            float hpBeforeDamage = _currentHp;
            _currentHp = Mathf.Clamp(_currentHp - amount, 0f, _maxHp);
            DamageResult result = new DamageResult(true, Mathf.Max(0f, hpBeforeDamage - _currentHp), 0f, _currentHp <= 0f);
            _dps.Record(Now,result.HpDamage); _currentDps=_dps.Sample(Now);
            
            // 데미지 팝업을 피격 지점에 띄웁니다.
            Vector3 popupPosition = hitPosition == Vector3.zero ? transform.position + Vector3.up : hitPosition;
            if(NetworkServer.active) RpcShowHit(popupPosition,amount,request.PopupSource);
            else ShowHit(popupPosition,amount,request.PopupSource);
            CombatHitFeedback.PlayStatusDamageForAttacker(source, request.Attacker);

            // Training targets refill immediately without discarding the damage window.
            if (_currentHp <= 0f) _currentHp = _maxHp;
            return result;
        }
        [ClientRpc] private void RpcShowHit(Vector3 position,float amount,DamageSource source) => ShowHit(position,amount,source);
        private void ShowHit(Vector3 popupPosition,float amount,DamageSource source)
        {
            HitImpactVfx.PlayFor(transform, popupPosition, source);
            if (DamagePopupManager.Instance != null)
            {
                if (source == DamageSource.Poison)
                    DamagePopupManager.Instance.CreatePopupWithFontDelta(popupPosition, amount, false, PoisonPopupColor, PoisonPopupFontSizeDelta);
                else
                    DamagePopupManager.Instance.CreatePopup(popupPosition, amount);
            }
        }
    }
}
