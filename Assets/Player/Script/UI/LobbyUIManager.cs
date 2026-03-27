using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Managers;
using BattlePvp.Stats;

namespace BattlePvp.UI
{
    /// <summary>
    /// Core Task 2: UI State Management
    /// 로비 버튼(Battle, Stat Setting)에 따른 가시성 조절 및 데이터 플로우 연동을 담당합니다.
    /// </summary>
    public sealed class LobbyUIManager : MonoBehaviour
    {
        [Header("Hierarchy UI Objects")]
        [SerializeField] private GameObject _lobby_UI;        // Lobby_UI 오브젝트 (Battle, Stat 버튼 부모)
        [SerializeField] private GameObject _room_UI;         // Room_UI 패널 (이미지상 Room)
        [SerializeField] private GameObject _canvas_Customizer; // Canvas_Customizer

        [Header("Buttons")]
        [SerializeField] private Button _battleButton;        // 'Battle' 버튼
        [SerializeField] private Button _statSettingButton;   // 'Stat Setting' 버튼 (이미지상 '스텟설정')

        private void OnEnable()
        {
            if (_battleButton != null)
                _battleButton.onClick.AddListener(OnBattleButtonClicked);
            
            if (_statSettingButton != null)
                _statSettingButton.onClick.AddListener(OnStatSettingButtonClicked);
        }

        private void OnDisable()
        {
            if (_battleButton != null)
                _battleButton.onClick.RemoveListener(OnBattleButtonClicked);
            
            if (_statSettingButton != null)
                _statSettingButton.onClick.RemoveListener(OnStatSettingButtonClicked);
        }

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
    }
}
