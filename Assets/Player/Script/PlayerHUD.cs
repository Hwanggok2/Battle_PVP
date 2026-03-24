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

        /// <summary>
        /// Glitch Overflow 표시:
        /// - isOverflow: overflow 여부
        /// - overlapPercent: (CurrentHp - MaxHp)/MaxHp 를 0..1로 정규화
        /// </summary>
        void SetOverflow(bool isOverflow, float overlapPercent);
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

            if (_status != null)
            {
                _status.HpChanged -= OnHpChanged;
                _status.OverflowChanged -= OnOverflowChanged;
            }
        }

        private void OnHpChanged(float current, float max)
        {
            _hudView?.SetHp(current, max);
        }

        private void OnOverflowChanged(bool isOverflow, float overlapPercent)
        {
            _hudView?.SetOverflow(isOverflow, overlapPercent);
        }

        private void OnIdentityChanged(Identity identity)
        {
            _hudView?.SetIdentity(identity);
        }
    }
}

