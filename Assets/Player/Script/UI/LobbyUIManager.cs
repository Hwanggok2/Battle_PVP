using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;
using BattlePvp.Stats;
using Mirror;
using System.Text.RegularExpressions;

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

        [Header("Room Selection (Internal)")]
        [SerializeField] private string _selectedRoomId = "Global_PvP_Room_1"; // 현재 선택된 방 ID (임시 기본값)

        [Header("Room Setting UI")]
        [SerializeField] private GameObject _roomSettingPanel;   // 방 설정 팝업 패널
        [SerializeField] private TMPro.TMP_InputField _roomNameInput; // 방 제목 입력창
        [SerializeField] private Button _saveRoomButton;        // 방 설정 저장(생성) 버튼

        private GameObject _battlePanelCached;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // 초기 상태에서는 방 설정 패널 비활성화
            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);

            // [초강력 수정] 비활성화된 오브젝트까지 포함하여 씬 전체에서 Lobby_UI를 찾습니다.
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
                Debug.Log($"[LobbyUI] Success: Found Lobby_UI! Searching for buttons inside...");
                
                // 부모 패널 캐싱
                var bp = _lobby_UI.transform.Find("Battle_Panel");
                if (bp == null) bp = _lobby_UI.transform.GetComponentInChildren<Transform>(true).Find("Battle_Panel");
                if (bp != null) _battlePanelCached = bp.gameObject;

                // 이름 기반으로 모든 버튼 탐색
                Button[] allButtons = _lobby_UI.GetComponentsInChildren<Button>(true);
                foreach (var b in allButtons)
                {
                    if (b.name.Equals("Battle")) _battleButton = b;
                    if (b.name.Equals("Stat")) _statSettingButton = b;
                }

                if (_battleButton == null) Debug.LogWarning("[LobbyUI] Failure: Could not find button named 'Battle' under Lobby_UI!");
                if (_statSettingButton == null) Debug.LogWarning("[LobbyUI] Failure: Could not find button named 'Stat' under Lobby_UI!");
            }
            else
            {
                Debug.LogError("[LobbyUI] Critical Failure: Could not find any GameObject named 'Lobby_UI' in the scene even with deep search!");
            }

            if (_canvas_Customizer == null)
            {
                // 1. 타입으로 먼저 찾기 (비활성화 포함)
                var customizer = FindFirstObjectByType<StatCustomizerController>(FindObjectsInactive.Include);
                if (customizer != null) _canvas_Customizer = customizer.gameObject;

                // 2. 실패 시 이름으로 정밀 탐색 (모든 씬 개체 포함)
                if (_canvas_Customizer == null)
                {
                    var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                    foreach (var go in allObjects)
                    {
                        if (go.name == "Canvas_Customizer" && go.scene.isLoaded)
                        {
                            _canvas_Customizer = go;
                            break;
                        }
                    }
                }
            }

            if (_canvas_Customizer != null)
                Debug.Log("[LobbyUI] Success: Found and assigned Canvas_Customizer.");
            else
                Debug.LogWarning("[LobbyUI] Warning: Canvas_Customizer not found in this scene.");

            // [수정] 씬과 상관없이 로비 매니저가 깨어났을 때 현재 상태를 즉시 반영합니다.
            RefreshVisibility();
        }

        private void Update()
        {
            // 최적화를 위해 Update 루프를 사용하지 않습니다.
            // 이벤트와 코루틴을 통해서만 가시성을 제어합니다.
        }

        /// <summary>
        /// 현재 씬 및 플레이어 상태에 따라 버튼 가시성을 갱신합니다.
        /// </summary>
        public void RefreshVisibility()
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isLobby = sceneName.Contains("Lobby") || !sceneName.Contains("Battle"); 
            bool isBattleWaiting = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
            bool isBattle = sceneName.Equals("Battle") || (sceneName.Contains("Battle") && !sceneName.Contains("wait"));

            // [추가] 부모 패널이 꺼져 있으면 절대 안 보이므로 강제로 켭니다.
            if (isLobby || isBattleWaiting)
            {
                if (_lobby_UI != null) _lobby_UI.SetActive(true);
                if (_battlePanelCached != null) _battlePanelCached.SetActive(true);
            }

            bool isMonostat = IsCurrentlyMonostat();

            if (isLobby || isBattleWaiting)
            {
                // [강력 수정] Battle 버튼은 무조건, 항시 보이도록 설정합니다.
                if (_battleButton != null)
                {
                    _battleButton.gameObject.SetActive(true);
                    _battleButton.interactable = true;
                    // Debug.Log($"[LobbyUI] Forcing Battle Button Active in {sceneName}");
                }

                // Stat 버튼은 몰빵형이 아닐 때만 노출합니다.
                if (_statSettingButton != null)
                {
                    _statSettingButton.gameObject.SetActive(!isMonostat);
                    _statSettingButton.interactable = true;
                }
                return;
            }

            // 실전 배틀 중일 때
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
                    if (_battleButton != null) _battleButton.gameObject.SetActive(true);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(!isMonostat);
                }
                else
                {
                    if (_battleButton != null) _battleButton.gameObject.SetActive(false);
                    if (_statSettingButton != null) _statSettingButton.gameObject.SetActive(false);
                }
            }
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            if (_battleButton != null)
                _battleButton.onClick.AddListener(OnBattleButtonClicked);
            
            if (_statSettingButton != null)
                _statSettingButton.onClick.AddListener(OnStatSettingButtonClicked);

            if (_createRoomButton != null)
                _createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);

            if (_joinRoomButton != null)
                _joinRoomButton.onClick.AddListener(OnJoinRoomButtonClicked);

            if (_saveRoomButton != null)
                _saveRoomButton.onClick.AddListener(OnSaveRoomButtonClicked);

            // [추가] 로컬 플레이어 스탯 변경 감지를 위한 코루틴 시작
            StartCoroutine(CoSubscribeToLocalPlayerStats());
        }

        private System.Collections.IEnumerator CoSubscribeToLocalPlayerStats()
        {
            // 로컬 플레이어가 생성될 때까지 대기
            while (Mirror.NetworkClient.localPlayer == null)
                yield return null;

            var statMgr = Mirror.NetworkClient.localPlayer.GetComponent<StatManager>();
            if (statMgr != null)
            {
                statMgr.StatsChanged += OnLocalStatsChanged;
                // 초기 상태 반영
                RefreshVisibility();
            }
        }

        private void OnLocalStatsChanged(StatContainer _)
        {
            RefreshVisibility();
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_battleButton != null)
                _battleButton.onClick.RemoveListener(OnBattleButtonClicked);
            
            if (_statSettingButton != null)
                _statSettingButton.onClick.RemoveListener(OnStatSettingButtonClicked);

            if (_createRoomButton != null)
                _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);

            if (_joinRoomButton != null)
                _joinRoomButton.onClick.RemoveListener(OnJoinRoomButtonClicked);

            if (_saveRoomButton != null)
                _saveRoomButton.onClick.RemoveListener(OnSaveRoomButtonClicked);

            if (Mirror.NetworkClient.localPlayer != null)
            {
                var statMgr = Mirror.NetworkClient.localPlayer.GetComponent<StatManager>();
                if (statMgr != null)
                    statMgr.StatsChanged -= OnLocalStatsChanged;
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Debug.Log($"[LobbyUI] Scene Loaded: {scene.name}. Managing UI Panels.");

            // 씬 이동 시 일단 패널들을 닫습니다.
            if (_room_UI != null) _room_UI.SetActive(false);
            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
            
            // [추가] 씬 로드 직후가 아니라, 플레이어 스폰 대기 후 가시성을 최종 결정하기 위해 코루틴 실행
            StartCoroutine(CoWaitAndRefreshVisibility(scene.name));
        }

        private System.Collections.IEnumerator CoWaitAndRefreshVisibility(string sceneName)
        {
            // 씬 전환 후 객체들이 안정화될 때까지 아주 짧게 대기
            yield return new WaitForSeconds(0.1f);

            bool isWaitingScene = sceneName.Contains("Battle_wait") || sceneName.Contains("Battle_waiting");
            
            if (isWaitingScene)
            {
                // 로컬 플레이어가 이미 있다면 다행이지만, 없다면 잠시 더 기다려 봅니다.
                float timeout = 2.0f;
                while (Mirror.NetworkClient.localPlayer == null && timeout > 0)
                {
                    timeout -= 0.1f;
                    yield return new WaitForSeconds(0.1f);
                }

                Debug.Log($"[LobbyUI] Refreshing visibility for waiting scene. LocalPlayer found: {Mirror.NetworkClient.localPlayer != null}");
                RefreshVisibility();

                // 스탯 커스터마이저 자동 활성화 제어
                bool isMonostat = IsCurrentlyMonostat();
                if (!isMonostat)
                {
                    if (_canvas_Customizer != null) _canvas_Customizer.SetActive(true);
                }
                else
                {
                    if (_canvas_Customizer != null) _canvas_Customizer.SetActive(false);
                }
            }
            else
            {
                // 일반 배틀 씬이나 다른 씬에서는 무조건 끕니다.
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

        /// <summary>
        /// 'Battle' 버튼 클릭 -> Room UI 토글 활성화
        /// </summary>
        private void OnBattleButtonClicked()
        {
            if (_room_UI != null) 
            {
                bool isActive = _room_UI.activeSelf;
                _room_UI.SetActive(!isActive);
                Debug.Log($"[LobbyUI] Room_UI toggled: {!isActive}");
            }
        }

        /// <summary>
        /// '방 만들기' 버튼 클릭 -> 방 설정 패널 열기
        /// </summary>
        private void OnCreateRoomButtonClicked()
        {
            if (StatCustomizerController.Instance != null && StatCustomizerController.Instance.GetRemainPoints() != 0)
            {
                StatCustomizerController.Instance.ShowFloatingMessage("모든 스텟을 투자하십시오");
                return;
            }

            if (_roomSettingPanel != null)
            {
                _roomSettingPanel.SetActive(true);
                if (_roomNameInput != null) _roomNameInput.text = ""; // 이전 입력 초기화
            }
        }

        /// <summary>
        /// 방 설정 UI에서 'Save' 버튼 클릭 시 실제 방 생성 요청
        /// </summary>
        private void OnSaveRoomButtonClicked()
        {
            if (_roomNameInput == null || string.IsNullOrEmpty(_roomNameInput.text))
            {
                Debug.LogWarning("[LobbyUI] Room name is empty!");
                return;
            }

            string roomName = _roomNameInput.text.Trim();

            if (PlayFabBattleManager.Instance != null)
            {
                Debug.Log($"[LobbyUI] Requesting Room Creation: {roomName}");
                PlayFabBattleManager.Instance.CreateRoom(roomName);
                
                // 생성 요청 후 패널 닫기 및 메인 UI 숨기기
                if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);
                if (_lobby_UI != null) _lobby_UI.SetActive(false);
            }
        }

        /// <summary>
        /// '참여하기' 버튼 클릭 -> 선택된 방에 참여 요청
        /// </summary>
        private void OnJoinRoomButtonClicked()
        {
            if (StatCustomizerController.Instance != null && StatCustomizerController.Instance.GetRemainPoints() != 0)
            {
                StatCustomizerController.Instance.ShowFloatingMessage("모든 스텟을 투자하십시오");
                return;
            }

            if (PlayFabBattleManager.Instance != null && !string.IsNullOrEmpty(_selectedRoomId))
            {
                Debug.Log($"[LobbyUI] Requesting to Join Room: {_selectedRoomId}");
                PlayFabBattleManager.Instance.JoinRoom(_selectedRoomId);
            }
        }

        /// <summary>
        /// 외부(방 목록 UI 등)에서 방을 선택했을 때 호출합니다.
        /// </summary>
        public void SetSelectedRoom(string roomId)
        {
            _selectedRoomId = roomId;
            Debug.Log($"[LobbyUI] Room Selected: {_selectedRoomId}");
        }

        /// <summary>
        /// 'Stat Setting' 버튼 클릭 -> Canvas_Customizer 토글 활성화
        /// </summary>
        private void OnStatSettingButtonClicked()
        {
            if (_canvas_Customizer != null) 
            {
                bool isActive = _canvas_Customizer.activeSelf;
                _canvas_Customizer.SetActive(!isActive);
                Debug.Log($"[LobbyUI] Canvas_Customizer toggled: {!isActive}");
            }
        }

        /// <summary>
        /// 더 이상 사용되지 않음 (글로벌 Floating 메시지로 대체)
        /// </summary>
        public void UpdateRoomButtonsInteractable(bool interactable)
        {
            // if (_createRoomButton != null) _createRoomButton.interactable = interactable;
            // if (_joinRoomButton != null) _joinRoomButton.interactable = interactable;
        }
    }
}
