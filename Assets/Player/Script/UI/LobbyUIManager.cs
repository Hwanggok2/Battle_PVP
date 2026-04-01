using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;
using BattlePvp.Stats;
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

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // 초기 상태에서는 방 설정 패널 비활성화
            if (_roomSettingPanel != null) _roomSettingPanel.SetActive(false);

            if (_canvas_Customizer == null)
            {
                var customizer = FindFirstObjectByType<StatCustomizerController>(FindObjectsInactive.Include);
                if (customizer != null)
                {
                    _canvas_Customizer = customizer.gameObject;
                }
            }
        }

        private void OnEnable()
        {
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
        }

        private void OnDisable()
        {
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

            // PlayFab SharedGroupId 규칙: 영문, 숫자, _, - 만 허용 (공백/한글 불가)
            if (!Regex.IsMatch(roomName, @"^[a-zA-Z0-9_-]+$"))
            {
                Debug.LogError("[LobbyUI] Invalid Room Name! Only English, numbers, '_', and '-' are allowed (No spaces or Korean).");
                return;
            }

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
