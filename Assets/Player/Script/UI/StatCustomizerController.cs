using System.Text;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using BattlePvp.Managers;
using BattlePvp.Combat;
using BattlePvp.Networking;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

namespace BattlePvp.UI
{
    /// <summary>
    /// 프리셋 로드/선택 이벤트를 편집 값과 미리보기에 반영하고 명시적인 적용을 요청한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatCustomizerController : MonoBehaviour
    {
        public static StatCustomizerController Instance { get; private set; }
        public static event System.Action InstanceChanged;
        public GameObject ViewRoot => transform.Find("Canvas_Customizer")?.gameObject;
        private NetworkIdentity _ownerIdentity;
        public bool CanOwnLocalUi => _ownerIdentity == null ||
            (NetworkClient.active || NetworkServer.active ? _ownerIdentity.isLocalPlayer :
                _configuredStatManager != null && _configuredStatManager == StatManager.Local);

        private const int TotalInvestedBudget = 30;

        [Header("Target")]
        [SerializeField] private StatManager _statManager;

        [Header("Rows")]
        [SerializeField] private StatSlider _str;
        [SerializeField] private StatSlider _agi;
        [SerializeField] private StatSlider _con;
        [SerializeField] private StatSlider _def;

        [Header("Budget UI")]
        [SerializeField] private TMP_Text _pointsText;
        [SerializeField] private TMP_Text _remainPointsText;

        [Header("Identity Preview")]
        [SerializeField] private Image _identityIcon;
        [SerializeField] private TMP_Text _identityName;
        [SerializeField] private IdentitySpriteSet _spriteSet;
        [SerializeField] private UIIdentityGlitchBinder[] _identityPreviewGlitchBinders;

        [Header("Derived Stats Preview")]
        [SerializeField] private TMP_Text _previewAtkText;
        [SerializeField] private TMP_Text _previewDefText;
        [SerializeField] private TMP_Text _previewMaxHpText;
        [SerializeField] private TMP_Text _previewPeneText;
        [SerializeField] private TMP_Text _previewRegenText;
        [SerializeField] private TMP_Text _previewMoveSpdText;
        [SerializeField] private TMP_Text _previewAtkSpdText;

        [Header("Apply & Restrictions")]
        [SerializeField] private Button _applyButton;
        [SerializeField] private Button[] _presetButtons;
        [SerializeField] private Button _strategistPresetButton;
        [SerializeField] private CanvasGroup _floatingMessageCanvasGroup;
        [SerializeField] private TMP_Text _floatingMessageText;

        private HealthSystem _playerHealth;

        private IdentityCalculator _identityCalculator;
        private StatContainer _baseStats;     
        private StatContainer _virtualStats;  

        private readonly StringBuilder _sb = new StringBuilder(64);
        private readonly Dictionary<Graphic, Color> _presetGraphicDefaultColors = new Dictionary<Graphic, Color>();
        private readonly Dictionary<Transform, StatPreviewPulse> _previewPulses = new Dictionary<Transform, StatPreviewPulse>();
        private Coroutine _previewPulseRoutine;

        private GlobalDataManager _profileData;
        private StatManager _configuredStatManager;
        private UnityAction[] _presetButtonActions;
        private UnityAction _strategistPresetButtonAction;
        private bool _editingStrategistTargetPreset;
        private bool _isApplying;
        private uint _applyGeneration;

        private void Awake()
        {
            _ownerIdentity = GetComponentInParent<NetworkIdentity>(true);
            _identityCalculator = new IdentityCalculator();
            if (_statManager == null) _statManager = GetComponentInParent<StatManager>(true);
            _configuredStatManager = _statManager;
            RefreshLocalOwnership();
            if (_playerHealth == null) _playerHealth = GetComponentInParent<HealthSystem>(true);
            ResolveIdentityPreviewReferences();
            if (_floatingMessageCanvasGroup != null) _floatingMessageCanvasGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            RefreshLocalOwnership();
            StatBalanceConfig.BalanceChanged += RebuildBudgetAndPreview;

            _profileData = GlobalDataManager.Instance;
            if (_profileData != null)
            {
                _profileData.OnSavedStatsUpdated += OnGlobalStatsUpdated;
                _profileData.OnStrategistTargetPresetChanged += OnStrategistPresetUpdated;
            }
            StatManager.LocalChanged += OnLocalPlayerChanged;

            TryFindTarget();
            ResolveIdentityPreviewReferences();

            Hook(_str); Hook(_agi); Hook(_con); Hook(_def);

            if (_applyButton != null)
                _applyButton.onClick.AddListener(Apply);
            HookPresetButtons();
            HookStrategistPresetButton();
            RefreshPresetSelectionVisuals();

            LoadFromSavedStatsOrTarget();
            RebuildBudgetAndPreview();
        }

        private void OnDisable()
        {
            if (Instance == this) { Instance = null; InstanceChanged?.Invoke(); }
            _applyGeneration++;
            SetApplying(false);
            StatBalanceConfig.BalanceChanged -= RebuildBudgetAndPreview;

            StatManager.LocalChanged -= OnLocalPlayerChanged;
            if (_profileData != null)
            {
                _profileData.OnSavedStatsUpdated -= OnGlobalStatsUpdated;
                _profileData.OnStrategistTargetPresetChanged -= OnStrategistPresetUpdated;
            }
            _profileData = null;

            Unhook(_str); Unhook(_agi); Unhook(_con); Unhook(_def);
            if (_applyButton != null) _applyButton.onClick.RemoveListener(Apply);
            UnhookPresetButtons();
            UnhookStrategistPresetButton();

            // [추가] 참조 명시적 초기화 및 코루틴 중단으로 안정성 확보
            StopAllCoroutines();
            _previewPulseRoutine = null;
            foreach (var entry in _previewPulses)
                if (entry.Key != null) entry.Key.localScale = entry.Value.OriginalScale;
            _previewPulses.Clear();
            _statManager = null;
            _playerHealth = null;
        }

        private void OnGlobalStatsUpdated(StatContainer updatedStats)
        {
            if (this == null) return;

            if (gameObject.activeInHierarchy && !_editingStrategistTargetPreset)
            {
                _baseStats = updatedStats;
                _virtualStats = _baseStats;
                RefreshSliderVisuals();
                RebuildBudgetAndPreview();
                RefreshPresetSelectionVisuals();
            }
        }

        private void OnStrategistPresetUpdated(StatContainer stats, bool hasPreset)
        {
            if (!isActiveAndEnabled || !_editingStrategistTargetPreset) return;
            _baseStats = hasPreset ? stats : default;
            _virtualStats = _baseStats;
            RefreshSliderVisuals();
            RebuildBudgetAndPreview();
            RefreshPresetSelectionVisuals();
        }

        private void OnLocalPlayerChanged(StatManager stats)
        {
            RefreshLocalOwnership();
            if (!CanOwnLocalUi) return;
            SetTarget(stats != null ? stats : (!NetworkClient.active ? _configuredStatManager : null));
            if (_profileData == null || !_profileData.HasLoadedPlayerStats) LoadFromSavedStatsOrTarget();
            RebuildBudgetAndPreview();
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; InstanceChanged?.Invoke(); }
        }

        private void RefreshLocalOwnership()
        {
            if (!CanOwnLocalUi)
            {
                if (ViewRoot != null) ViewRoot.SetActive(false);
                if (Instance == this) { Instance = null; InstanceChanged?.Invoke(); }
                return;
            }
            // A player-owned view supersedes a standalone lobby view, regardless of Awake order.
            if (Instance == this || (Instance != null && _ownerIdentity == null)) return;
            if (Instance != null && Instance.ViewRoot != null) Instance.ViewRoot.SetActive(false);
            Instance = this;
            if (ViewRoot != null) ViewRoot.SetActive(false);
            InstanceChanged?.Invoke();
        }

        private void RefreshSliderVisuals()
        {
            if (_str != null) { _str.SetItem(_baseStats.STR.Item); _str.SetInvestedWithoutNotify(_baseStats.STR.Invested); }
            if (_agi != null) { _agi.SetItem(_baseStats.AGI.Item); _agi.SetInvestedWithoutNotify(_baseStats.AGI.Invested); }
            if (_con != null) { _con.SetItem(_baseStats.CON.Item); _con.SetInvestedWithoutNotify(_baseStats.CON.Invested); }
            if (_def != null) { _def.SetItem(_baseStats.DEF.Item); _def.SetInvestedWithoutNotify(_baseStats.DEF.Invested); }
        }

        private void TryFindTarget()
        {
            if (!CanOwnLocalUi) { SetTarget(null); return; }
            StatManager target = NetworkClient.active
                ? (NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<StatManager>() : null)
                : StatManager.Local != null ? StatManager.Local : _configuredStatManager;
            if (target == null && !NetworkClient.active) target = GetComponentInParent<StatManager>();
            SetTarget(target);
        }

        private void SetTarget(StatManager target)
        {
            _statManager = target;
            _playerHealth = target != null ? target.GetComponent<HealthSystem>() : null;
        }

        private void LoadFromTarget()
        {
            _baseStats = _statManager != null ? _statManager.GetStatsCopy() : default;
            _virtualStats = _baseStats;
            RefreshSliderVisuals();
        }

        private bool TryLoadFromSavedStats()
        {
            if (_profileData == null || !_profileData.HasLoadedPlayerStats)
                return false;

            int slotIndex = _profileData.SelectedStatPresetSlot;
            StatContainer saved = _profileData.HasStatPresetSlot(slotIndex)
                ? _profileData.GetStatPresetSlot(slotIndex)
                : default;

            _baseStats = saved;
            _virtualStats = _baseStats;
            RefreshSliderVisuals();
            return true;
        }

        private void LoadFromSavedStatsOrTarget()
        {
            if (_editingStrategistTargetPreset)
            {
                OnStrategistPresetUpdated(_profileData != null ? _profileData.StrategistTargetPreset : default,
                    _profileData != null && _profileData.HasStrategistTargetPreset);
                return;
            }
            if (TryLoadFromSavedStats())
                return;

            LoadFromTarget();
        }

        private void HookPresetButtons()
        {
            ResolvePresetButtons();
            if (_presetButtons == null || _presetButtons.Length == 0)
                return;

            _presetButtonActions = new UnityAction[_presetButtons.Length];
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                Button button = _presetButtons[i];
                if (button == null)
                    continue;

                int slotIndex = i;
                _presetButtonActions[i] = () => SelectPresetSlot(slotIndex);
                button.onClick.AddListener(_presetButtonActions[i]);
                CapturePresetButtonColors(button);
            }
        }

        private void UnhookPresetButtons()
        {
            if (_presetButtons == null || _presetButtonActions == null)
                return;

            int count = Mathf.Min(_presetButtons.Length, _presetButtonActions.Length);
            for (int i = 0; i < count; i++)
            {
                if (_presetButtons[i] != null && _presetButtonActions[i] != null)
                    _presetButtons[i].onClick.RemoveListener(_presetButtonActions[i]);
            }

            _presetButtonActions = null;
        }

        private void ResolvePresetButtons()
        {
            if (_presetButtons != null && _presetButtons.Length > 0)
                return;

            Button[] buttons = GetComponentsInChildren<Button>(true);
            List<Button> presetButtons = new List<Button>();
            for (int i = 1; i <= 9; i++)
            {
                Button button = FindButtonByName(buttons, $"Preset{i}");
                if (button != null)
                    presetButtons.Add(button);
            }

            if (presetButtons.Count > 0)
                _presetButtons = presetButtons.ToArray();
        }

        private void HookStrategistPresetButton()
        {
            if (_strategistPresetButton == null)
                _strategistPresetButton = FindButtonByName(GetComponentsInChildren<Button>(true), "Strategist_Preset");

            if (_strategistPresetButton == null)
                return;

            _strategistPresetButtonAction = SelectStrategistTargetPreset;
            _strategistPresetButton.onClick.AddListener(_strategistPresetButtonAction);
            CapturePresetButtonColors(_strategistPresetButton);
        }

        private void UnhookStrategistPresetButton()
        {
            if (_strategistPresetButton != null && _strategistPresetButtonAction != null)
                _strategistPresetButton.onClick.RemoveListener(_strategistPresetButtonAction);

            _strategistPresetButtonAction = null;
        }

        private static Button FindButtonByName(Button[] buttons, string objectName)
        {
            if (buttons == null)
                return null;

            foreach (Button button in buttons)
            {
                if (button != null && button.name == objectName)
                    return button;
            }

            return null;
        }

        private void SelectPresetSlot(int slotIndex)
        {
            if (_profileData == null)
                return;

            _editingStrategistTargetPreset = false;
            _profileData.SelectStatPresetSlot(slotIndex);
            LoadFromSavedStatsOrTarget();
            RebuildBudgetAndPreview();
            TryFindTarget();
            RefreshPresetSelectionVisuals();
        }

        private void SelectStrategistTargetPreset()
        {
            _editingStrategistTargetPreset = true;

            if (_profileData != null && _profileData.HasStrategistTargetPreset)
                _baseStats = _profileData.StrategistTargetPreset;
            else
                _baseStats = default;

            _virtualStats = _baseStats;
            RefreshSliderVisuals();
            RebuildBudgetAndPreview();
            RefreshPresetSelectionVisuals();
        }

        private void CapturePresetButtonColors(Button button)
        {
            if (button == null)
                return;

            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
            {
                if (graphic != null && !_presetGraphicDefaultColors.ContainsKey(graphic))
                    _presetGraphicDefaultColors.Add(graphic, graphic.color);
            }
        }

        private void RefreshPresetSelectionVisuals()
        {
            if (_presetButtons != null)
            {
                for (int i = 0; i < _presetButtons.Length; i++)
                    SetPresetButtonSelected(_presetButtons[i], !_editingStrategistTargetPreset && _profileData != null && _profileData.SelectedStatPresetSlot == i);
            }

            SetPresetButtonSelected(_strategistPresetButton, _editingStrategistTargetPreset);
        }

        private void SetPresetButtonSelected(Button button, bool selected)
        {
            if (button == null)
                return;

            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
            {
                if (graphic == null)
                    continue;

                if (!_presetGraphicDefaultColors.TryGetValue(graphic, out Color original))
                {
                    original = graphic.color;
                    _presetGraphicDefaultColors.Add(graphic, original);
                }

                graphic.color = selected
                    ? new Color(original.r * 0.45f, original.g * 0.45f, original.b * 0.45f, original.a)
                    : original;
            }
        }

        private void Hook(StatSlider s) { if (s != null) s.InvestedChanged += OnInvestedChanged; }
        private void Unhook(StatSlider s) { if (s != null) s.InvestedChanged -= OnInvestedChanged; }

        private void OnInvestedChanged(StatSlider changed, float _)
        {
            if (changed != null)
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                bool isBattleScene = sceneName == "Battle";
                
                if (isBattleScene)
                {
                    int maxAllowed = TotalInvestedBudget - 1;
                    if (Mathf.RoundToInt(changed.Invested) > maxAllowed)
                    {
                        changed.SetInvestedWithoutNotify(maxAllowed);
                        ShowFloatingMessage($"배틀 중 몰빵형 변환 불가 (최대 {maxAllowed} 제한)");
                    }
                }
            }


            int total = GetTotalInvested();
            if (total > TotalInvestedBudget && changed != null)
            {
                int overflow = total - TotalInvestedBudget;
                // 정수 연산을 보장하기 위해 (int)로 캐스팅 후 계산
                int next = Mathf.Max(0, Mathf.RoundToInt(changed.Invested) - overflow);
                changed.SetInvestedWithoutNotify(next);
            }
            
            SyncVirtualFromSliders();
            RebuildBudgetAndPreview();
        }

        private int GetTotalInvested()
        {
            int s = _str != null ? Mathf.RoundToInt(_str.Invested) : 0;
            int a = _agi != null ? Mathf.RoundToInt(_agi.Invested) : 0;
            int c = _con != null ? Mathf.RoundToInt(_con.Invested) : 0;
            int d = _def != null ? Mathf.RoundToInt(_def.Invested) : 0;
            return s + a + c + d;
        }

        public int GetRemainPoints() => gameObject.activeInHierarchy ? TotalInvestedBudget - GetTotalInvested() : 0;

        private void SyncVirtualFromSliders()
        {
            _virtualStats = _baseStats;
            if (_str != null) _virtualStats.STR.Invested = (int)_str.Invested;
            if (_agi != null) _virtualStats.AGI.Invested = (int)_agi.Invested;
            if (_con != null) _virtualStats.CON.Invested = (int)_con.Invested;
            if (_def != null) _virtualStats.DEF.Invested = (int)_def.Invested;
        }

        private void RebuildBudgetAndPreview()
        {
            ResolveIdentityPreviewReferences();
            int used = GetTotalInvested();
            int remain = TotalInvestedBudget - used;
            if (_pointsText != null) _pointsText.text = $"{used} / {TotalInvestedBudget}";
            if (_remainPointsText != null) _remainPointsText.text = $"잔여 스탯: {remain}";

            Identity id = _identityCalculator.ResolveIdentity(_virtualStats, out _);
            ApplyIdentityPreviewGlitch(id, _virtualStats);
            if (_identityName != null) {
                _sb.Clear();
                _sb.Append(id.PrimaryStat); _sb.Append(' '); _sb.Append(id.Type.ToString().ToUpperInvariant());
                _identityName.text = _sb.ToString();
            }
            if (_identityIcon != null && _spriteSet != null)
            {
                Sprite sprite = _spriteSet.Resolve(id);
                _identityIcon.sprite = sprite;
                _identityIcon.enabled = sprite != null;
                Color color = _identityIcon.color;
                if (color.a <= 0.01f)
                {
                    color.a = 1f;
                    _identityIcon.color = color;
                }
            }

            if (_statManager != null)
            {
                _statManager.CalculatePreviewStats(_virtualStats, out float atk, out float def, out float maxHp, out float pene, out float regen, out float moveSpd, out float atkSpd);
                UpdatePreviewText(_previewAtkText, $"공격력 : {atk:F0}");
                UpdatePreviewText(_previewDefText, $"방어력 : {def:F1}%");
                UpdatePreviewText(_previewMaxHpText, $"최대 체력 : {maxHp:F0}");
                UpdatePreviewText(_previewPeneText, $"물리 관통력 : {pene:F1}%");
                UpdatePreviewText(_previewRegenText, $"재생력 : {regen:F1}/s");
                UpdatePreviewText(_previewMoveSpdText, $"이동속도 : {moveSpd:F2}");
                UpdatePreviewText(_previewAtkSpdText, $"공격속도 : {atkSpd:F2}");
            }
            if (_applyButton != null) _applyButton.interactable = remain >= 0 && !_isApplying;
        }

        private void UpdatePreviewText(TMP_Text textRef, string newValue)
        {
            if (textRef == null) return;
            if (textRef.text != newValue)
            {
                textRef.text = newValue;
                Transform target = textRef.transform;
                if (!_previewPulses.TryGetValue(target, out StatPreviewPulse pulse))
                {
                    pulse = new StatPreviewPulse(target.localScale);
                    _previewPulses.Add(target, pulse);
                }
                pulse.Restart();
                target.localScale = pulse.Scale;
                if (_previewPulseRoutine == null)
                    _previewPulseRoutine = StartCoroutine(AnimatePreviewPulses());
            }
        }

        private void ResolveIdentityPreviewReferences()
        {
            if (_identityIcon != null && _identityName != null)
            {
                ResolveIdentityPreviewGlitchBinders();
                if (!_identityIcon.gameObject.activeSelf)
                    _identityIcon.gameObject.SetActive(true);
                if (!_identityName.gameObject.activeSelf)
                    _identityName.gameObject.SetActive(true);
                return;
            }

            Transform preview = FindChildRecursive(transform, "Identity_Preview");
            if (preview != null && !preview.gameObject.activeSelf)
                preview.gameObject.SetActive(true);

            Transform root = preview != null ? preview : transform;
            if (_identityIcon == null)
                _identityIcon = root.GetComponentInChildren<Image>(true);
            if (_identityName == null)
                _identityName = root.GetComponentInChildren<TMP_Text>(true);

            if (_identityIcon != null && !_identityIcon.gameObject.activeSelf)
                _identityIcon.gameObject.SetActive(true);
            if (_identityName != null && !_identityName.gameObject.activeSelf)
                _identityName.gameObject.SetActive(true);

            if ((_identityPreviewGlitchBinders == null || _identityPreviewGlitchBinders.Length == 0) && preview != null)
                _identityPreviewGlitchBinders = preview.GetComponentsInChildren<UIIdentityGlitchBinder>(true);
        }

        private void ResolveIdentityPreviewGlitchBinders()
        {
            if (_identityPreviewGlitchBinders != null && _identityPreviewGlitchBinders.Length > 0)
                return;

            Transform preview = FindChildRecursive(transform, "Identity_Preview");
            if (preview != null)
                _identityPreviewGlitchBinders = preview.GetComponentsInChildren<UIIdentityGlitchBinder>(true);
        }

        private void ApplyIdentityPreviewGlitch(Identity identity, StatContainer stats)
        {
            if (_identityPreviewGlitchBinders == null || _identityPreviewGlitchBinders.Length == 0)
                return;

            foreach (UIIdentityGlitchBinder binder in _identityPreviewGlitchBinders)
            {
                if (binder != null)
                    binder.SetIdentity(identity, stats);
            }
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null)
                return null;

            if (root.name == targetName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private IEnumerator AnimatePreviewPulses()
        {
            yield return null;
            while (true)
            {
                bool active = false;
                foreach (var entry in _previewPulses)
                {
                    if (entry.Key == null || !entry.Value.IsActive) continue;
                    entry.Value.Advance(Time.unscaledDeltaTime);
                    entry.Key.localScale = entry.Value.Scale;
                    active |= entry.Value.IsActive;
                }
                if (!active) break;
                yield return null;
            }
            _previewPulseRoutine = null;
        }

        private Coroutine _floatingMessageRoutine;
        public void ShowFloatingMessage(string msg)
        {
            if (_floatingMessageCanvasGroup == null || _floatingMessageText == null) return;
            _floatingMessageText.text = msg;
            if (_floatingMessageRoutine != null) StopCoroutine(_floatingMessageRoutine);
            _floatingMessageRoutine = StartCoroutine(CoShowFloatingMessage());
        }

        private IEnumerator CoShowFloatingMessage()
        {
            _floatingMessageCanvasGroup.alpha = 1f;
            yield return new WaitForSeconds(1f);
            float elapsed = 0f;
            while(elapsed < 1f)
            {
                elapsed += Time.deltaTime;
                _floatingMessageCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed);
                yield return null;
            }
        }

        private void Apply()
        {
            if (_isApplying || Instance != this || !CanOwnLocalUi) return;
            // [추가] 적용 순간에 한 번 더 타겟 유효성을 검사하고 시도합니다.
            if (_editingStrategistTargetPreset)
            {
                if (GetRemainPoints() != 0)
                {
                    ShowFloatingMessage("모든 스텟을 투자하십시오");
                    return;
                }

                if (!GlobalDataManager.IsStrategistPreset(_virtualStats))
                {
                    ShowFloatingMessage("전략가 전환 프리셋은 전략가형만 설정할 수 있습니다.");
                    return;
                }

                ApplyPreset(_virtualStats, true);
                return;
            }

            if (_statManager == null) TryFindTarget();
            if (_statManager == null) 
            {
                ShowFloatingMessage("대상을 동기화할 수 없습니다. 잠시 후 시도하세요.");
                return;
            }

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isBattleScene = sceneName == "Battle";
            bool isDead = _playerHealth != null && _playerHealth.IsDead;

            // [강화] 실제 전투 씬에서만 몰빵형 전환을 금지합니다.
            if (isBattleScene || isDead)
            {
                if (StatManager.IsMonostat(_virtualStats))
                {
                    ShowFloatingMessage("몰빵형 변환은 불가능합니다.");
                    LoadFromSavedStatsOrTarget();
                    RebuildBudgetAndPreview();
                    return;
                }
            }

            StatContainer currentStats = _baseStats;
            currentStats.STR.Invested = _virtualStats.STR.Invested;
            currentStats.AGI.Invested = _virtualStats.AGI.Invested;
            currentStats.CON.Invested = _virtualStats.CON.Invested;
            currentStats.DEF.Invested = _virtualStats.DEF.Invested;

            ApplyPreset(currentStats, false);
        }

        private void ApplyPreset(StatContainer stats, bool strategistTarget)
        {
            SetApplying(true);
            uint generation = ++_applyGeneration;
            StatPresetApplication.Apply(_statManager, _profileData, PlayFabBattleManager.Instance,
                stats, strategistTarget, (succeeded, error) =>
                {
                    if (this == null || !isActiveAndEnabled || generation != _applyGeneration) return;
                    SetApplying(false);
                    if (!succeeded)
                    {
                        ShowFloatingMessage(error);
                        return;
                    }
                    _baseStats = stats;
                    _virtualStats = stats;
                    RefreshSliderVisuals();
                    RebuildBudgetAndPreview();
                    if (LobbyUIManager.Instance != null) LobbyUIManager.Instance.SetCustomizerActive(false);
                    else gameObject.SetActive(false);
                });
        }

        private void SetApplying(bool applying)
        {
            _isApplying = applying;
            _str?.SetInteractable(!applying);
            _agi?.SetInteractable(!applying);
            _con?.SetInteractable(!applying);
            _def?.SetInteractable(!applying);
            if (_presetButtons != null)
                foreach (Button button in _presetButtons)
                    if (button != null) button.interactable = !applying;
            if (_strategistPresetButton != null) _strategistPresetButton.interactable = !applying;
            if (_applyButton != null) _applyButton.interactable = !applying && GetRemainPoints() >= 0;
        }
    }
}
