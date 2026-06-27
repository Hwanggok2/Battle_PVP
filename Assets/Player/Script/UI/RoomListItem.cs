using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace BattlePvp.UI
{
    /// <summary>
    /// 방 목록의 각 항목(Prefab)을 관리하는 스크립트입니다.
    /// </summary>
    public sealed class RoomListItem : MonoBehaviour
    {
        private static RoomListItem _selectedItem;

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _roomNameText;
        [SerializeField] private TextMeshProUGUI _masterNameText;
        [SerializeField] private TextMeshProUGUI _playerCountText;
        [SerializeField] private Button _selectButton;
        [SerializeField] private Outline _selectionOutline;

        private string _roomName;
        private Action<string> _onSelected;

        private void Awake()
        {
            MakeSelectButtonOverlayTransparent();
            EnsureSelectionOutline();
            SetSelected(false);
        }

        private void OnDestroy()
        {
            if (_selectedItem == this)
                _selectedItem = null;
        }

        /// <summary>
        /// 방 정보를 외부(Manager)로부터 주입받아 UI를 갱신합니다.
        /// </summary>
        /// <param name="roomName">방 고유 ID/이름</param>
        /// <param name="playerCount">현재 접속 인원</param>
        /// <param name="onSelected">클릭 시 실행할 콜백</param>
        public void SetInfo(string roomName, string masterName, int playerCount, Action<string> onSelected)
        {
            _roomName = roomName;
            _onSelected = onSelected;
            SetSelected(_selectedItem == this);

            if (_roomNameText != null) _roomNameText.text = roomName;
            if (_masterNameText != null)
            {
                _masterNameText.gameObject.SetActive(true);
                _masterNameText.text = $"Master : {masterName}";
            }
            if (_playerCountText != null)
            {
                _playerCountText.gameObject.SetActive(true);
                _playerCountText.text = $"Player : {playerCount}";
            }

            if (_selectButton != null)
            {
                _selectButton.onClick.RemoveAllListeners();
                _selectButton.onClick.AddListener(OnItemClicked);
            }
        }

        private void OnItemClicked()
        {
            if (_selectedItem != null && _selectedItem != this)
                _selectedItem.SetSelected(false);

            _selectedItem = this;
            SetSelected(true);

            _onSelected?.Invoke(_roomName);
            Debug.Log($"[RoomListItem] Item Selected: {_roomName}");
        }

        private void EnsureSelectionOutline()
        {
            if (_selectionOutline == null)
                _selectionOutline = GetComponent<Outline>();

            if (_selectionOutline == null)
                _selectionOutline = gameObject.AddComponent<Outline>();

            _selectionOutline.effectColor = Color.black;
            _selectionOutline.effectDistance = new Vector2(2f, -2f);
            _selectionOutline.useGraphicAlpha = false;
        }

        private void MakeSelectButtonOverlayTransparent()
        {
            if (_selectButton == null) return;

            var graphic = _selectButton.targetGraphic;
            if (graphic != null)
            {
                Color color = graphic.color;
                color.a = 0f;
                graphic.color = color;
                graphic.raycastTarget = true;
            }
        }

        private void SetSelected(bool selected)
        {
            EnsureSelectionOutline();
            _selectionOutline.enabled = selected;
        }
    }
}
