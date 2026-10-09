using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class WeaponSelectionPanel : MonoBehaviour
    {
        public static WeaponSelectionPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeSelf;
        private GameObject _panel;
        private TMP_Text _status;
        private readonly Button[] _choices = new Button[4];
        private void Awake()
        {
            Instance = this;
            var font = BattleResultTheme.SharedFont;
            var open = RoomUiElements.Button("Weapons", transform, font, "무기", new Vector2(116,44), Vector2.zero);
            RoomUiElements.TopMenuButton(open, -1); open.onClick.AddListener(Toggle);
            var panel = RoomUiElements.Rect("Weapon selection", transform, Vector2.zero, Vector2.zero);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.gameObject.AddComponent<Image>().color = new Color(0,.015f,.025f,.8f); _panel = panel.gameObject;
            var dialog = RoomUiElements.Rect("Dialog", panel, new Vector2(950, 620), Vector2.zero);
            dialog.gameObject.AddComponent<Image>().color = new Color(.025f,.055f,.09f,.99f);
            RoomUiElements.Text("Title", dialog, font, "무기 선택", new Vector2(720,50), new Vector2(-60,255),30);
            RoomUiElements.Button("Close", dialog, font, "닫기", new Vector2(88,38),new Vector2(395,255)).onClick.AddListener(Close);
            for (int i=0;i<4;i++)
            {
                int slot=i; var entry=WeaponCatalog.Instance?.Find((MeleeWeaponKind)i);
                _choices[i]=RoomUiElements.Button("Weapon "+i,dialog,font,entry?.Name ?? "무기",new Vector2(190,88),new Vector2(-330,160-i*112));
                _choices[i].onClick.AddListener(()=>Select((MeleeWeaponKind)slot));
                var description=RoomUiElements.Text("Description "+i,dialog,font,entry?.Description ?? "",new Vector2(620,88),new Vector2(115,160-i*112),20);
                description.alignment=TextAlignmentOptions.MidlineLeft;
            }
            _status=RoomUiElements.Text("Status",dialog,font,"",new Vector2(870,44),new Vector2(0,-266),19);
            _panel.SetActive(false);
        }
        private void OnDestroy() { if (Instance==this) Instance=null; }
        private void Toggle()
        {
            if(IsOpen) { Close(); return; }
            if (!WeaponLoadout.CanEdit) return;
            CharacterSelectionPanel.CloseIfOpen(); JobGuidePanel.CloseIfOpen(); CharacterInfoController.CloseOpenPanel();
            LobbyUIManager.Instance?.CloseInputPanels();
            if(GameSettingsPanel.IsOpen) GameSettingsPanel.Instance.Cancel();
            if(WaitingRoomTerminal.IsOpen) WaitingRoomTerminal.Instance.Close();
            _panel.SetActive(true); Refresh(); GameInputController.RefreshCursorState();
        }
        private void Update() { if(IsOpen) Refresh(); }
        private void Refresh()
        {
            var loadout=StatManager.Local != null ? StatManager.Local.GetComponent<WeaponLoadout>() : null;
            for(int i=0;i<4;i++)
            {
                _choices[i].interactable=loadout!=null && WeaponLoadout.CanEdit;
                _choices[i].GetComponent<Image>().color=loadout!=null && (int)loadout.Selected==i ? new Color(.08f,.36f,.4f) : new Color(.04f,.12f,.17f);
            }
            _status.text=!WeaponLoadout.CanEdit ? "전투 중에는 팔방미인이거나 사망한 상태에서 무기를 변경할 수 있습니다." : loadout==null ? "플레이어를 기다리고 있습니다." :
                "사용 중 · "+WeaponCatalog.Instance.Find(loadout.Selected).Name+"   |   공격·막기를 마친 뒤 장착할 수 있습니다.";
        }
        private void Select(MeleeWeaponKind kind) { StatManager.Local?.GetComponent<WeaponLoadout>()?.Request(kind); Refresh(); }
        public void Close() { if(_panel==null) return; _panel.SetActive(false); UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null); GameInputController.RefreshCursorState(); }
        public static void CloseIfOpen() { if(IsOpen) Instance.Close(); }
    }
}
