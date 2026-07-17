using System;
using System.Collections;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.UI
{
    public interface IPlayerHudView
    {
        void SetHp(float current, float max);
        void SetShield(float shield);
        void SetIdentity(Identity identity);
        void SetSkill(SkillHudState state);
        void SetOverflow(bool isOverflow, float overlapPercent);
        void SetMatchTimer(float seconds);
        void SetCountdown(string text, bool active);
        void SetScore(int points);
        void SetDeathOverlay(bool active, string text = "", Color? textColor = null);
        void SetLoadingOverlay(bool active);
    }

    public enum SkillHudPhase
    {
        Hidden = 0,
        Ready = 1,
        Casting = 2,
        Active = 3,
        Cooldown = 4
    }

    public readonly struct SkillHudState
    {
        public readonly bool Visible;
        public readonly string Name;
        public readonly int SelectedIndex;
        public readonly int SkillCount;
        public readonly SkillHudPhase Phase;
        public readonly float NormalizedFill;
        public readonly float RemainingSeconds;
        public readonly Sprite IconSprite;

        public SkillHudState(
            bool visible,
            string name,
            int selectedIndex,
            int skillCount,
            SkillHudPhase phase,
            float normalizedFill,
            float remainingSeconds,
            Sprite iconSprite = null)
        {
            Visible = visible;
            Name = name;
            SelectedIndex = selectedIndex;
            SkillCount = skillCount;
            Phase = phase;
            NormalizedFill = Mathf.Clamp01(normalizedFill);
            RemainingSeconds = Mathf.Max(0f, remainingSeconds);
            IconSprite = iconSprite;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerHUD : MonoBehaviour
    {
        public static PlayerHUD Instance { get; private set; }
        private static PlayerHUD _globalHud;

        [Header("Sources")]
        [SerializeField] private StatManager _statManager;
        [SerializeField] private MonoBehaviour _healthSource;

        [Header("View")]
        [SerializeField] private MonoBehaviour _view;

        private IPlayerStatusSource _status;
        private IDamageReceiver _damageReceiver;
        private PlayerCombat _combatSource;
        private HealthSystem _shieldSource;
        private IPlayerHudView _hudView;
        private NetworkIdentity _ownerIdentity;
        private bool _isSubscribed;
        private bool _hasExplicitTarget;
        private bool _hasLoadingOverlayState;
        private bool _lastLoadingOverlayActive;
        private float _nextLoadingOverlaySyncTime;
        private const float LoadingOverlaySyncInterval = 0.25f;

        public static void UpdateLocalDeathOverlay(bool active, string text = "", Color? textColor = null)
        {
            bool applied = false;
            var huds = Resources.FindObjectsOfTypeAll<PlayerHUD>();
            foreach (var hud in huds)
            {
                if (hud == null || !hud.gameObject.scene.isLoaded)
                    continue;

                if (!hud.CanApplyLocalDeathOverlay())
                    continue;

                hud.ApplyDeathOverlay(active, text, textColor);
                applied = true;
            }

            if (!applied && Instance != null)
                Instance.UpdateDeathOverlay(active, text, textColor);
        }

        public static bool BindToPlayer(StatManager statManager)
        {
            if (statManager == null)
                return false;

            HealthSystem health = statManager.GetComponent<HealthSystem>();
            if (health == null)
                return false;

            return BindToPlayer(statManager, health, statManager.GetComponent<PlayerCombat>());
        }

        public static bool BindToPlayer(StatManager statManager, HealthSystem health, PlayerCombat combat = null)
        {
            if (statManager == null || health == null)
                return false;

            PlayerHUD hud = statManager.GetComponent<PlayerHUD>();
            if (hud == null || !hud.HasResolvedView())
                hud = FindBindableHud();

            if (hud == null)
                return false;

            hud.SetTarget(statManager, health, combat != null ? combat : statManager.GetComponent<PlayerCombat>());
            return true;
        }

        private void Awake()
        {
            _ownerIdentity = GetComponentInParent<NetworkIdentity>();

            ResolveView();
            if (_ownerIdentity == null && _hudView != null)
                _globalHud = this;

            if (_statManager == null)
                _statManager = GetComponentInParent<StatManager>();

            if (!NetworkClient.active && _ownerIdentity == null)
                InitializeSources();

            if (_ownerIdentity == null)
                Instance = this;
            else if (Instance == null && CanBindThisHud() && !HasGlobalHud())
                Instance = this;
        }

        private void Start()
        {
            StartCoroutine(CoBindLocalHud());
            SetViewActive(ShouldDisplayThisHud());
            SyncLoadingOverlayFromBattleState();
        }

        private void OnEnable()
        {
            TryBindLocalPlayerImmediate();
            if (IsBoundToLocalPlayer() && _hudView != null && _damageReceiver != null)
            {
                _hudView.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
                _hudView.SetShield(_shieldSource != null ? _shieldSource.CurrentShield : 0f);
            }
        }

        private void OnDisable()
        {
            UnsubscribeCurrent();
        }

        private void Update()
        {
            if (IsBoundToLocalPlayer() && ShouldDisplayThisHud())
                Instance = this;

            if (Time.unscaledTime >= _nextLoadingOverlaySyncTime)
            {
                _nextLoadingOverlaySyncTime = Time.unscaledTime + LoadingOverlaySyncInterval;
                SyncLoadingOverlayFromBattleState();
            }
        }

        private void ResolveView()
        {
            _hudView = _view as IPlayerHudView;
            if (_hudView != null)
                return;

            var viewComp = GetComponentInChildren<PlayerHudView>(true);
            _hudView = viewComp as IPlayerHudView;
            if (_view == null && viewComp != null)
                _view = viewComp;
        }

        private bool HasResolvedView()
        {
            ResolveView();
            return _hudView != null;
        }

        public static bool ShowLocalReceivedDamage(float damage, Color color)
        {
            PlayerHUD hud = null;
            if (Instance != null && Instance.HasResolvedView())
                hud = Instance;
            else if (_globalHud != null && _globalHud.HasResolvedView())
                hud = _globalHud;

            return hud != null
                && hud._hudView is PlayerHudView view
                && view.ShowReceivedDamage(damage, color);
        }

        private static PlayerHUD FindBindableHud()
        {
            if (Instance != null && Instance.HasResolvedView())
                return Instance;

            if (_globalHud != null && _globalHud.HasResolvedView())
                return _globalHud;

            PlayerHUD[] huds = Resources.FindObjectsOfTypeAll<PlayerHUD>();
            foreach (PlayerHUD hud in huds)
            {
                if (hud == null || !hud.gameObject.scene.isLoaded)
                    continue;

                if (hud.HasResolvedView())
                    return hud;
            }

            return null;
        }

        private IEnumerator CoBindLocalHud()
        {
            float timeout = 5f;
            while (NetworkClient.active && NetworkClient.localPlayer == null && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (_ownerIdentity != null && !_ownerIdentity.isLocalPlayer && !_hasExplicitTarget)
            {
                UnsubscribeCurrent();
                SetViewActive(false);
                yield break;
            }

            if (NetworkClient.localPlayer != null)
            {
                TryBindLocalPlayerImmediate();

                if (_ownerIdentity != null && HasGlobalHud())
                    yield break;
            }
            else if (!NetworkClient.active)
            {
                InitializeSources();
                SetViewActive(true);
            }
        }

        private void InitializeSources()
        {
            UnsubscribeCurrent();

            _status = _healthSource as IPlayerStatusSource;
            _damageReceiver = _healthSource as IDamageReceiver;

            if (_status == null || _damageReceiver == null)
            {
                var hs = GetComponentInParent<HealthSystem>();
                _status = hs as IPlayerStatusSource;
                _damageReceiver = hs as IDamageReceiver;
            }

            _combatSource = GetComponentInParent<PlayerCombat>();

            SubscribeNew();
        }

        private bool TryBindLocalPlayerImmediate()
        {
            if (!NetworkClient.active || NetworkClient.localPlayer == null)
                return IsBoundToLocalPlayer();

            var localStats = NetworkClient.localPlayer.GetComponent<StatManager>();
            var localHealth = NetworkClient.localPlayer.GetComponent<HealthSystem>();
            var localCombat = NetworkClient.localPlayer.GetComponent<PlayerCombat>();
            if (localStats == null || localHealth == null)
                return false;

            if (_ownerIdentity != null && !_ownerIdentity.isLocalPlayer)
            {
                UnsubscribeCurrent();
                SetViewActive(false);
                return false;
            }

            if (_ownerIdentity != null && HasGlobalHud())
            {
                _globalHud.SetTarget(localStats, localHealth, localCombat);
                UnsubscribeCurrent();
                SetViewActive(false);
                return false;
            }

            if (!IsBoundToLocalPlayer())
                SetTarget(localStats, localHealth, localCombat);

            return IsBoundToLocalPlayer();
        }

        public void SetTarget(StatManager sm, HealthSystem hs)
        {
            SetTarget(sm, hs, hs != null ? hs.GetComponent<PlayerCombat>() : null);
        }

        public void SetTarget(StatManager sm, HealthSystem hs, PlayerCombat combat)
        {
            if (NetworkClient.active && _ownerIdentity != null && !_ownerIdentity.isLocalPlayer)
                return;

            if (NetworkClient.active && NetworkClient.localPlayer != null)
            {
                if (hs == null || hs.transform.root != NetworkClient.localPlayer.transform)
                    return;
            }

            UnsubscribeCurrent();

            _statManager = sm;
            _healthSource = hs;
            _status = hs as IPlayerStatusSource;
            _damageReceiver = hs as IDamageReceiver;
            _shieldSource = hs as HealthSystem;
            _combatSource = combat != null ? combat : ResolveCombatSource(sm, hs);
            _hasExplicitTarget = sm != null || hs != null;

            SubscribeNew();
            SetViewActive(ShouldDisplayThisHud());
            if (ShouldDisplayThisHud())
                Instance = this;

            if (_statManager != null)
                OnIdentityChanged(_statManager.CurrentIdentity);

            if (_damageReceiver != null)
                _hudView?.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
            _hudView?.SetShield(_shieldSource != null ? _shieldSource.CurrentShield : 0f);

            _hudView?.SetOverflow(false, 0f);
            if (_combatSource != null)
                OnSkillHudChanged(_combatSource.GetSkillHudState());
            else
                _hudView?.SetSkill(new SkillHudState(false, string.Empty, 0, 0, SkillHudPhase.Hidden, 0f, 0f));
        }

        private void SubscribeNew()
        {
            if (_isSubscribed || !CanBindThisHud())
                return;

            if (_statManager != null)
            {
                _statManager.IdentityChanged += OnIdentityChanged;
                _statManager.StatsChanged += OnStatsChanged;
            }

            if (_status != null)
            {
                _status.HpChanged += OnHpChanged;
                _status.OverflowChanged += OnOverflowChanged;
            }

            if (_shieldSource != null)
                _shieldSource.ShieldChanged += OnShieldChanged;

            if (_combatSource != null)
                _combatSource.SkillHudChanged += OnSkillHudChanged;

            _isSubscribed = true;
        }

        private void UnsubscribeCurrent()
        {
            if (!_isSubscribed)
                return;

            if (_statManager != null)
            {
                _statManager.IdentityChanged -= OnIdentityChanged;
                _statManager.StatsChanged -= OnStatsChanged;
            }

            if (_status != null)
            {
                _status.HpChanged -= OnHpChanged;
                _status.OverflowChanged -= OnOverflowChanged;
            }

            if (_shieldSource != null)
                _shieldSource.ShieldChanged -= OnShieldChanged;

            if (_combatSource != null)
                _combatSource.SkillHudChanged -= OnSkillHudChanged;

            _isSubscribed = false;
        }

        private bool CanBindThisHud()
        {
            if (_ownerIdentity == null)
                return true;

            if (_hasExplicitTarget)
                return true;

            return !NetworkClient.active || _ownerIdentity.isLocalPlayer;
        }

        private bool HasGlobalHud()
        {
            return _globalHud != null && _globalHud != this && _globalHud._hudView != null;
        }

        private bool ShouldDisplayThisHud()
        {
            if (!CanBindThisHud())
                return false;

            if (_ownerIdentity != null && HasGlobalHud())
                return false;

            return true;
        }

        private bool IsBoundToLocalPlayer()
        {
            if (!NetworkClient.active)
                return true;

            if (NetworkClient.localPlayer == null)
                return _hasExplicitTarget && _damageReceiver != null;

            if (_damageReceiver == null)
                return false;

            if (_damageReceiver is Component component)
                return component.transform.root == NetworkClient.localPlayer.transform;

            return false;
        }

        private static PlayerCombat ResolveCombatSource(StatManager statManager, Component healthSource)
        {
            if (statManager != null && statManager.TryGetComponent(out PlayerCombat combatFromStats))
                return combatFromStats;

            if (healthSource != null && healthSource.TryGetComponent(out PlayerCombat combatFromHealth))
                return combatFromHealth;

            return null;
        }

        private PlayerCombat ResolveCurrentCombatSource()
        {
            if (_combatSource != null)
                return _combatSource;

            PlayerCombat combat = ResolveCombatSource(_statManager, _healthSource as Component);
            if (combat == null)
                return null;

            _combatSource = combat;
            if (_isSubscribed)
                _combatSource.SkillHudChanged += OnSkillHudChanged;

            return _combatSource;
        }

        private bool CanApplyLocalDeathOverlay()
        {
            if (!NetworkClient.active)
                return true;

            if (NetworkClient.localPlayer == null)
                return false;

            var localStats = NetworkClient.localPlayer.GetComponent<StatManager>();
            var localHealth = NetworkClient.localPlayer.GetComponent<HealthSystem>();
            if (localStats == null || localHealth == null)
                return false;

            if (_ownerIdentity != null && !_ownerIdentity.isLocalPlayer)
                return false;

            if (!IsBoundToLocalPlayer())
                SetTarget(localStats, localHealth);

            return IsBoundToLocalPlayer();
        }

        private void SetViewActive(bool active)
        {
            if (_view != null)
            {
                if (_view.gameObject.activeSelf != active)
                    _view.gameObject.SetActive(active);
                return;
            }

            if (_hudView is Component component)
            {
                if (component.gameObject.activeSelf != active)
                    component.gameObject.SetActive(active);
            }
        }

        private void SyncLoadingOverlayFromBattleState()
        {
            if (!IsBoundToLocalPlayer())
            {
                SetLoadingOverlayCached(false);
                return;
            }

            var battleState = BattlePvp.Networking.BattleStateMachine.Instance;
            bool shouldShow = battleState != null &&
                              battleState.IsLoading &&
                              battleState.CurrentState != BattlePvp.Networking.BattleState.InBattle &&
                              battleState.CurrentState != BattlePvp.Networking.BattleState.MatchEnded;

            UpdateLoadingOverlay(shouldShow);
        }

        private void OnHpChanged(float current, float max)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            _hudView.SetHp(current, max);
            _hudView.SetShield(_shieldSource != null ? _shieldSource.CurrentShield : 0f);
        }

        private void OnShieldChanged(float shield)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            _hudView.SetShield(shield);
        }

        private void OnOverflowChanged(bool isOverflow, float overlapPercent)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            _hudView.SetOverflow(isOverflow, overlapPercent);
        }

        private void OnStatsChanged(StatContainer _)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            if (_damageReceiver != null)
            {
                _hudView.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
                _hudView.SetShield(_shieldSource != null ? _shieldSource.CurrentShield : 0f);
            }

            if (_statManager != null)
                _hudView.SetIdentity(_statManager.CurrentIdentity);

            PlayerCombat combat = ResolveCurrentCombatSource();

            if (combat != null)
                _hudView.SetSkill(combat.GetSkillHudState());
        }

        private void OnIdentityChanged(Identity identity)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            _hudView.SetIdentity(identity);

            if (_damageReceiver != null)
            {
                _hudView.SetHp(_damageReceiver.CurrentHp, _damageReceiver.MaxHp);
                _hudView.SetShield(_shieldSource != null ? _shieldSource.CurrentShield : 0f);
            }

            PlayerCombat combat = ResolveCurrentCombatSource();

            if (combat != null)
                _hudView.SetSkill(combat.GetSkillHudState());
        }

        private void OnSkillHudChanged(SkillHudState state)
        {
            if (this == null || _hudView == null || !IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            _hudView.SetSkill(state);
        }

        public void UpdateTimer(float seconds) => _hudView?.SetMatchTimer(seconds);
        public void UpdateCountdown(string text, bool active) => _hudView?.SetCountdown(text, active);
        public void UpdateScore(int points) => _hudView?.SetScore(points);

        public void UpdateDeathOverlay(bool active, string text = "", Color? textColor = null)
        {
            if (_ownerIdentity != null && HasGlobalHud())
            {
                _globalHud.UpdateDeathOverlay(active, text, textColor);
                return;
            }

            TryBindLocalPlayerImmediate();

            if (!IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            if (active)
                SetViewActive(true);

            ApplyDeathOverlay(active, text, textColor);
        }

        private void ApplyDeathOverlay(bool active, string text = "", Color? textColor = null)
        {
            if (active)
                SetViewActive(true);

            _hudView?.SetDeathOverlay(active, text, textColor);

            if (!active)
                SetViewActive(ShouldDisplayThisHud());
        }

        public void UpdateLoadingOverlay(bool active)
        {
            if (!IsBoundToLocalPlayer() || !ShouldDisplayThisHud())
                return;

            string sceneName = SceneManager.GetActiveScene().name;
            bool canShowLoading = sceneName == "Battle";
            SetLoadingOverlayCached(active && canShowLoading);
        }

        private void SetLoadingOverlayCached(bool active)
        {
            if (_hasLoadingOverlayState && _lastLoadingOverlayActive == active)
                return;

            _hasLoadingOverlayState = true;
            _lastLoadingOverlayActive = active;
            _hudView?.SetLoadingOverlay(active);
        }
    }
}
