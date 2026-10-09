using System.Collections;
using System.Collections.Generic;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed partial class PracticeModePanel : MonoBehaviour
    {
        public static PracticeModePanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._overlay != null && Instance._overlay.activeSelf;
        private GameObject _overlay, _editor, _choicePanel;
        private RectTransform _dialog, _roster, _choices;
        private ScrollRect _rosterScroll;
        private TMP_InputField _count;
        private TMP_Text _mapLabel, _status, _rosterTitle, _editorTitle, _stats, _empty, _choiceTitle;
        private RawImage _mapPreview;
        private Button _character, _weapon, _job;
        private readonly Button[] _skills = new Button[2];
        private readonly TMP_Text[] _skillDetails = new TMP_Text[2];
        private readonly List<PracticeBotSetup> _bots = new();
        private byte _map;
        private int _selected;
        private bool _hasDraft, _starting;
        private Coroutine _startRoutine;
        private static List<PracticeBotSetup> _lastBots;
        private static byte _lastMap;
        private PracticeBotSetup Selected => _selected >= 0 && _selected < _bots.Count ? _bots[_selected] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Lobby") return;
            var go = new GameObject("Practice UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            // Lobby chat's transparent viewport is on order 100 and still receives clicks.
            go.GetComponent<Canvas>().sortingOrder = 110;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            go.AddComponent<PracticeModePanel>();
        }
        private void Awake() { Instance = this; BuildLayout(); }
        private static void Clear(Transform root)
        { foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); } }
        private void ChangeMap(int delta)
        { _map = (byte)((_map + delta + BattleMapSelection.MapCount) % BattleMapSelection.MapCount); RefreshMap(); }
        private void RefreshMap()
        { _mapLabel.text = "전장 · " + BattleMapSelection.MapName(_map); _mapPreview.texture = BattleMapPreviews.Get(_map); }
        private bool ReadCount()
        {
            if (_starting) return false;
            if (!int.TryParse(_count.text, out int count) || count < 0 || count > BattleNetworkManager.MaxPracticeBots)
            { _status.text = $"AI 수는 0~{BattleNetworkManager.MaxPracticeBots}명으로 입력하세요."; return false; }
            if (count != _bots.Count) ResizeBots(count);
            return true;
        }
        private void AdjustCount(int delta)
        {
            int.TryParse(_count.text, out int count);
            ResizeBots(Mathf.Clamp(count + delta, 0, BattleNetworkManager.MaxPracticeBots));
        }
        private void ResizeBots(int count)
        {
            bool added = count > _bots.Count;
            while (_bots.Count < count) _bots.Add(PracticeBotSetup.Randomized());
            while (_bots.Count > count) _bots.RemoveAt(_bots.Count - 1);
            if (added) _selected = count - 1;
            _count.SetTextWithoutNotify(count.ToString()); RefreshBots();
            if (added) { Canvas.ForceUpdateCanvases(); _rosterScroll.verticalNormalizedPosition = 0; }
        }
        private void RemoveSelected()
        {
            if (Selected == null) return;
            _bots.RemoveAt(_selected); _count.SetTextWithoutNotify(_bots.Count.ToString()); RefreshBots();
        }
        private void RefreshBots()
        {
            _choicePanel.SetActive(false);
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _bots.Count - 1));
            _rosterTitle.text = $"AI 참가자 · {_bots.Count}명";
            Clear(_roster);
            for (int i = 0; i < _bots.Count; i++)
            {
                int index = i; var bot = _bots[i]; var definition = CharacterCatalog.Instance.Find(bot.CharacterId);
                var row = Button($"AI {i + 1:00}", _roster, "", new Vector2(228, 86), Vector2.zero);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 86;
                row.GetComponent<Image>().color = i == _selected ? new Color(.09f, .3f, .36f) : new Color(.04f, .1f, .14f);
                var portrait = RoomUiElements.Rect("Portrait", row.transform, new Vector2(48, 64), new Vector2(-80, 0)).gameObject.AddComponent<Image>();
                portrait.sprite = definition.Portrait; portrait.preserveAspect = true; portrait.raycastTarget = false;
                portrait.enabled = portrait.sprite != null;
                var label = row.GetComponentInChildren<TMP_Text>(); label.fontSize = 17;
                label.rectTransform.anchoredPosition = new Vector2(28, 0); label.rectTransform.sizeDelta = new Vector2(156, 78);
                label.alignment = TextAlignmentOptions.MidlineLeft; label.overflowMode = TextOverflowModes.Ellipsis;
                label.text = $"AI {i + 1:00}\n{definition.DisplayName}\n{WeaponCatalog.Instance.Find(bot.Weapon).Name}";
                row.onClick.AddListener(() => { _selected = index; RefreshBots(); });
            }
            _editor.SetActive(Selected != null); _empty.gameObject.SetActive(Selected == null);
            _status.text = "추가한 AI의 설정은 연습 시작 시 그대로 적용됩니다.";
            if (Selected == null) return;
            _editorTitle.text = $"AI {_selected + 1:00} · 구성 편집";
            Label(_character, "캐릭터 · " + CharacterCatalog.Instance.Find(Selected.CharacterId).DisplayName + "   >");
            Label(_weapon, "무기 · " + WeaponCatalog.Instance.Find(Selected.Weapon).Name + "   >");
            Label(_job, "직업 · " + JobGuideContent.Name(Selected.Job) + "   >");
            var stats = Selected.Stats;
            _stats.text = $"스탯 배분  STR {stats.STR.Invested:0} · CON {stats.CON.Invested:0} · AGI {stats.AGI.Invested:0} · DEF {stats.DEF.Invested:0}";
            for (int i = 0; i < 2; i++)
            {
                var kind = (JobSkillKind)Selected.Skills[Selected.Job * 2 + i];
                Label(_skills[i], $"스킬 {i + 1} · {SkillName(kind)}   >");
                _skillDetails[i].text = SkillGameData.Description(kind);
            }
        }
        private static void Label(Button button, string text) => button.GetComponentInChildren<TMP_Text>().text = text;
        private static string SkillName(JobSkillKind kind)
        { var data = SkillGameData.Instance?.Find((int)kind); return data == null ? kind.ToString() : SkillGameData.Text(data.NameKey, kind.ToString()); }
        private void BeginChoices(string title)
        { Clear(_choices); _choiceTitle.text = title; _choicePanel.SetActive(true); _choices.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1; }
        private void Choice(string key, string title, string detail, bool selected, System.Action apply)
        {
            var button = Button(key, _choices, "", new Vector2(508, 90), Vector2.zero);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
            button.GetComponent<Image>().color = selected ? new Color(.09f, .3f, .36f) : new Color(.045f, .13f, .17f);
            Text("Name", button.transform, (selected ? "선택됨 · " : "") + title, new Vector2(478, 30), new Vector2(0, 21), 21);
            var description = Text("Detail", button.transform, detail, new Vector2(478, 40), new Vector2(0, -17), 16);
            description.overflowMode = TextOverflowModes.Ellipsis;
            button.onClick.AddListener(() => { apply(); RefreshBots(); });
        }
        private void ShowCharacters()
        {
            if (Selected == null) return; BeginChoices("캐릭터 선택");
            foreach (var character in CharacterCatalog.Instance.Characters)
            {
                var entry = character;
                Choice(entry.Id, entry.DisplayName, entry.Description, Selected.CharacterId == entry.Id, () => Selected.CharacterId = entry.Id);
            }
        }
        private void ShowWeapons()
        {
            if (Selected == null) return; BeginChoices("무기 선택");
            foreach (var weapon in WeaponCatalog.Instance.Weapons)
            {
                var entry = weapon;
                Choice(entry.Kind.ToString(), entry.Name, entry.Description, Selected.Weapon == entry.Kind, () => Selected.Weapon = entry.Kind);
            }
        }
        private void ShowJobs()
        {
            if (Selected == null) return; BeginChoices("직업 선택");
            for (int i = 0; i < JobGuideContent.Count; i++)
            {
                int job = i;
                Choice("Job " + i, JobGuideContent.Name(i), JobGuideContent.Description(i), Selected.Job == i, () => Selected.SetJob(job));
            }
        }
        private void ShowSkills(int slot)
        {
            if (Selected == null) return; BeginChoices($"스킬 {slot + 1} 선택");
            foreach (var kind in PracticeBotSetup.SkillOptions(Selected.Job))
            {
                var entry = kind;
                Choice(entry.ToString(), SkillName(entry), SkillGameData.Description(entry), Selected.Skills[Selected.Job * 2 + slot] == (int)entry,
                    () => Selected.SetSkill(slot, entry));
            }
        }
        public PracticeBotSetup[] SnapshotBots() => _bots.ConvertAll(bot => bot.Copy()).ToArray();
        public void Open()
        {
            LobbyUIManager.Instance?.CloseInputPanels();
            CharacterSelectionPanel.CloseIfOpen(); WeaponSelectionPanel.CloseIfOpen(); JobGuidePanel.CloseIfOpen();
            if (!_hasDraft)
            {
                _hasDraft = true; _map = _lastMap;
                if (_lastBots != null) _bots.AddRange(_lastBots.ConvertAll(bot => bot.Copy()));
                else for (int i = 0; i < 3; i++) _bots.Add(PracticeBotSetup.Randomized());
            }
            _count.SetTextWithoutNotify(_bots.Count.ToString()); RefreshMap(); RefreshBots();
            _overlay.SetActive(true); GameInputController.RefreshCursorState();
        }
        public void Close()
        {
            if (_startRoutine != null) { StopCoroutine(_startRoutine); _startRoutine = null; }
            SetStarting(false);
            _lastBots = _bots.ConvertAll(bot => bot.Copy()); _lastMap = _map;
            if (_overlay != null) _overlay.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            GameInputController.RefreshCursorState();
        }
        private void SetStarting(bool starting)
        {
            _starting = starting;
            foreach (var button in _dialog.GetComponentsInChildren<Button>(true))
                button.interactable = !starting || button.name == "Cancel" || button.name == "Close";
            _count.interactable = !starting;
        }
        private void StartPractice()
        {
            if (_starting || !ReadCount()) return;
            var manager = NetworkManager.singleton as BattleNetworkManager;
            if (manager == null || NetworkServer.active || NetworkClient.active)
            { _status.text = "진행 중인 방에서 나온 뒤 연습을 시작해 주세요."; return; }
            SetStarting(true);
            _startRoutine = StartCoroutine(PreparePractice(manager, SnapshotBots(), _map));
        }
        private IEnumerator PreparePractice(BattleNetworkManager manager, PracticeBotSetup[] bots, byte map)
        {
            _status.text = "AI 캐릭터를 준비하는 중…";
            // Cache each chosen visual once while the setup screen remains responsive.
            var loaded = new HashSet<string>();
            foreach (var bot in bots)
                if (loaded.Add(bot.CharacterId)) yield return CharacterCatalog.Instance.Find(bot.CharacterId).LoadVisualAsync();
            _startRoutine = null; SetStarting(false);
            if (manager != null && manager.StartPractice(bots, map)) Close();
            else _status.text = "연습모드를 시작하지 못했습니다. 설정을 확인해 주세요.";
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
