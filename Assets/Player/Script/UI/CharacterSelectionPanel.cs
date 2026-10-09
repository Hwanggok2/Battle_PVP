using System.Collections.Generic;
using System.Collections;
using BattlePvp.Characters;
using BattlePvp.Logic;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class CharacterSelectionPanel : MonoBehaviour
    {
        public static CharacterSelectionPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeInHierarchy;
        [SerializeField] private TMP_FontAsset _font;
        private GameObject _panel;
        private CharacterPreview _preview;
        private RawImage _previewImage;
        private TMP_Text _name, _description, _status, _current, _creditText;
        private GameObject _credits;
        private readonly TMP_Text[] _statValues = new TMP_Text[5];
        private Button _apply, _source, _license;
        private CharacterDefinition _selected;
        private PlayerAppearance _player;
        private Coroutine _previewLoad;
        private readonly Dictionary<CharacterDefinition, Button> _cards = new();

        private void Awake() { Instance = this; Build(); if (GetComponent<WeaponSelectionPanel>() == null) gameObject.AddComponent<WeaponSelectionPanel>(); }
        private void OnEnable() { StatManager.LocalChanged += Bind; Bind(StatManager.Local); }
        private void OnDisable() { StatManager.LocalChanged -= Bind; Bind(null); Close(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void Bind(StatManager stats)
        {
            if (_player != null) { _player.Changed -= Refresh; _player.RequestCompleted -= Completed; }
            _player = stats != null ? stats.GetComponent<PlayerAppearance>() : null;
            if (_player != null) { _player.Changed += Refresh; _player.RequestCompleted += Completed; }
            if (IsOpen) Refresh();
        }
        private void Update()
        {
            if (IsOpen && GameInputController.Instance == null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                GameInputController.HandleEscape();
        }
        public void Toggle() { if (IsOpen) Close(); else Open(); }
        public void Open()
        {
            if (!PlayerAppearance.CanEdit) return;
            WeaponSelectionPanel.CloseIfOpen();
            JobGuidePanel.CloseIfOpen();
            if (GameSettingsPanel.IsOpen) GameSettingsPanel.Instance.Cancel();
            if (WaitingRoomTerminal.IsOpen) WaitingRoomTerminal.Instance.Close();
            CharacterInfoController.CloseOpenPanel();
            LobbyUIManager.Instance?.CloseInputPanels();
            _panel.SetActive(true);
            _preview.Initialize(_previewImage);
            Select(CharacterCatalog.Instance?.Find(_player != null ? _player.SelectedId : CharacterAppearanceStore.Read())
                ?? CharacterCatalog.Instance?.Find(CharacterCatalog.DefaultId));
            GameInputController.RefreshCursorState();
        }
        public void Close()
        {
            if (_panel == null) return;
            if (_previewLoad != null) { StopCoroutine(_previewLoad); _previewLoad = null; }
            _panel.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            GameInputController.RefreshCursorState();
        }
        public static void CloseIfOpen() { if (IsOpen) Instance.Close(); }
        private void Select(CharacterDefinition definition)
        {
            if (_previewLoad != null) { StopCoroutine(_previewLoad); _previewLoad = null; }
            _selected = definition;
            _name.text = definition != null ? definition.DisplayName : "캐릭터";
            _description.text = definition != null ? definition.Description : "등록된 캐릭터가 없습니다.";
            _creditText.text = definition != null && !string.IsNullOrWhiteSpace(definition.Attribution)
                ? definition.Attribution : "별도 출처 표기 없음";
            _credits.SetActive(false);
            var modifiers = definition != null ? definition.CombatModifiers.Validated : CharacterStatModifiers.Baseline;
            float[] values = { modifiers.Health, modifiers.Defense, modifiers.Attack, modifiers.MoveSpeed, modifiers.AttackSpeed };
            for (int i = 0; i < values.Length; i++)
            {
                float percent = (values[i] - 1f) * 100f;
                _statValues[i].text = Mathf.Approximately(percent, 0) ? "100%  · 기준" : $"{values[i] * 100f:0}%  ({percent:+0;-0}%)";
                _statValues[i].color = Mathf.Approximately(percent, 0) ? Color.white : percent > 0
                    ? new Color(.36f, .94f, .8f) : new Color(1f, .65f, .48f);
            }
            _source.gameObject.SetActive(definition != null && IsWebUrl(definition.SourceUrl));
            _license.gameObject.SetActive(definition != null && IsWebUrl(definition.LicenseUrl));
            if (Application.isPlaying && definition != null && !definition.IsVisualLoaded)
            {
                _previewImage.enabled = false;
                _status.text = "캐릭터를 불러오는 중…";
                _previewLoad = StartCoroutine(LoadPreview(definition));
            }
            else ShowPreview(definition);
            Refresh();
        }
        private IEnumerator LoadPreview(CharacterDefinition definition)
        {
            yield return definition.LoadVisualAsync();
            _previewLoad = null;
            if (!IsOpen || _selected != definition) yield break;
            ShowPreview(definition); Refresh();
        }
        private void ShowPreview(CharacterDefinition definition)
        {
            _previewImage.enabled = true;
            _status.text = _preview.Show(definition, out string error) ? "" : error;
        }
        private void Refresh()
        {
            if (_current == null) return;
            string current = _player != null ? _player.SelectedId : CharacterAppearanceStore.Read();
            _current.text = "사용 중 · " + (CharacterCatalog.Instance?.Find(current)?.DisplayName ?? "기본 캐릭터");
            foreach (var pair in _cards)
            {
                pair.Value.GetComponent<Image>().color = pair.Key == _selected ? new Color(.09f, .32f, .38f) : new Color(.055f, .16f, .21f);
                pair.Value.GetComponentInChildren<TMP_Text>().text = pair.Key.DisplayName + (pair.Key.Id == current ? "  · 사용 중" : "");
            }
            bool valid = _selected != null && _selected.IsVisualLoaded && CharacterCatalog.Instance?.Find(_selected.Id) == _selected;
            if (valid && _player != null) valid = _player.Validate(_selected, out _);
            _apply.interactable = valid && _player != null && !_player.IsPending && PlayerAppearance.CanEdit && current != _selected.Id;
            if (!PlayerAppearance.CanEdit) _status.text = "캐릭터는 로비와 대기실에서 변경할 수 있습니다.";
            else if (_player == null) _status.text = "플레이어를 기다리고 있습니다.";
        }
        private void Apply()
        {
            if (_player == null || _selected == null) return;
            _status.text = "적용 중…";
            if (!_player.Request(_selected.Id, out string error)) _status.text = error;
            Refresh();
        }
        private void Completed(bool accepted, string message) { _status.text = message; Refresh(); }
        private static bool IsWebUrl(string value) => System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri)
            && uri.Scheme == System.Uri.UriSchemeHttps;
        private static void OpenCredit(string url) { if (IsWebUrl(url)) Application.OpenURL(url); }

        private TMP_Text Text(string name, Transform parent, string value, Vector2 size, Vector2 pos, float fontSize = 20)
            => RoomUiElements.Text(name, parent, _font, value, size, pos, fontSize);
        private Button Button(string name, Transform parent, string label, Vector2 size, Vector2 pos)
            => RoomUiElements.Button(name, parent, _font, label, size, pos);
        private void Build()
        {
            var open = Button("Open", transform, "캐릭터", new Vector2(110, 44), new Vector2(-596, -42));
            RoomUiElements.TopMenuButton(open, 0);
            var border = open.gameObject.AddComponent<Outline>(); border.effectColor = new Color(.24f, .9f, .92f, .7f);
            border.effectDistance = new Vector2(1, -1);
            open.onClick.AddListener(Toggle);
            var overlay = RoomUiElements.Rect("Panel", transform, Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, .015f, .025f, .78f);
            _panel = overlay.gameObject;
            var dialog = RoomUiElements.Rect("Dialog", overlay, new Vector2(1080, 690), Vector2.zero);
            dialog.gameObject.AddComponent<Image>().color = new Color(.025f, .055f, .09f, .99f);
            Text("Title", dialog, "캐릭터 선택", new Vector2(760, 42), new Vector2(-130, 292), 30);
            Text("Hint", dialog, "기본 캐릭터 = 100% · 같은 스탯 배분에서 캐릭터별 특성이 적용됩니다.", new Vector2(1000, 32), new Vector2(0, 246), 18);
            Button("Close", dialog, "닫기", new Vector2(88, 38), new Vector2(460, 292)).onClick.AddListener(Close);
            var viewport = RoomUiElements.Rect("List", dialog, new Vector2(310, 424), new Vector2(-355, -1));
            viewport.gameObject.AddComponent<Image>().color = new Color(.02f, .04f, .065f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = RoomUiElements.Rect("Content", viewport, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10; layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var catalog = CharacterCatalog.Instance;
            if (catalog != null && catalog.Characters != null) foreach (var definition in catalog.Characters)
            {
                if (definition == null || _cards.ContainsKey(definition)) continue;
                var entry = definition;
                var card = Button("Character " + entry.Id, content, entry.DisplayName, new Vector2(290, 80), Vector2.zero);
                card.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
                card.onClick.AddListener(() => Select(entry)); _cards.Add(entry, card);
                var label = card.GetComponentInChildren<TMP_Text>(); label.rectTransform.anchoredPosition = new Vector2(34, 0);
                label.rectTransform.sizeDelta = new Vector2(198, 68); label.fontSize = 19; label.alignment = TextAlignmentOptions.MidlineLeft;
                var icon = RoomUiElements.Rect("Portrait", card.transform, new Vector2(54, 54), new Vector2(-106, 0)).gameObject.AddComponent<Image>();
                icon.sprite = entry.Portrait; icon.preserveAspect = true; icon.raycastTarget = false;
                icon.color = entry.Portrait != null ? Color.white : new Color(.2f, .5f, .55f);
            }
            _previewImage = RoomUiElements.Rect("Preview", dialog, new Vector2(360, 365), new Vector2(0, 20)).gameObject.AddComponent<RawImage>();
            _previewImage.raycastTarget = false;
            _preview = overlay.gameObject.AddComponent<CharacterPreview>();
            Button("RotateLeft", dialog, "<", new Vector2(40, 34), new Vector2(-158, -185)).onClick.AddListener(() => _preview.Rotate(-30));
            Button("RotateRight", dialog, ">", new Vector2(40, 34), new Vector2(158, -185)).onClick.AddListener(() => _preview.Rotate(30));
            _name = Text("Name", dialog, "", new Vector2(265, 38), new Vector2(0, -185), 22);
            _name.alignment = TextAlignmentOptions.Center;
            _description = Text("Description", dialog, "", new Vector2(360, 62), new Vector2(0, -237), 16);
            Text("StatTitle", dialog, "캐릭터 특성", new Vector2(280, 36), new Vector2(355, 176), 24);
            string[] statLabels = { "최대 체력", "방어력", "공격력", "이동속도", "공격속도" };
            for (int i = 0; i < statLabels.Length; i++)
            {
                float y = 123 - i * 46;
                Text("StatLabel" + i, dialog, statLabels[i], new Vector2(110, 36), new Vector2(270, y), 18);
                _statValues[i] = Text("StatValue" + i, dialog, "", new Vector2(175, 36), new Vector2(410, y), 19);
                _statValues[i].alignment = TextAlignmentOptions.MidlineRight;
            }
            Text("StatNote", dialog, "스탯 배분·직업 보너스와 함께 적용\n방어력은 기존 방어 상한을 유지합니다.", new Vector2(290, 50), new Vector2(355, -115), 14);
            Button("CreditsToggle", dialog, "크레딧 / 라이선스", new Vector2(280, 30), new Vector2(355, -163))
                .onClick.AddListener(() => _credits.SetActive(!_credits.activeSelf));
            var credits = RoomUiElements.Rect("Credits", dialog, new Vector2(300, 106), new Vector2(355, -236));
            credits.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .8f);
            _credits = credits.gameObject;
            _creditText = Text("Attribution", credits, "", new Vector2(282, 58), new Vector2(0, 17), 13);
            _source = Button("Source", credits, "출처", new Vector2(90, 28), new Vector2(-55, -34));
            _license = Button("License", credits, "라이선스", new Vector2(100, 28), new Vector2(55, -34));
            _source.onClick.AddListener(() => OpenCredit(_selected?.SourceUrl));
            _license.onClick.AddListener(() => OpenCredit(_selected?.LicenseUrl));
            _current = Text("Current", dialog, "", new Vector2(310, 46), new Vector2(-355, -248), 17);
            _status = Text("Status", dialog, "", new Vector2(780, 42), new Vector2(-110, -311), 17);
            _apply = Button("Apply", dialog, "적용", new Vector2(160, 40), new Vector2(425, -311));
            _apply.onClick.AddListener(Apply);
            _panel.SetActive(false);
        }
    }
}
