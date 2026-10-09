using BattlePvp.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed partial class JobGuidePanel
    {
        private GameObject _passivePanel;
        private PassiveLoadout _passives;
        private int[] _passiveDraft;
        private TMP_Text _passiveSlots, _passiveStatus;
        private Button _passiveSave;
        private readonly Button[,] _passiveEquip = new Button[13,2];
        private static readonly string[] PassiveNames = { "비어 있음", "반격", "배후 공격", "정화", "회복 방패", "뇌진탕",
            "광전사", "저격", "승전보", "학자", "이단 점프", "고속", "충만한 체력", "철갑" };
        private static readonly string[] PassiveDescriptions = {
            "방어·패링 성공 후 2초 안에 적중하는 다음 근접 공격 피해 +10% · 발동 간격 5초",
            "상대 뒤쪽에서 근접 공격 적중 시 피해 +10% · 발동 간격 4초",
            "덫·기절·둔화·중독 지속시간 15% 감소",
            "방패 방어 성공 시 최대 체력의 2% 회복 · 발동 간격 6초",
            "같은 상대의 머리를 4초 안에 4회 타격하면 1초 기절",
            "잃은 체력에 비례해 공격력·공격속도 상승 · 각각 최대 15%",
            "활·나이프·갈고리 등 원거리 공격 피해 12% 증가",
            "적 처치 시 최대 체력의 20% 회복",
            "스킬 재사용 및 충전 시간 12% 감소",
            "공중에서 점프를 한 번 더 사용 · 착지하면 재사용 가능",
            "이동속도·공격속도 10% 증가",
            "최대 체력 10% 증가",
            "방어력 10% 증가 · 기존 방어 상한 적용" };

        private void BuildPassives()
        {
            var font = BattleResultTheme.SharedFont;
            RoomUiElements.Button("Passives", _panel.transform, font, "패시브 · 2칸", new Vector2(170,38), new Vector2(180,376)).onClick.AddListener(OpenPassives);
            var panel = RoomUiElements.Rect("PassiveSelection", _panel.transform, new Vector2(1040,840), Vector2.zero);
            _passivePanel = panel.gameObject;
            panel.gameObject.AddComponent<Image>().color = new Color(.025f,.055f,.085f,1);
            RoomUiElements.Text("Title", panel, font, "패시브 선택", new Vector2(430,48), new Vector2(-265,376), 30);
            RoomUiElements.Button("Back", panel, font, "직업·스킬", new Vector2(150,38), new Vector2(165,376)).onClick.AddListener(()=> { _passivePanel.SetActive(false); Select(_selected); });
            _passiveSave = RoomUiElements.Button("Save", panel, font, "저장", new Vector2(100,38), new Vector2(310,376));
            _passiveSave.onClick.AddListener(SavePassives);
            RoomUiElements.Button("Close", panel, font, "닫기", new Vector2(100,38), new Vector2(435,376)).onClick.AddListener(Close);
            _passiveSlots = RoomUiElements.Text("Slots", panel, font, "", new Vector2(960,48), new Vector2(0,312), 23);
            _passiveStatus = RoomUiElements.Text("Status", panel, font, "", new Vector2(960,48), new Vector2(0,-383), 18);
            var viewport = RoomUiElements.Rect("List", panel, new Vector2(970,620), new Vector2(0,-20));
            viewport.gameObject.AddComponent<Image>().color = new Color(.03f,.07f,.10f,1);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = RoomUiElements.Rect("Content", viewport, new Vector2(960,7*156), Vector2.zero);
            content.anchorMin = content.anchorMax = new Vector2(.5f,1); content.pivot = new Vector2(.5f,1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
            var barRect = RoomUiElements.Rect("ScrollBar",panel,new Vector2(12,620),new Vector2(499,-20));
            barRect.gameObject.AddComponent<Image>().color = new Color(.06f,.13f,.18f);
            var handle = RoomUiElements.Rect("Handle",barRect,Vector2.zero,Vector2.zero);
            handle.anchorMin=Vector2.zero; handle.anchorMax=Vector2.one;
            var handleImage=handle.gameObject.AddComponent<Image>(); handleImage.color=new Color(.2f,.65f,.69f);
            var bar=barRect.gameObject.AddComponent<Scrollbar>(); bar.handleRect=handle; bar.targetGraphic=handleImage;
            bar.direction=Scrollbar.Direction.BottomToTop; bar.value=1; scroll.verticalScrollbar=bar;
            for (int i=0; i<13; i++)
            {
                int kind = i+1;
                var card = RoomUiElements.Rect("Passive"+kind, content, new Vector2(465,144), new Vector2(i%2==0 ? -242 : 242,-10-i/2*156));
                card.anchorMin=card.anchorMax=new Vector2(.5f,1); card.pivot=new Vector2(.5f,1);
                card.gameObject.AddComponent<Image>().color = new Color(.055f,.12f,.17f,1);
                RoomUiElements.Text("Name", card, font, PassiveNames[kind], new Vector2(425,30), new Vector2(0,51),24).fontStyle=FontStyles.Bold;
                var description=RoomUiElements.Text("Description",card,font,PassiveDescriptions[i],new Vector2(425,57),new Vector2(0,3),18);
                description.textWrappingMode=TextWrappingModes.Normal;
                for(int slot=0; slot<2; slot++)
                {
                    int at=slot;
                    var button=RoomUiElements.Button("Equip"+slot,card,font,"",new Vector2(200,31),new Vector2(slot==0 ? -111 : 111,-49));
                    button.GetComponentInChildren<TMP_Text>().fontSize=17;
                    button.onClick.AddListener(()=>EquipPassive(kind,at)); _passiveEquip[i,slot]=button;
                }
            }
            _passivePanel.SetActive(false);
        }
        private void BindPassives(PassiveLoadout next)
        {
            if (_passives != null) _passives.Changed -= PassivesChanged;
            _passives = next;
            if (_passives != null) _passives.Changed += PassivesChanged;
        }
        private void OpenPassives()
        {
            _passiveDraft = _passives != null ? _passives.Snapshot() : PassiveStore.Read();
            _passivePanel.SetActive(true); RefreshPassives();
            Canvas.ForceUpdateCanvases();
            var scroll=_passivePanel.GetComponentInChildren<ScrollRect>(); scroll.StopMovement(); scroll.verticalNormalizedPosition=1;
        }
        private void RefreshPassives()
        {
            _passiveSlots.text="1번 · "+PassiveNames[_passiveDraft[0]]+"     |     2번 · "+PassiveNames[_passiveDraft[1]];
            for(int i=0;i<13;i++) for(int slot=0;slot<2;slot++)
            {
                bool selected=_passiveDraft[slot]==i+1;
                var button=_passiveEquip[i,slot]; button.interactable=PassiveLoadout.CanEdit;
                button.GetComponent<Image>().color=selected ? new Color(.12f,.5f,.53f) : new Color(.06f,.16f,.21f);
                button.GetComponentInChildren<TMP_Text>().text=selected ? (slot+1)+"번 장착 중 · 해제" : (slot+1)+"번에 장착";
            }
            _passiveSave.interactable=PassiveLoadout.CanEdit;
            _passiveStatus.text="서로 다른 패시브를 최대 2개 선택하세요. 대기실·사망 중 변경할 수 있습니다.";
        }
        private void EquipPassive(int kind,int slot)
        {
            if (!PassiveLoadout.CanEdit) return;
            int other=1-slot;
            if(_passiveDraft[slot]==kind) _passiveDraft[slot]=0;
            else { if(_passiveDraft[other]==kind) _passiveDraft[other]=_passiveDraft[slot]; _passiveDraft[slot]=kind; }
            RefreshPassives();
        }
        private void SavePassives()
        {
            if(!PassiveLoadout.CanEdit || !PassiveLoadout.Validate(_passiveDraft)) return;
            _passiveStatus.text=Mirror.NetworkClient.active ? "서버에서 저장을 확인하고 있습니다." : "패시브를 저장했습니다.";
            if(_passives!=null) { if(!_passives.Request(_passiveDraft)) return; }
            else PassiveStore.Save(_passiveDraft);
        }
        private void PassivesChanged()
        {
            if(_passivePanel==null || !_passivePanel.activeInHierarchy) return;
            _passiveDraft=_passives.Snapshot(); RefreshPassives(); _passiveStatus.text="패시브를 저장했습니다.";
        }
    }
}
