using System;
using BattlePvp.Combat;
using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>
    /// HUD 렌더링을 위한 "뷰" 인터페이스.
    /// 실제 UI 구현(UGUI/TMP/UITK)은 이 인터페이스를 구현해 교체 가능하도록 설계합니다.
    /// </summary>
    public interface IPlayerHudView
    {
        /// <summary>HP 바/텍스트 등 갱신</summary>
        void SetHp(float current, float max);

        /// <summary>아이덴티티 표시(타입/주력 스탯)</summary>
        void SetIdentity(Identity identity);

        void SetOverflow(bool isOverflow, float overlapPercent);

        /// <summary>매치 타이머 업데이트</summary>
        void SetMatchTimer(float seconds);

        /// <summary>카운트다운 텍스트 표시</summary>
        void SetCountdown(string text, bool active);

        /// <summary>킬 점수 업데이트</summary>
        void SetScore(int points);

        /// <summary>플레이어 사망 패널 제어</summary>
        void SetDeathOverlay(bool active, string text = "");
    }

    /// <summary>
    /// StatManager/HealthSystem 이벤트를 구독해 UI를 이벤트 기반으로 갱신하는 HUD 컨트롤러.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHUD : MonoBehaviour
    {
        public static PlayerHUD Instance { get; private set; }

        [Header("Sources")]
        [SerializeField] private StatManager _statManager;
        [SerializeField] private MonoBehaviour _healthSource; // IPlayerStatusSource + IDamageReceiver

        [Header("View")]
        [SerializeField] private MonoBehaviour _view; // IPlayerHudView

        private IPlayerStatusSource _status;
        private IDamageReceiver _damageReceiver;
        private IPlayerHudView _hudView;

        private void Awake()
        {
            if (Instance == null) Instance = this;

            // Inspector에서 명시적으로 할당되지 않았을 경우 부모나 자신에게서 찾기 시도
            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();

            InitializeSources();

            _hudView = _view as IPlayerHudView;
            if (_hudView == null)
            {
                var viewComp = GetComponentInChildren<PlayerHudView>(true);
                _hudView = viewComp as IPlayerHudView;
            }
        }

        private void InitializeSources()
        {
            // [수정] 이미 구독 중이라면 해제 후 재연결 (중복 방지)
            UnsubscribeCurrent();

            _status = _healthSource as IPlayerStatusSource;
            _damageReceiver = _healthSource as IDamageReceiver;

            if (_status == null || _damageReceiver == null)
            {
                var hs = GetComponentInParent<HealthSystem>();
                _status = hs as IPlayerStatusSource;
                _damageReceiver = hs as IDamageReceiver;
            }
            
            SubscribeNew();
        }

        /// <summary>
        /// 외부(더미 캐릭터 등)에서 이 HUD가 추적할 대상을 강제로 지정합니다.
        /// </summary>
        public void SetTarget(StatManager sm, HealthSystem hs)
        {
            UnsubscribeCurrent();
            
            _statManager = sm;
            _healthSource = hs;
            
            _status = hs as IPlayerStatusSource;
            _damageReceiver = hs as IDamageReceiver;
            
            SubscribeNew();
            
            // 즉시 반영
            if (_statManager != null) OnIdentityChanged(_statManager.CurrentIdentity);
            if (_damageReceiver != null) _hudView?.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
            if (_status != null) OnOverflowChanged(_status is IPlayerStatusSource s && s != null ? false : false, 0f); // 리셋
        }

        private void SubscribeNew()
        {
            if (_statManager != null) _statManager.IdentityChanged += OnIdentityChanged;
            if (_status != null)
            {
                _status.HpChanged += OnHpChanged;
                _status.OverflowChanged += OnOverflowChanged;
            }
        }

        private void UnsubscribeCurrent()
        {
            if (_statManager != null) _statManager.IdentityChanged -= OnIdentityChanged;
            if (_status != null)
            {
                _status.HpChanged -= OnHpChanged;
                _status.OverflowChanged -= OnOverflowChanged;
            }
        }

        private void OnEnable()
        {
            if (_hudView != null && _damageReceiver != null)
            {
                _hudView.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
            }
        }

        private void OnDisable()
        {
            UnsubscribeCurrent();
        }

        private void Update()
        {
            // 최적화를 위해 Update 루프를 제거했습니다.
            // 대신 HealthSystem의 HpChanged 이벤트를 통해 실시간 갱신을 수행합니다.
        }

        private void OnHpChanged(float current, float max)
        {
            if (this == null || _hudView == null) return;
            _hudView.SetHp(current, max);
        }

        private void OnOverflowChanged(bool isOverflow, float overlapPercent)
        {
            if (this == null || _hudView == null) return;
            _hudView.SetOverflow(isOverflow, overlapPercent);
        }

        private void OnIdentityChanged(Identity identity)
        {
            if (this == null || _hudView == null) return;
            _hudView.SetIdentity(identity);
        }

        #region [Match UI Bridge]

        /// <summary>
        /// 외부(BattleStateMachine 등)에서 타이머 정보를 주입합니다.
        /// </summary>
        public void UpdateTimer(float seconds) => _hudView?.SetMatchTimer(seconds);

        /// <summary>
        /// 카운트다운 상태를 업데이트합니다.
        /// </summary>
        public void UpdateCountdown(string text, bool active) => _hudView?.SetCountdown(text, active);

        /// <summary>
        /// 자신의 점수를 업데이트합니다.
        /// </summary>
        public void UpdateScore(int points) => _hudView?.SetScore(points);

        /// <summary>
        /// 사망 패널을 띄우거나 닫습니다.
        /// </summary>
        public void UpdateDeathOverlay(bool active, string text = "") => _hudView?.SetDeathOverlay(active, text);

        #endregion
    }
}

