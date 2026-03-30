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
    }

    /// <summary>
    /// StatManager/HealthSystem 이벤트를 구독해 UI를 이벤트 기반으로 갱신하는 HUD 컨트롤러.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHUD : MonoBehaviour
    {
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
            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();

            _status = _healthSource as IPlayerStatusSource;
            _damageReceiver = _healthSource as IDamageReceiver;
            _hudView = _view as IPlayerHudView;
        }

        private void OnEnable()
        {
            if (_statManager != null)
            {
                _statManager.IdentityChanged += OnIdentityChanged;
                // 초기 1회 반영
                OnIdentityChanged(_statManager.CurrentIdentity);
            }

            if (_status != null)
            {
                _status.HpChanged += OnHpChanged;
                _status.OverflowChanged += OnOverflowChanged;
            }

            if (_damageReceiver != null)
            {
                // 초기 HP 반영 (Update 없이)
                _hudView?.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
            }
        }

        private void OnDisable()
        {
            if (_statManager != null)
                _statManager.IdentityChanged -= OnIdentityChanged;

            if (_healthSource != null)
            {
                if (_status != null)
                {
                    _status.HpChanged -= OnHpChanged;
                    _status.OverflowChanged -= OnOverflowChanged;
                }
            }
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

        #endregion
    }
}

