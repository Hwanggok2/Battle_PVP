using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattlePvp.Stats;

namespace BattlePvp.UI
{
    /// <summary>
    /// 캐릭터 상세 정보 UI 요소들을 관리합니다.
    /// 플레이어 아이콘 클릭 시 활성화되어 최신 스탯 정보를 표시합니다.
    /// </summary>
    public class CharacterInfoController : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private GameObject _infoPanel;
        [SerializeField] private Button _playerIconButton;
        [SerializeField] private StatManager _statManager;
        // 로그인 ID 표시용 레이블 (로비 매니저나 PlayFab 데이터 연동 필요)
        [SerializeField] private TMP_Text _loginIdText;

        [Header("Primary Stats (Left Column)")]
        [SerializeField] private TMP_Text _strText;
        [SerializeField] private TMP_Text _agiText;
        [SerializeField] private TMP_Text _conText;
        [SerializeField] private TMP_Text _defText;

        [Header("Derived Stats (Right Column)")]
        [SerializeField] private TMP_Text _atkText;
        [SerializeField] private TMP_Text _defRateText;
        [SerializeField] private TMP_Text _maxHpText;
        [SerializeField] private TMP_Text _peneText;
        [SerializeField] private TMP_Text _regenText;
        [SerializeField] private TMP_Text _moveSpdText;
        [SerializeField] private TMP_Text _atkSpdText;

        private void Awake()
        {
            if (_playerIconButton != null)
            {
                _playerIconButton.onClick.AddListener(ToggleInfoPanel);
            }
        }

        private void OnEnable()
        {
            // 부모 캔버스 활성화 혹은 스크립트 켜질 때 갱신 (선택적)
            if (_infoPanel != null && _infoPanel.activeSelf)
            {
                UpdateStatsDisplay();
            }
        }

        private void OnDestroy()
        {
            if (_playerIconButton != null)
            {
                _playerIconButton.onClick.RemoveListener(ToggleInfoPanel);
            }
        }

        /// <summary>
        /// 상태창을 열고 닫는 토글 기능
        /// </summary>
        public void ToggleInfoPanel()
        {
            if (_infoPanel == null) return;
            
            bool isActive = _infoPanel.activeSelf;
            _infoPanel.SetActive(!isActive);

            if (!isActive)
            {
                UpdateStatsDisplay();
            }
        }

        /// <summary>
        /// UI를 표시할 때 호출되어 실제 스탯(FinalTotal)을 기반으로 UI를 갱신합니다.
        /// </summary>
        public void UpdateStatsDisplay()
        {
            if (_statManager == null) return;

            // Optional: 로그인 ID 업데이트 (추후 PlayFab 데이터 연동 시 주석 해제하여 사용)
            // if (_loginIdText != null && BattlePvp.Networking.PlayFabBattleManager.Instance != null)
            // {
            //     _loginIdText.text = $"ID: {PlayerPrefs.GetString("PlayFabId", "Unknown")}";
            // }

            // 1) Primary Stats
            if (_strText != null) _strText.text = $"STR : {_statManager.GetFinalTotal(StatKind.STR)}";
            if (_agiText != null) _agiText.text = $"AGI : {_statManager.GetFinalTotal(StatKind.AGI)}";
            if (_conText != null) _conText.text = $"CON : {_statManager.GetFinalTotal(StatKind.CON)}";
            if (_defText != null) _defText.text = $"DEF : {_statManager.GetFinalTotal(StatKind.DEF)}";

            // 2) Derived Stats (현재 적용된 스탯을 가상 스탯 컨테이너처럼 던져서 갱신값을 받아옴)
            StatContainer currentStats = _statManager.GetStatsCopy();
            _statManager.CalculatePreviewStats(currentStats, out float atk, out float def, out float maxHp, out float pene, out float regen, out float moveSpd, out float atkSpd);

            if (_atkText != null) _atkText.text = $"공격력 : {atk:F0}";
            if (_defRateText != null) _defRateText.text = $"방어력 : {def:F1}%";
            if (_maxHpText != null) _maxHpText.text = $"최대 체력 : {maxHp:F0}";
            if (_peneText != null) _peneText.text = $"물리 관통력 : {pene:F1}%";
            if (_regenText != null) _regenText.text = $"재생력 : {regen:F1}/s";
            if (_moveSpdText != null) _moveSpdText.text = $"이동속도 : {moveSpd:F2}";
            if (_atkSpdText != null) _atkSpdText.text = $"공격속도 : {atkSpd:F2}";
        }
    }
}
