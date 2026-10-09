using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed partial class JobGuidePanel : MonoBehaviour
    {
        public static JobGuidePanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeInHierarchy;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _openButton;
        [SerializeField] private IdentitySpriteSet _portraits;
        [SerializeField] private JobSkillData[] _skills;
        private readonly Button[] _tabs = new Button[JobGuideContent.Count];
        private readonly TMP_Text[] _tabLabels = new TMP_Text[JobGuideContent.Count];
        private readonly Transform[] _cards = new Transform[4];
        private int[] _draft;
        private SkillLoadout _loadout;
        private TMP_Text _status;
        private Button _save;
        private TMP_Text _title, _requirement, _description;
        private Image _portrait;
        private StatManager _local;
        private int _selected;

        private void Awake()
        {
            // Older scenes contain repeated copies of this standalone canvas.
            if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene &&
                Instance.OwnsLocalView && OwnsLocalView)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            if (OwnsLocalView) Instance = this;
            // The menu buttons use separate canvases (character/weapon: 156).
            // Only the dialog belongs above them; keep its opener in the normal menu layer.
            var dialogCanvas = _panel.GetComponent<Canvas>();
            if (dialogCanvas == null) dialogCanvas = _panel.AddComponent<Canvas>();
            if (_panel.GetComponent<GraphicRaycaster>() == null) _panel.AddComponent<GraphicRaycaster>();
            _panel.SetActive(false);
            RoomUiElements.TopMenuButton(_openButton, 1);
            _openButton.onClick.AddListener(Toggle);
            _panel.transform.Find("Close").GetComponent<Button>().onClick.AddListener(Close);
            _title = _panel.transform.Find("JobName").GetComponent<TMP_Text>();
            _title.rectTransform.sizeDelta = new Vector2(350,42);
            _title.rectTransform.anchoredPosition = new Vector2(77.5f,287);
            _requirement = _panel.transform.Find("Requirement").GetComponent<TMP_Text>();
            _description = _panel.transform.Find("Description").GetComponent<TMP_Text>();
            _portrait = _panel.transform.Find("Portrait").GetComponent<Image>();
            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                _tabs[i] = _panel.transform.Find("Job" + i).GetComponent<Button>();
                _tabLabels[i] = _tabs[i].GetComponentInChildren<TMP_Text>();
                _tabs[i].onClick.AddListener(() => Select(index));
            }
            for (int i = 0; i < _cards.Length; i++)
            {
                int card = i; _cards[i] = _panel.transform.Find("Skill" + i);
                for (int slot = 0; slot < 2; slot++)
                { int equip = slot; _cards[i].Find("Equip" + slot).GetComponent<Button>().onClick.AddListener(() => Equip(card, equip)); }
            }
            _save = _panel.transform.Find("Save").GetComponent<Button>(); _save.onClick.AddListener(Save);
            _status = _panel.transform.Find("Footer").GetComponent<TMP_Text>();
            BuildPassives();
        }
        private void OnEnable() { StatManager.LocalChanged += Bind; Bind(StatManager.Local); }
        private void OnDisable()
        {
            StatManager.LocalChanged -= Bind;
            if (_local != null) _local.StatsChanged -= RefreshStats;
            if (_loadout != null) _loadout.Changed -= OnLoadoutChanged;
            _local = null;
            BindPassives(null);
            EndPassiveDrag();
            if (_panel != null) _panel.SetActive(false);
            GameInputController.RefreshCursorState();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void Bind(StatManager stats)
        {
            if (OwnsLocalView) Instance = this;
            else if (Instance == this) Instance = null;
            if (_local != null) _local.StatsChanged -= RefreshStats;
            if (_loadout != null) _loadout.Changed -= OnLoadoutChanged;
            _local = stats;
            BindPassives(_local != null ? _local.GetComponent<PassiveLoadout>() : null);
            _loadout = _local != null ? _local.GetComponent<SkillLoadout>() : null;
            if (_loadout != null) _loadout.Changed += OnLoadoutChanged;
            if (_local != null) _local.StatsChanged += RefreshStats;
            if (IsOpen) Select(_selected);
        }
        private void RefreshStats(StatContainer _) { if (IsOpen) Select(_selected); }
        private void Update()
        {
            FlushPassiveSelection();
            // Lobby can display the guide without the FPS input manager.
            if (IsOpen && GameInputController.Instance == null && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
                GameInputController.HandleEscape();
        }
        public void Toggle() { if (IsOpen) Close(); else Open(); }
        public void Open()
        {
            if (!OwnsLocalView || !SkillLoadout.CanEdit) return;
            Instance = this;
            CharacterSelectionPanel.CloseIfOpen(); WeaponSelectionPanel.CloseIfOpen();
            if (GameSettingsPanel.IsOpen) GameSettingsPanel.Instance.Cancel();
            if (WaitingRoomTerminal.IsOpen) WaitingRoomTerminal.Instance.Close();
            CharacterInfoController.CloseOpenPanel();
            LobbyUIManager.Instance?.CloseInputPanels();
            _draft = _loadout != null ? _loadout.Snapshot() : SkillLoadoutStore.Read();
            _passivePanel.SetActive(false);
            _panel.SetActive(true);
            // Enabling a newly nested canvas can restore its parent's sort settings.
            var dialogCanvas = _panel.GetComponent<Canvas>();
            dialogCanvas.overrideSorting = true; dialogCanvas.sortingOrder = 250;
            Select(_local != null ? JobGuideContent.IndexOf(_local.CurrentIdentity) : 0);
            GameInputController.RefreshCursorState();
        }
        public void Close()
        {
            EndPassiveDrag();
            _panel.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            GameInputController.RefreshCursorState();
        }
        public static void CloseIfOpen() { if (IsOpen) Instance.Close(); }
        private bool OwnsLocalView
        {
            get
            {
                var owner = GetComponentInParent<Mirror.NetworkIdentity>();
                return owner == null || (!Mirror.NetworkClient.active && !Mirror.NetworkServer.active) || owner.isLocalPlayer;
            }
        }
        public void Select(int index)
        {
            if (_draft == null) _draft = _loadout != null ? _loadout.Snapshot() : SkillLoadoutStore.Read();
            _selected = Mathf.Clamp(index, 0, JobGuideContent.Count - 1);
            Identity identity = JobGuideContent.IdentityAt(_selected);
            int current = _local != null ? JobGuideContent.IndexOf(_local.CurrentIdentity) : -1;
            var job = SkillGameData.Instance != null ? System.Array.Find(SkillGameData.Instance.Jobs, j => j.Index == _selected) : null;
            _title.text = job != null ? SkillGameData.Text(job.NameKey) : JobGuideContent.Name(_selected);
            _requirement.text = job != null ? SkillGameData.Text(job.RequirementKey) : JobGuideContent.Requirement(_selected);
            _description.text = job != null ? SkillGameData.Text(job.DescriptionKey) : JobGuideContent.Description(_selected);
            _portrait.sprite = _portraits != null ? _portraits.Resolve(identity) : null;
            _portrait.enabled = _portrait.sprite != null;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].GetComponent<Image>().color = i == _selected ? new Color(.10f, .31f, .38f, 1) : new Color(.035f, .08f, .13f, 1);
                var tabJob = SkillGameData.Instance != null ? System.Array.Find(SkillGameData.Instance.Jobs, j=>j.Index==i) : null;
                _tabLabels[i].text = (tabJob != null ? SkillGameData.Text(tabJob.NameKey) : JobGuideContent.Name(i)) +
                    (i == current ? "\n<size=14><color=#63ECE5>"+SkillGameData.Text("UI_Skill_Current","현재 직업")+"</color></size>" : "");
            }
            var candidates = Candidates();
            for (int slot = 0; slot < _cards.Length; slot++)
            {
                bool available = slot < candidates.Count;
                JobSkillKind kind = available ? candidates[slot] : default;
                JobSkillData data = available ? System.Array.Find(_skills, s => s != null && s.SkillKind == kind) : null;
                _cards[slot].gameObject.SetActive(data != null);
                if (data == null) continue;
                _cards[slot].Find("Name").GetComponent<TMP_Text>().text = data.DisplayName;
                _cards[slot].Find("Effect").GetComponent<TMP_Text>().text = SkillDescription.Effect(data);
                _cards[slot].Find("Timing").GetComponent<TMP_Text>().text = string.Format(SkillGameData.Text("UI_SkillTiming", "시전 {0:0.#}초 · 재사용 {1:0.#}초"), data.CastSeconds, data.CooldownSeconds * (_passives?.CooldownMultiplier ?? 1f));
                var icon = _cards[slot].Find("Icon").GetComponent<Image>();
                icon.sprite = data.IconSprite; icon.enabled = icon.sprite != null;
                for (int equip = 0; equip < 2; equip++)
                {
                    var button = _cards[slot].Find("Equip" + equip).GetComponent<Button>();
                    bool selected = _draft[_selected * 2 + equip] == (int)kind;
                    button.interactable = SkillLoadout.CanEdit;
                    button.GetComponent<Image>().color = selected ? new Color(.12f,.5f,.53f) : new Color(.06f,.12f,.18f);
                    string key=(equip == 0 ? LocalGameSettings.Current.skill1 : LocalGameSettings.Current.skill2).ToUpperInvariant();
                    button.GetComponentInChildren<TMP_Text>().text = selected ? "["+key+"]" : key;
                }
            }
            _save.interactable = SkillLoadout.CanEdit;
            _status.text = SkillLoadout.CanEdit ? "스킬 두 개를 선택한 뒤 저장하세요. 사망 중 변경한 스킬은 부활 후 사용합니다." : "생존 중에는 스킬을 변경할 수 없습니다.";
        }
        private System.Collections.Generic.List<JobSkillKind> Candidates()
        {
            var result = new System.Collections.Generic.List<JobSkillKind>();
            if (SkillGameData.Instance != null) { foreach (var row in SkillGameData.Instance.Pools) if (row.Job == _selected) result.Add((JobSkillKind)row.Kind); }
            else { for (int slot=0;slot<2;slot++) if (CombatSkillRules.TrySelect(JobGuideContent.IdentityAt(_selected),slot,out var kind)) result.Add(kind); }
            return result;
        }
        private void Equip(int card, int slot)
        {
            if (!SkillLoadout.CanEdit) return;
            var choices = Candidates(); if (card >= choices.Count) return;
            int at = _selected * 2 + slot, other = _selected * 2 + 1 - slot, kind = (int)choices[card];
            if (_draft[other] == kind) _draft[other] = _draft[at];
            _draft[at] = kind; Select(_selected);
        }
        private void Save()
        {
            if (!SkillLoadout.CanEdit || !SkillLoadout.Validate(_draft)) return;
            if (_loadout != null) _loadout.Request(_draft);
            else SkillLoadoutStore.Save(_draft);
            if (!Mirror.NetworkClient.active) _status.text = SkillGameData.Text("UI_Skill_Saved");
        }
        private void OnLoadoutChanged()
        {
            if (!IsOpen) return;
            _draft = _loadout != null ? _loadout.Snapshot() : SkillLoadoutStore.Read(); Select(_selected);
            _status.text = SkillGameData.Text("UI_Skill_Saved");
        }
    }
}
