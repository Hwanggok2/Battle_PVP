using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.Managers;
using Mirror;
using System.Text.RegularExpressions;
using System.Collections;

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
        private Coroutine _discoveryRoutine;
        private Coroutine _statUpdateRoutine;
        private BattlePvp.Combat.HealthSystem _localHealthSystem;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);

            if (_lobby_UI == null)
            {
                var allLobbyUIs = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var go in allLobbyUIs)
                {
                    if (go.name == "Lobby_UI" && go.scene.isLoaded)
                    {
                        _lobby_UI = go;
                        break;
                    }
                }
            }
            
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
            RefreshVisibility();
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

            // [최적화] 코루틴 기반 지속적 탐색 루틴 시작
            if (_discoveryRoutine != null) StopCoroutine(_discoveryRoutine);
            _discoveryRoutine = StartCoroutine(CoAutoDiscovery());

            if (_statUpdateRoutine != null) StopCoroutine(_statUpdateRoutine);
            _statUpdateRoutine = StartCoroutine(CoSubscribeToLocalPlayerStats());
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

            if (Mirror.NetworkClient.localPlayer != null)
            {
                var statMgr = Mirror.NetworkClient.localPlayer.GetComponent<StatManager>();
                if (statMgr != null) statMgr.StatsChanged -= OnLocalStatsChanged;
            }

            if (_localHealthSystem != null)
            {
                _localHealthSystem.OnDied -= OnLocalPlayerDied;
                _localHealthSystem.OnRevived -= OnLocalPlayerRevived;
                _localHealthSystem = null;
            }

            // [최적화] 모든 루틴 정지
            StopAllCoroutines();
            _discoveryRoutine = null;
            _statUpdateRoutine = null;
        }

        // Update() 제거 (이벤트 기반으로 전환)

        private IEnumerator CoAutoDiscovery()
        {
            while (true)
            {
                if (this == null) yield break;
                FindCanvasCustomizer();
                yield return new WaitForSeconds(1f); // 1초 주기로 탐색 (최적화)
            }
        }

        private void FindCanvasCustomizer()
        {
            if (this == null) return;

            // Unity bool 캐스팅: Missing(파괴된) 오브젝트도 false로 올바르게 감지
            if (_canvas_Customizer && _canvas_Customizer.name.Contains("_Root"))
                _canvas_Customizer = null;
            if (_canvas_Customizer) return;

            _canvas_Customizer = null; // Missing 참조 완전 제거

            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                if (go.name == "Canvas_Customizer" && go.scene.isLoaded)
                {
                    _canvas_Customizer = go;
                    return;
                }
            }

            var controllers = Resources.FindObjectsOfTypeAll<StatCustomizerController>();
            foreach (var controller in controllers)
            {
                if (controller == null || !controller.gameObject.scene.isLoaded)
                    continue;

                _canvas_Customizer = controller.gameObject;
                return;
            }
        }

        public void RefreshVisibility()
        {
            if (this == null) return;
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isLobby = sceneName.Contains("Lobby") || !sceneName.Contains("Battle"); 
            bool isBattleWaiting = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
            bool isBattle = sceneName.Equals("Battle") || (sceneName.Contains("Battle") && !sceneName.Contains("wait"));

            if (isLobby || isBattleWaiting)
            {
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
                if (_battleButton != null) { _battleButton.gameObject.SetActive(true); _battleButton.interactable = true; }
                if (_statSettingButton != null) { _statSettingButton.gameObject.SetActive(true); _statSettingButton.interactable = true; }
                return;
            }

            if (isBattle)
            {
                bool isDead = false;
                if (Mirror.NetworkClient.localPlayer != null)
                {
                    var hpSys = Mirror.NetworkClient.localPlayer.GetComponent<BattlePvp.Combat.HealthSystem>();
                    if (hpSys != null) isDead = hpSys.IsDead;
                }

                if (isDead)
                {
                    if (_lobby_UI != null) _lobby_UI.SetActive(true);
                    // 사망 시 Stat 버튼만 활성화 — 플레이어가 직접 눌러서 창을 열도록 합니다.
                    if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(!isMonostat);
                }
                else
                {
                    if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(false);
                }
            }
        }

        private IEnumerator CoSubscribeToLocalPlayerStats()
        {
            while (Mirror.NetworkClient.localPlayer == null) yield return null;
            if (this == null) yield break;

            var statMgr = Mirror.NetworkClient.localPlayer.GetComponent<StatManager>();
            if (statMgr != null)
                statMgr.StatsChanged += OnLocalStatsChanged;

            // HealthSystem 이벤트 구독 (사망/부활 시 가시성 갱신)
            var hs = Mirror.NetworkClient.localPlayer.GetComponent<BattlePvp.Combat.HealthSystem>();
            if (hs != null)
            {
                _localHealthSystem = hs;
                hs.OnDied += OnLocalPlayerDied;
                hs.OnRevived += OnLocalPlayerRevived;
            }

            RefreshVisibility();
        }

        private void OnLocalPlayerDied() => RefreshVisibility();
        private void OnLocalPlayerRevived() => RefreshVisibility();

        private void OnLocalStatsChanged(StatContainer _) => RefreshVisibility();

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (this == null) return;
            if (_room_UI != null) _room_UI.SetActive(false);
            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
            
            StartCoroutine(CoWaitAndRefreshVisibility(scene.name));
        }

        private IEnumerator CoWaitAndRefreshVisibility(string sceneName)
        {
            yield return new WaitForSeconds(0.1f);
            if (this == null) yield break;

            bool isWaitingScene = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
            if (isWaitingScene)
            {
                float timeout = 2.0f;
                while (Mirror.NetworkClient.localPlayer == null && timeout > 0) { timeout -= 0.1f; yield return new WaitForSeconds(0.1f); }
                if (this == null) yield break;

                RefreshVisibility();
                if (_canvas_Customizer != null) _canvas_Customizer.SetActive(true);
            }
            else
            {
                if (_canvas_Customizer != null) _canvas_Customizer.SetActive(false);
                RefreshVisibility();
            }
        }

        private bool IsCurrentlyMonostat()
        {
            IdentityType currentType = IdentityType.Polymath;
            if (Mirror.NetworkClient.localPlayer != null)
            {
                var statMgr = Mirror.NetworkClient.localPlayer.GetComponent<StatManager>();
                if (statMgr != null) currentType = statMgr.CurrentIdentity.Type;
            }
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
        }

        private void OnCreateRoomButtonClicked()
        {
            if (!CanStartRoomFlow())
                return;
            if (_roomSettingPanel != null) { _roomSettingPanel.SetActive(true); if (_roomNameInput != null) _roomNameInput.text = ""; }
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
                PlayFabBattleManager.Instance.JoinRoom(_selectedRoomId);
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
                ShowRoomValidationMessage("전략가 전환 프리셋을 설정하십시오");
                return false;
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
            if (_canvas_Customizer == null) FindCanvasCustomizer();
            if (_canvas_Customizer == null) return;

            if (active)
                EnsureCustomizerHierarchyVisible(_canvas_Customizer.transform);

            _canvas_Customizer.SetActive(active);
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

        public void UpdateRoomButtonsInteractable(bool interactable) { }
    }
}
