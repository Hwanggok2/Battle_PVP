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
        [SerializeField] private Button _deleteButton;
        [SerializeField] private Outline _selectionOutline;

        private string _roomId;
        private string _roomName;
        private string _masterName;
        private int _playerCount;
        private bool _hasInfo;
        public bool IsSelected => _selectedItem == this;
        private Action<string> _onSelected;
        private Action<string> _onDeleteRequested;
        private Graphic _rowBackground;
        private Color _normalBackgroundColor;

        private void Awake()
        {
            if (_selectButton != null) _selectButton.onClick.AddListener(OnItemClicked);
            MakeSelectButtonOverlayTransparent();
            EnsureSelectionOutline();
            SetSelected(false);
        }

        private void OnDestroy()
        {
            if (_selectButton != null) _selectButton.onClick.RemoveListener(OnItemClicked);
            if (_deleteButton != null) _deleteButton.onClick.RemoveListener(OnDeleteClicked);
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
            SetInfo(roomName, roomName, masterName, playerCount, onSelected, null, false);
        }

        public void SetInfo(
            string roomId,
            string roomName,
            string masterName,
            int playerCount,
            Action<string> onSelected,
            Action<string> onDeleteRequested,
            bool showDeleteButton)
        {
            _roomId = roomId;
            _onSelected = onSelected;
            _onDeleteRequested = onDeleteRequested;
            SetSelected(_selectedItem == this);

            if (_roomNameText != null && (!_hasInfo || _roomName != roomName))
                UserTextPresentation.SetPlain(_roomNameText, UserDisplayText.SingleLine(roomName, UserDisplayText.RoomNameLimit));
            if (_masterNameText != null && (!_hasInfo || _masterName != masterName))
            {
                _masterNameText.gameObject.SetActive(true);
                UserTextPresentation.SetPlain(_masterNameText, $"방장 · {UserDisplayText.SingleLine(masterName, UserDisplayText.NameLimit)}");
            }
            if (_playerCountText != null && (!_hasInfo || _playerCount != playerCount))
            {
                _playerCountText.gameObject.SetActive(true);
                _playerCountText.text = $"참가자 · {playerCount}";
            }

            _roomName = roomName;
            _masterName = masterName;
            _playerCount = playerCount;
            _hasInfo = true;
            ConfigureDeleteButton(showDeleteButton);
        }

        private void OnItemClicked()
        {
            if (_selectedItem != null && _selectedItem != this)
                _selectedItem.SetSelected(false);

            _selectedItem = this;
            SetSelected(true);

            _onSelected?.Invoke(_roomId);
            Debug.Log($"[RoomListItem] Item Selected: {_roomId}");
        }

        public void SetAdmissionInfo(int count, int capacity, bool isPrivate)
        {
            if (_playerCountText != null) _playerCountText.text = $"{(isPrivate ? "비밀방 · " : "")}{count} / {Mathf.Max(2, capacity)}명";
        }

        public void SetBrowseOnly(bool browseOnly)
        {
            if (_selectButton != null) _selectButton.interactable = !browseOnly;
            if (browseOnly)
            {
                if (_selectedItem == this) _selectedItem = null;
                SetSelected(false);
            }
        }

        private void OnDeleteClicked()
        {
            _onDeleteRequested?.Invoke(_roomId);
        }

        private void EnsureSelectionOutline()
        {
            if (_rowBackground == null)
            {
                _rowBackground = GetComponent<Graphic>();
                if (_rowBackground != null) _normalBackgroundColor = _rowBackground.color;
            }
            if (_selectionOutline == null)
                _selectionOutline = GetComponent<Outline>();

            if (_selectionOutline == null)
                _selectionOutline = gameObject.AddComponent<Outline>();

            _selectionOutline.effectColor = new Color(.1f, .85f, 1f, 1f);
            _selectionOutline.effectDistance = new Vector2(2f, -2f);
            _selectionOutline.useGraphicAlpha = false;
        }

        private void MakeSelectButtonOverlayTransparent()
        {
            if (_selectButton == null) return;

            var graphic = _selectButton.targetGraphic;
            if (graphic != null && graphic.gameObject != gameObject)
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
            if (_rowBackground != null)
            {
                Color color = selected ? _normalBackgroundColor * .55f : _normalBackgroundColor;
                color.a = selected ? Mathf.Max(.95f, _normalBackgroundColor.a) : _normalBackgroundColor.a;
                _rowBackground.color = color;
            }
        }

        private void ConfigureDeleteButton(bool showDeleteButton)
        {
            if (!showDeleteButton)
            {
                if (_deleteButton != null)
                {
                    _deleteButton.onClick.RemoveListener(OnDeleteClicked);
                    _deleteButton.gameObject.SetActive(false);
                }

                return;
            }

            if (_deleteButton == null)
                _deleteButton = CreateDeleteButton();

            if (_deleteButton == null)
                return;

            _deleteButton.gameObject.SetActive(true);
            _deleteButton.onClick.RemoveListener(OnDeleteClicked);
            _deleteButton.onClick.AddListener(OnDeleteClicked);
        }

        private Button CreateDeleteButton()
        {
            var buttonObject = new GameObject("Dev_DeleteRoom", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-8f, 0f);
            rect.sizeDelta = new Vector2(72f, 28f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.55f, 0.12f, 0.12f, 0.92f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "Delete";
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            return button;
        }
    }
}
