using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.Managers;
using BattlePvp.Logic;
using Mirror;
using System.Text.RegularExpressions;
using System.Collections;
using TMPro;

namespace BattlePvp.UI
{
    /// <summary>
    /// Core Task 2: UI State Management
    /// 로비 버튼(Battle, Stat Setting)에 따른 가시성 조절 및 데이터 플로우 연동을 담당합니다.
    /// </summary>
    public sealed class LobbyUIManager : MonoBehaviour
    {
        public static LobbyUIManager Instance { get; private set; }

        [Header("Hierarchy UI Objects")]
        [SerializeField] private GameObject _lobby_UI;        // Lobby_UI 오브젝트 (Battle, Stat 버튼 부모)
        [SerializeField] private GameObject _room_UI;         // Room_UI 패널 (이미지상 Room)
        [SerializeField] private GameObject _canvas_Customizer; // Canvas_Customizer

        [Header("Buttons")]
        [SerializeField] private Button _battleButton;        // 'Battle' 버튼 (Room UI 토글)
        [SerializeField] private Button _statSettingButton;   // 'Stat Setting' 버튼 (이미지상 '스텟설정')
        [SerializeField] private Button _createRoomButton;    // '방 만들기' 버튼
        [SerializeField] private Button _joinRoomButton;      // '참여하기' 버튼
        [SerializeField] private Button _saveRoomButton;        // '방 설정 저장(생성) 버튼

        [Header("Room Selection (Internal)")]
        [SerializeField] private string _selectedRoomId = "Global_PvP_Room_1"; // 현재 선택된 방 ID (임시 기본값)

        [Header("Room Setting UI")]
        [SerializeField] private GameObject _roomSettingPanel;   // 방 설정 팝업 패널
        [SerializeField] private TMPro.TMP_InputField _roomNameInput; // 방 제목 입력창
        [SerializeField] private Button _saveRoomButtonComp;        // 방 설정 저장(생성) 버튼 (중복 선언 방지: _saveRoomButton과 동일)

        private GameObject _battlePanelCached;
        private StatManager _localStatManager;
        private PlayFabBattleManager _roomService;
        private BattlePvp.Combat.HealthSystem _localHealthSystem;
        private TextMeshProUGUI _roomFlowStatusText;
        private TextMeshProUGUI _latencyText;
        private Coroutine _latencyDisplayRoutine;
        public bool HasOpenInputPanel => isActiveAndEnabled &&
            ((_canvas_Customizer != null && _canvas_Customizer.activeInHierarchy) ||
             (_room_UI != null && _room_UI.activeInHierarchy) ||
             (_roomSettingPanel != null && _roomSettingPanel.activeInHierarchy));

        private static readonly Color GoodLatencyColor = new Color(0.25f, 0.9f, 0.35f, 1f);
        private static readonly Color FairLatencyColor = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color PoorLatencyColor = new Color(1f, 0.3f, 0.25f, 1f);

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            if (_lobby_UI == null)
                _lobby_UI = FindLoadedSceneObject("Lobby_UI");
            
            if (_lobby_UI != null)
            {
                var bp = _lobby_UI.transform.Find("Battle_Panel");
                if (bp == null) bp = _lobby_UI.transform.GetComponentInChildren<Transform>(true).Find("Battle_Panel");
                if (bp != null) _battlePanelCached = bp.gameObject;

                Button[] allButtons = _lobby_UI.GetComponentsInChildren<Button>(true);
                foreach (var b in allButtons)
                {
                    if (b.name.Equals("Battle")) _battleButton = b;
                    if (b.name.Equals("Stat")) _statSettingButton = b;
                }
            }
            
            FindCanvasCustomizer();
            CloseStartupPanels();
            RefreshVisibility();
            UpgradeHangulLegacyText();
            EnsureLobbyButtonTextVisible();
            EnsureLatencyText();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            if (_battleButton != null) _battleButton.onClick.AddListener(OnBattleButtonClicked);
            if (_statSettingButton != null) _statSettingButton.onClick.AddListener(OnStatSettingButtonClicked);
            if (_createRoomButton != null) _createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
            if (_joinRoomButton != null) _joinRoomButton.onClick.AddListener(OnJoinRoomButtonClicked);
            if (_saveRoomButton != null) _saveRoomButton.onClick.AddListener(OnSaveRoomButtonClicked);
            if (_saveRoomButtonComp != null && _saveRoomButtonComp != _saveRoomButton) _saveRoomButtonComp.onClick.AddListener(OnSaveRoomButtonClicked);
            PlayFabBattleManager.InstanceChanged += BindRoomService;
            BindRoomService(PlayFabBattleManager.Instance);
            StatManager.LocalChanged += BindLocalPlayer;
            StatCustomizerController.InstanceChanged += FindCanvasCustomizer;
            BindLocalPlayer(StatManager.Local);
            FindCanvasCustomizer();
            RefreshVisibility();

            if (_latencyDisplayRoutine != null) StopCoroutine(_latencyDisplayRoutine);
            _latencyDisplayRoutine = StartCoroutine(CoUpdateLatencyDisplay());
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_battleButton != null) _battleButton.onClick.RemoveListener(OnBattleButtonClicked);
            if (_statSettingButton != null) _statSettingButton.onClick.RemoveListener(OnStatSettingButtonClicked);
            if (_createRoomButton != null) _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);
            if (_joinRoomButton != null) _joinRoomButton.onClick.RemoveListener(OnJoinRoomButtonClicked);
            if (_saveRoomButton != null) _saveRoomButton.onClick.RemoveListener(OnSaveRoomButtonClicked);
            if (_saveRoomButtonComp != null && _saveRoomButtonComp != _saveRoomButton) _saveRoomButtonComp.onClick.RemoveListener(OnSaveRoomButtonClicked);
            PlayFabBattleManager.InstanceChanged -= BindRoomService;
            BindRoomService(null);
            StatManager.LocalChanged -= BindLocalPlayer;
            StatCustomizerController.InstanceChanged -= FindCanvasCustomizer;
            UnsubscribeLocalPlayer();

            // [최적화] 모든 루틴 정지
            StopAllCoroutines();
            _latencyDisplayRoutine = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BindRoomService(PlayFabBattleManager service)
        {
            if (ReferenceEquals(_roomService, service)) return;
            if (_roomService != null) _roomService.OnRoomFlowStateChanged -= OnRoomFlowStateChanged;
            _roomService = service;
            if (_roomService == null)
            {
                OnRoomFlowStateChanged(string.Empty, false);
                return;
            }
            _roomService.OnRoomFlowStateChanged += OnRoomFlowStateChanged;
            OnRoomFlowStateChanged(_roomService.LastRoomNotice, false);
        }

        private void EnsureLatencyText()
        {
            if (_latencyText != null || _lobby_UI == null)
                return;

            Transform existing = _lobby_UI.transform.Find("NetworkLatency");
            if (existing != null)
                _latencyText = existing.GetComponent<TextMeshProUGUI>();

            if (_latencyText == null)
            {
                var latencyObject = new GameObject(
                    "NetworkLatency",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                latencyObject.transform.SetParent(_lobby_UI.transform, false);
                _latencyText = latencyObject.GetComponent<TextMeshProUGUI>();
            }

            RectTransform rect = _latencyText.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-4f, -114f);
            rect.sizeDelta = new Vector2(190f, 62f);

            TextMeshProUGUI buttonLabel = _battleButton != null
                ? _battleButton.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            if (buttonLabel != null && buttonLabel.font != null)
                _latencyText.font = buttonLabel.font;

            _latencyText.fontSize = 16f;
            _latencyText.alignment = TextAlignmentOptions.TopRight;
            _latencyText.raycastTarget = false;
            _latencyText.text = string.Empty;
        }

        private IEnumerator CoUpdateLatencyDisplay()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (isActiveAndEnabled)
            {
                EnsureLatencyText();
                UpdateLatencyText();
                yield return wait;
            }

            _latencyDisplayRoutine = null;
        }

        private void UpdateLatencyText()
        {
            if (_latencyText == null)
                return;

            bool connected = NetworkClient.active && NetworkClient.isConnected;
            _latencyText.gameObject.SetActive(connected);
            if (!connected)
                return;

            if (NetworkServer.active)
            {
                double highestRemoteRtt = 0d;
                foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                {
                    if (connection == null || connection is LocalConnectionToClient)
                        continue;

                    highestRemoteRtt = System.Math.Max(highestRemoteRtt, connection.rtt);
                }

                if (highestRemoteRtt <= 0d)
                {
                    _latencyText.text = AppendRelayRegion("HOST");
                    _latencyText.color = GoodLatencyColor;
                    return;
                }

                float hostRttMs = (float)(highestRemoteRtt * 1000d);
                _latencyText.text = AppendRelayRegion($"CLIENT RTT {hostRttMs:F0} ms");
                _latencyText.color = ResolveLatencyColor(hostRttMs);
                return;
            }

            float rttMs = (float)(NetworkTime.rtt * 1000d);
            float jitterMs = (float)(System.Math.Sqrt(System.Math.Max(0d, NetworkTime.rttVariance)) * 1000d);
            _latencyText.text = AppendRelayRegion($"RTT {rttMs:F0} ms\nJitter {jitterMs:F0} ms");
            _latencyText.color = ResolveLatencyColor(rttMs);
        }

        private static string AppendRelayRegion(string latencyText)
        {
            if (Transport.active is not UnityRelayTransport relay || string.IsNullOrWhiteSpace(relay.LastRelayRegion))
                return latencyText;

            string regionLabel = string.IsNullOrWhiteSpace(relay.LastRelayRegionLabel)
                ? relay.LastRelayRegion
                : relay.LastRelayRegionLabel;
            return $"{latencyText}\nRelay {regionLabel}";
        }

        private static Color ResolveLatencyColor(float rttMs)
        {
            if (rttMs <= 50f)
                return GoodLatencyColor;
            if (rttMs <= 100f)
                return FairLatencyColor;
            return PoorLatencyColor;
        }

        // Update() 제거 (이벤트 기반으로 전환)

        private void FindCanvasCustomizer()
        {
            if (this == null) return;

            var owner = StatCustomizerController.Instance;
            GameObject next = owner != null && owner.CanOwnLocalUi ? owner.ViewRoot : null;
            if (_canvas_Customizer == next) return;
            if (_canvas_Customizer != null) _canvas_Customizer.SetActive(false);
            _canvas_Customizer = next;
            if (_canvas_Customizer != null) _canvas_Customizer.SetActive(false);
            GameInputController.RefreshCursorState();
        }

        private void CloseStartupPanels()
        {
            JobGuidePanel.CloseIfOpen();
            if (_room_UI != null) _room_UI.SetActive(false);
            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
            SetCustomizerActive(false);
        }

        private static GameObject FindLoadedSceneObject(string objectName)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == objectName) return candidate.gameObject;
            }
            return null;
        }

        public void RefreshVisibility()
        {
            if (this == null) return;
            EnsureLobbyButtonTextVisible();
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isLobby = sceneName.Contains("Lobby") || !sceneName.Contains("Battle"); 
            bool isBattleWaiting = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
            bool isBattle = sceneName.Equals("Battle") || (sceneName.Contains("Battle") && !sceneName.Contains("wait"));

            if (isLobby || isBattleWaiting)
            {
                // Lobby 씬에는 입력 매니저가 없어도 이전 전투의 커서 잠금을 해제한다.
                GameInputController.RefreshCursorState();
                if (_lobby_UI != null) _lobby_UI.SetActive(true);
                if (_battlePanelCached != null) _battlePanelCached.SetActive(true);
            }
            // Battle 씬에서는 Lobby_UI를 비활성화하지 않음 — 버튼 단위로만 가시성 조절

            bool isMonostat = IsCurrentlyMonostat();

            if (isLobby)
            {
                if (_battleButton != null) { _battleButton.gameObject.SetActive(true); _battleButton.interactable = true; }
                if (_statSettingButton != null) { _statSettingButton.gameObject.SetActive(true); _statSettingButton.interactable = true; }
                return;
            }
            else if (isBattleWaiting)
            {
                if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                if (_statSettingButton != null) { _statSettingButton.gameObject.SetActive(true); _statSettingButton.interactable = true; }
                return;
            }

            if (isBattle)
            {
                bool isDead = _localHealthSystem != null && _localHealthSystem.IsDead;

                if (isDead)
                {
                    if (_lobby_UI != null) _lobby_UI.SetActive(true);
                    // 사망 시 Stat 버튼만 활성화 — 플레이어가 직접 눌러서 창을 열도록 합니다.
                    if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(!isMonostat);
                }
                else
                {
                    SetCustomizerActive(false);
                    if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(false);
                }
            }
        }

        private void EnsureLobbyButtonTextVisible()
        {
            EnsureButtonTextVisible(_battleButton);
            EnsureButtonTextVisible(_statSettingButton);
        }

        private static void EnsureButtonTextVisible(Button button)
        {
            if (button == null)
                return;

            // WebGL can retain a transparent/white TMP material variant from the prefab.
            // Keep the existing label and force a high-contrast vertex color at runtime.
            TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI label in labels)
            {
                if (label == null)
                    continue;

                label.color = new Color(0.84f, 0.92f, 0.97f, 1f);
                label.canvasRenderer.SetAlpha(1f);
            }
        }

        private void UpgradeHangulLegacyText()
        {
            if (_lobby_UI == null)
                return;

            UnityEngine.UI.Text[] legacyLabels = _lobby_UI.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            foreach (UnityEngine.UI.Text legacy in legacyLabels)
            {
                if (legacy == null || !ContainsHangul(legacy.text))
                    continue;

                TextMeshProUGUI label = legacy.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null)
                {
                    var labelObject = new GameObject(
                        "TMP Label",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(TextMeshProUGUI));
                    labelObject.transform.SetParent(legacy.transform, false);

                    RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;

                    label = labelObject.GetComponent<TextMeshProUGUI>();
                }

                label.text = legacy.text;
                label.font = TMP_Settings.defaultFontAsset;
                label.fontSize = legacy.fontSize;
                label.color = legacy.color;
                label.alignment = ConvertAlignment(legacy.alignment);
                label.enableAutoSizing = legacy.resizeTextForBestFit;
                label.fontSizeMin = legacy.resizeTextMinSize;
                label.fontSizeMax = legacy.resizeTextMaxSize;
                label.textWrappingMode = legacy.horizontalOverflow == HorizontalWrapMode.Wrap
                    ? TextWrappingModes.Normal
                    : TextWrappingModes.NoWrap;
                label.overflowMode = legacy.verticalOverflow == VerticalWrapMode.Truncate
                    ? TextOverflowModes.Truncate
                    : TextOverflowModes.Overflow;
                label.raycastTarget = legacy.raycastTarget;
                label.canvasRenderer.SetAlpha(1f);
                legacy.enabled = false;
            }
        }

        private static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char character in value)
            {
                if (character >= '\uAC00' && character <= '\uD7A3')
                    return true;
            }

            return false;
        }

        private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
                TextAnchor.UpperCenter => TextAlignmentOptions.Top,
                TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
                TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
                TextAnchor.MiddleRight => TextAlignmentOptions.Right,
                TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
                TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
                TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
                _ => TextAlignmentOptions.Center
            };
        }

        private void OnRoomFlowStateChanged(string message, bool isBusy)
        {
            UpdateRoomButtonsInteractable(!isBusy);

            if (_roomFlowStatusText == null && !string.IsNullOrEmpty(message))
                _roomFlowStatusText = CreateRoomFlowStatusText();

            if (_roomFlowStatusText == null)
                return;

            _roomFlowStatusText.text = message ?? string.Empty;
            _roomFlowStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message) &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby");
        }

        private TextMeshProUGUI CreateRoomFlowStatusText()
        {
            Transform parent = _room_UI != null ? _room_UI.transform : transform;
            var statusObject = new GameObject("RoomFlowStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            statusObject.transform.SetParent(parent, false);
            statusObject.transform.SetAsLastSibling();

            RectTransform rect = statusObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 68f);
            rect.sizeDelta = new Vector2(800f, 40f);

            TextMeshProUGUI label = statusObject.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 16f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private void BindLocalPlayer(StatManager stats)
        {
            if (ReferenceEquals(_localStatManager, stats)) return;
            UnsubscribeLocalPlayer();
            _localStatManager = stats;
            if (_localStatManager != null)
            {
                _localStatManager.StatsChanged += OnLocalStatsChanged;
                _localHealthSystem = _localStatManager.GetComponent<BattlePvp.Combat.HealthSystem>();
                if (_localHealthSystem != null)
                {
                    _localHealthSystem.OnDied += OnLocalPlayerDied;
                    _localHealthSystem.OnRevived += OnLocalPlayerRevived;
                }
                FindCanvasCustomizer();
            }
            RefreshVisibility();
        }

        private void UnsubscribeLocalPlayer()
        {
            if (_localStatManager != null) _localStatManager.StatsChanged -= OnLocalStatsChanged;
            if (_localHealthSystem != null)
            {
                _localHealthSystem.OnDied -= OnLocalPlayerDied;
                _localHealthSystem.OnRevived -= OnLocalPlayerRevived;
            }
            _localStatManager = null;
            _localHealthSystem = null;
        }

        private void OnLocalPlayerDied() => RefreshVisibility();
        private void OnLocalPlayerRevived()
        {
            SetCustomizerActive(false);
            RefreshVisibility();
        }

        private void OnLocalStatsChanged(StatContainer _) => RefreshVisibility();

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (this == null) return;
            FindCanvasCustomizer();
            CloseStartupPanels();
            BindRoomService(PlayFabBattleManager.Instance);
            BindLocalPlayer(StatManager.Local);
            RefreshVisibility();
        }

        private bool IsCurrentlyMonostat()
        {
            IdentityType currentType = IdentityType.Polymath;
            if (_localStatManager != null)
                currentType = _localStatManager.CurrentIdentity.Type;
            else if (BattlePvp.Managers.GlobalDataManager.Instance != null)
            {
                Identity id = new IdentityCalculator().ResolveIdentity(BattlePvp.Managers.GlobalDataManager.Instance.SavedStats, out _);
                currentType = id.Type;
            }
            return currentType == IdentityType.Monostat;
        }

        private void OnBattleButtonClicked()
        {
            if (_room_UI == null)
                return;

            bool nextActive = !_room_UI.activeSelf;
            _room_UI.SetActive(nextActive);

            if (nextActive)
            {
                var roomList = _room_UI.GetComponentInChildren<RoomListManager>(true);
                if (roomList == null)
                    roomList = FindFirstObjectByType<RoomListManager>(FindObjectsInactive.Include);

                roomList?.RefreshList();
            }
            GameInputController.RefreshCursorState();
        }

        public void OpenRoomInfo()
        {
            if (_room_UI == null) return;
            CharacterInfoController.CloseOpenPanel();
            SetCustomizerActive(false);
            _room_UI.SetActive(true);
            GameInputController.RefreshCursorState();
        }

        public bool CloseInputPanels()
        {
            if (!HasOpenInputPanel) return false;
            CloseStartupPanels();
            GameInputController.RefreshCursorState();
            return true;
        }

        private void OnCreateRoomButtonClicked()
        {
            if (!CanStartRoomFlow())
                return;
            if (_roomSettingPanel != null)
            {
                // List/status labels can be created after this popup. Keep them behind it on every open.
                _roomSettingPanel.transform.SetAsLastSibling();
                var background = _roomSettingPanel.GetComponent<Image>();
                if (background != null)
                {
                    Color color = background.color;
                    color.a = 1f;
                    background.color = color;
                }
                _roomSettingPanel.SetActive(true);
                if (_roomNameInput != null) _roomNameInput.text = "";
            }
        }

        private void OnSaveRoomButtonClicked()
        {
            if (_roomNameInput == null || string.IsNullOrEmpty(_roomNameInput.text)) return;
            string roomName = _roomNameInput.text.Trim();
            if (PlayFabBattleManager.Instance != null)
            {
                PlayFabBattleManager.Instance.CreateRoom(roomName);
                if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
                if (_lobby_UI != null) _lobby_UI.SetActive(true);
                if (_room_UI != null) _room_UI.SetActive(true);
            }
        }

        private void OnJoinRoomButtonClicked()
        {
            if (!CanStartRoomFlow())
                return;
            if (PlayFabBattleManager.Instance != null && !string.IsNullOrEmpty(_selectedRoomId))
            {
                if (PlayFabBattleManager.Instance.IsListedPrivate(_selectedRoomId)) RoomPasswordPrompt.Open(_selectedRoomId, transform);
                else PlayFabBattleManager.Instance.JoinRoom(_selectedRoomId);
            }
        }

        private bool CanStartRoomFlow()
        {
            GlobalDataManager globalData = GlobalDataManager.Instance;
            if (globalData == null)
                return true;

            globalData.EnsurePlayerStatsLoadedForCurrentScene();

            if (!globalData.HasLoadedPlayerStats || globalData.IsPlayerStatsLoadInFlight)
            {
                ShowRoomValidationMessage("스텟 정보를 불러오는 중입니다. 잠시 후 다시 시도하십시오");
                return false;
            }

            if (!globalData.HasCompleteSavedStats())
            {
                ShowRoomValidationMessage("모든 스텟을 투자하십시오");
                return false;
            }

            if (!globalData.HasStrategistTargetPreset)
            {
                // An unconfigured swap uses the server's 30-point default allocation.
                return true;
            }

            if (!GlobalDataManager.IsCompleteStatPreset(globalData.StrategistTargetPreset))
            {
                ShowRoomValidationMessage("전략가 전환 프리셋의 모든 스텟을 투자하십시오");
                return false;
            }

            if (!GlobalDataManager.IsStrategistPreset(globalData.StrategistTargetPreset))
            {
                ShowRoomValidationMessage("전략가 전환 프리셋은 전략가형만 설정할 수 있습니다.");
                return false;
            }

            return true;
        }

        private void ShowRoomValidationMessage(string message)
        {
            if (StatCustomizerController.Instance == null)
            {
                Debug.LogWarning(message);
                return;
            }

            StatCustomizerController.Instance.ShowFloatingMessage(message);
        }

        public void SetSelectedRoom(string roomId) => _selectedRoomId = roomId;

        private void OnStatSettingButtonClicked()
        {
            if (_canvas_Customizer == null) FindCanvasCustomizer();
            if (_canvas_Customizer != null) SetCustomizerActive(!IsCustomizerVisible());
            else Debug.LogWarning("[LobbyUIManager] Stat customizer was not found in the loaded scene.");
        }

        public void SetCustomizerActive(bool active)
        {
            FindCanvasCustomizer();
            if (_canvas_Customizer == null) return;

            if (active)
            {
                JobGuidePanel.CloseIfOpen();
                CharacterInfoController.CloseOpenPanel();
                if (_room_UI != null) _room_UI.SetActive(false);
                if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
                EnsureCustomizerHierarchyVisible(_canvas_Customizer.transform);
            }

            _canvas_Customizer.SetActive(active);
            if (active) StatCustomizerController.Instance?.RefreshForOpen();
            GameInputController.RefreshCursorState();
        }

        private bool IsCustomizerVisible()
        {
            if (_canvas_Customizer == null)
                return false;

            if (!_canvas_Customizer.activeInHierarchy)
                return false;

            Transform current = _canvas_Customizer.transform;
            while (current != null)
            {
                Vector3 scale = current.localScale;
                if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f))
                    return false;
                current = current.parent;
            }

            return true;
        }

        private static void EnsureCustomizerHierarchyVisible(Transform customizer)
        {
            Transform current = customizer;
            while (current != null)
            {
                if (current != customizer &&
                    (current.GetComponent<NetworkIdentity>() != null || current.GetComponent<PlayerManager>() != null))
                {
                    break;
                }

                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);

                Canvas canvas = current.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 50);
                }

                bool isUiCanvasRoot = canvas != null || current.name.Contains("UI_Root") || current.name.Contains("Canvas");
                if (isUiCanvasRoot)
                {
                    Vector3 scale = current.localScale;
                    if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
                        current.localScale = Vector3.one;
                }

                current = current.parent;
            }
        }

        public void UpdateRoomButtonsInteractable(bool interactable)
        {
            if (_createRoomButton != null) _createRoomButton.interactable = interactable;
            if (_joinRoomButton != null) _joinRoomButton.interactable = interactable;
            if (_saveRoomButton != null) _saveRoomButton.interactable = interactable;
            if (_saveRoomButtonComp != null) _saveRoomButtonComp.interactable = interactable;
        }
    }
}
