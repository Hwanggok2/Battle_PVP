using System.Collections.Generic;
using BattlePvp.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed partial class JobGuidePanel
    {
        private GameObject _passivePanel, _dragGhost;
        private PassiveLoadout _passives;
        private PassivePresetBook _presetBook;
        private string _selectedPreset, _pendingPreset;
        private int[] _passiveDraft, _pendingChoices;
        private int _chosenPassive;
        private TMP_Text _passiveStatus, _presetTitle, _activeBadge;
        private Button _passiveSave, _addPreset;
        private RectTransform _presetContent;
        private ScrollRect _presetScroll;
        private readonly List<PresetRow> _presetRows = new();
        private readonly List<Image> _passiveCards = new();
        private sealed class PresetRow
        {
            public string Id;
            public Image Background;
            public TMP_Text Badge;
            public TMP_InputField Name;
            public Button Delete;
            public readonly Image[] Icons = new Image[2];
            public readonly TMP_Text[] Labels = new TMP_Text[2];
            public readonly Button[] Clear = new Button[2];
        }
        private static readonly string[] PassiveNames = { "비어 있음", "반격", "배후 공격", "정화", "회복 방패", "뇌진탕",
            "광전사", "저격", "승전보", "학자", "이단 점프", "고속", "충만한 체력", "철갑" };
        private static readonly string[] PassiveDescriptions = {
            "방어·패링 후 2초 내 근접 피해 +10%\n발동 간격 5초",
            "후방 근접 피해 +10%·기절 1초\n대상별 재발동 8초",
            "덫·기절·둔화·중독\n지속시간 15% 감소",
            "방패 방어 시 최대 체력 2% 회복\n발동 간격 6초",
            "같은 적 머리 4초 내 4회 타격\n1초 동안 기절",
            "잃은 체력에 비례해 공격력·공속\n각각 최대 15% 상승",
            "활·나이프·갈고리 등\n원거리 공격 피해 12% 증가",
            "적 처치 시\n최대 체력 20% 회복",
            "스킬 재사용·충전 시간\n12% 감소",
            "공중에서 한 번 더 점프\n착지하면 재사용 가능",
            "이동속도·공격속도\n10% 증가",
            "최대 체력\n10% 증가",
            "방어력 10% 증가\n기존 방어 상한 적용" };

        private TMP_Text PassiveText(string name, Transform parent, string value, Vector2 size, Vector2 position, int fontSize)
            => RoomUiElements.Text(name, parent, BattleResultTheme.SharedFont, value, size, position, fontSize);
        private Button PassiveButton(string name, Transform parent, string value, Vector2 size, Vector2 position, int fontSize=20)
        {
            var button=RoomUiElements.Button(name,parent,BattleResultTheme.SharedFont,value,size,position);
            button.GetComponentInChildren<TMP_Text>().fontSize=fontSize;
            return button;
        }
        private void BuildPassives()
        {
            PassiveButton("Passives",_panel.transform,"패시브 설정",new Vector2(178,38),new Vector2(401,289)).onClick.AddListener(OpenPassives);
            var panel=RoomUiElements.Rect("PassiveSelection",_panel.transform,new Vector2(1040,840),Vector2.zero);
            _passivePanel=panel.gameObject;
            panel.gameObject.AddComponent<Image>().color=new Color(.025f,.055f,.085f,1);
            PassiveText("Title",panel,"패시브 설정",new Vector2(430,48),new Vector2(-265,376),30);
            PassiveButton("Back",panel,"직업·스킬",new Vector2(150,38),new Vector2(165,376)).onClick.AddListener(()=>
            { EndPassiveDrag(); _passivePanel.SetActive(false); Select(_selected); });
            _passiveSave=PassiveButton("Save",panel,"적용",new Vector2(100,38),new Vector2(310,376));
            _passiveSave.onClick.AddListener(SavePassives);
            PassiveButton("Close",panel,"닫기",new Vector2(100,38),new Vector2(435,376)).onClick.AddListener(Close);
            PassiveText("PresetsTitle",panel,"프리셋",new Vector2(280,32),new Vector2(-355,316),22);
            _presetTitle=PassiveText("SelectedPreset",panel,"",new Vector2(470,36),new Vector2(60,316),23);
            _activeBadge=PassiveText("ActiveBadge",panel,"",new Vector2(138,32),new Vector2(429,316),19);
            _activeBadge.alignment=TextAlignmentOptions.Right;
            _activeBadge.color=new Color(.36f,.94f,.74f);
            _passiveStatus=PassiveText("Status",panel,"",new Vector2(970,28),new Vector2(0,-394),15);
            _presetScroll=PassiveScroll("Presets",panel,new Vector2(280,600),new Vector2(-355,-3),out _presetContent);
            _addPreset=PassiveButton("AddPreset",panel,"+ 프리셋 추가",new Vector2(280,46),new Vector2(-355,-343));
            _addPreset.onClick.AddListener(AddPassivePreset);
            PassiveScroll("List",panel,new Vector2(676,660),new Vector2(152,-38),out var content);
            content.sizeDelta=new Vector2(676,7*136+12);
            for(int i=0;i<13;i++)
            {
                int kind=i+1;
                var card=RoomUiElements.Rect("Passive"+kind,content,new Vector2(326,124),new Vector2(i%2==0?-170:170,-6-i/2*136));
                card.anchorMin=card.anchorMax=new Vector2(.5f,1); card.pivot=new Vector2(.5f,1);
                var background=card.gameObject.AddComponent<Image>(); background.color=new Color(.055f,.12f,.17f,1);
                _passiveCards.Add(background);
                var drag=card.gameObject.AddComponent<PassiveDragItem>(); drag.Owner=this; drag.Kind=kind;
                PassiveIconView.Create(card,"Icon",new Vector2(-114,0),76,kind);
                PassiveText("Name",card,PassiveNames[kind],new Vector2(205,32),new Vector2(46,35),22).fontStyle=FontStyles.Bold;
                var description=PassiveText("Description",card,PassiveDescriptions[i],new Vector2(205,67),new Vector2(46,-15),16);
                description.color=new Color(.67f,.79f,.84f);
                description.textWrappingMode=TextWrappingModes.Normal;
            }
            _passivePanel.SetActive(false);
        }
        private ScrollRect PassiveScroll(string name,Transform panel,Vector2 size,Vector2 position,out RectTransform content)
        {
            var viewport=RoomUiElements.Rect(name,panel,size,position);
            viewport.gameObject.AddComponent<Image>().color=new Color(.02f,.045f,.07f,1);
            viewport.gameObject.AddComponent<RectMask2D>();
            content=RoomUiElements.Rect("Content",viewport,size,Vector2.zero);
            content.anchorMin=content.anchorMax=new Vector2(.5f,1); content.pivot=new Vector2(.5f,1);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
            var barRect=RoomUiElements.Rect(name+"ScrollBar",panel,new Vector2(6,size.y),position+new Vector2(size.x*.5f+6,0));
            barRect.gameObject.AddComponent<Image>().color=new Color(.06f,.13f,.18f);
            var handle=RoomUiElements.Rect("Handle",barRect,Vector2.zero,Vector2.zero);
            handle.anchorMin=Vector2.zero; handle.anchorMax=Vector2.one;
            var handleImage=handle.gameObject.AddComponent<Image>(); handleImage.color=new Color(.2f,.65f,.69f);
            var bar=barRect.gameObject.AddComponent<Scrollbar>(); bar.handleRect=handle; bar.targetGraphic=handleImage;
            bar.direction=Scrollbar.Direction.BottomToTop; bar.value=1; scroll.verticalScrollbar=bar;
            return scroll;
        }
        private void BuildPresetRows()
        {
            for(int i=_presetContent.childCount-1;i>=0;i--)
            {
                var child=_presetContent.GetChild(i).gameObject;
                child.SetActive(false); if(Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _presetRows.Clear();
            _presetContent.sizeDelta=new Vector2(280,_presetBook.Entries.Count*98+8);
            for(int i=0;i<_presetBook.Entries.Count;i++)
            {
                var entry=_presetBook.Entries[i]; string id=entry.Id;
                var root=RoomUiElements.Rect("Preset-"+id,_presetContent,new Vector2(276,94),new Vector2(0,-4-i*98));
                root.anchorMin=root.anchorMax=new Vector2(.5f,1); root.pivot=new Vector2(.5f,1);
                var row=new PresetRow {Id=id,Background=root.gameObject.AddComponent<Image>()};
                var button=root.gameObject.AddComponent<Button>(); button.targetGraphic=row.Background;
                button.navigation=new Navigation {mode=Navigation.Mode.None};
                button.onClick.AddListener(()=>SelectPassivePreset(id));
                row.Name=RoomUiElements.Input("Name",root,BattleResultTheme.SharedFont,"프리셋 이름",new Vector2(178,24),new Vector2(-38,32));
                row.Name.characterLimit=18; row.Name.textComponent.fontSize=18;
                row.Name.SetTextWithoutNotify(entry.Name);
                row.Name.onSelect.AddListener(_=>SelectPassivePreset(id));
                row.Name.onEndEdit.AddListener(value=>RenamePassivePreset(id,value));
                row.Badge=PassiveText("ActiveBadge",root,"",new Vector2(52,24),new Vector2(81,32),13);
                row.Badge.alignment=TextAlignmentOptions.Right; row.Badge.color=new Color(.36f,.94f,.74f);
                row.Delete=PassiveButton("DeletePreset",root,"×",new Vector2(22,22),new Vector2(123,32),17);
                row.Delete.onClick.AddListener(()=>DeletePassivePreset(id));
                for(int slot=0;slot<2;slot++)
                {
                    int at=slot; float x=slot==0?-66:66;
                    row.Icons[slot]=PassiveIconView.Create(root,"Slot"+slot,new Vector2(x,-5),46,entry.Choices[slot]);
                    var drop=row.Icons[slot].transform.parent.gameObject.AddComponent<PassiveDropSlot>();
                    drop.Owner=this; drop.PresetId=id; drop.Slot=slot;
                    row.Labels[slot]=PassiveText("SlotLabel"+slot,root,"",new Vector2(125,20),new Vector2(x,-36),13);
                    row.Labels[slot].alignment=TextAlignmentOptions.Center;
                    row.Clear[slot]=PassiveButton("Clear"+slot,root,"×",new Vector2(21,21),new Vector2(x+31,7),17);
                    row.Clear[slot].onClick.AddListener(()=>DropPassive(id,at,0));
                }
                _presetRows.Add(row);
            }
        }
        private void BindPassives(PassiveLoadout next)
        {
            if(_passives!=null) { _passives.Changed-=PassivesChanged; _passives.RequestCompleted-=PassiveRequestCompleted; }
            if(_passives!=next) { _pendingPreset=null; _pendingChoices=null; }
            _passives=next;
            if(_passives!=null) { _passives.Changed+=PassivesChanged; _passives.RequestCompleted+=PassiveRequestCompleted; }
        }
        private void OpenPassives()
        {
            if(_pendingPreset==null)
            {
                _presetBook=PassivePresetStore.Read();
                _selectedPreset=_presetBook.ActiveId ?? (_presetBook.Entries.Count>0 ? _presetBook.Entries[0].Id : null);
                _chosenPassive=0; _passiveDraft=(int[]) (_presetBook.Find(_selectedPreset)?.Choices ?? new int[2]).Clone();
                BuildPresetRows();
            }
            _passivePanel.SetActive(true); RefreshPassives();
            Canvas.ForceUpdateCanvases();
            foreach(var scroll in _passivePanel.GetComponentsInChildren<ScrollRect>())
            { scroll.StopMovement(); scroll.verticalNormalizedPosition=1; }
        }
        private int[] CurrentPassives() => _passives!=null ? _passives.Snapshot() : PassiveStore.Read();
        private bool IsActivePreset(PassivePreset entry) => entry!=null && entry.Id==_presetBook.ActiveId && PassivePresetBook.Same(entry.Choices,CurrentPassives());
        private void RefreshPassives()
        {
            if(_presetBook==null) return;
            bool editable=PassiveLoadout.CanEdit && _pendingPreset==null;
            var selected=_presetBook.Find(_selectedPreset);
            _presetTitle.text=selected!=null ? selected.Name : "프리셋을 추가하세요";
            _activeBadge.text=selected==null ? "" : IsActivePreset(selected) ? "사용 중" : "선택 중";
            foreach(var row in _presetRows)
            {
                var entry=_presetBook.Find(row.Id);
                row.Background.color=row.Id==_selectedPreset ? new Color(.08f,.25f,.30f) : new Color(.045f,.095f,.135f);
                row.Badge.text=IsActivePreset(entry) ? "사용 중" : "";
                if(row.Name!=null) row.Name.interactable=editable;
                row.Delete.gameObject.SetActive(row.Id==_selectedPreset);
                row.Delete.interactable=editable;
                for(int slot=0;slot<2;slot++)
                {
                    int kind=entry.Choices[slot];
                    row.Icons[slot].sprite=PassiveIconView.Icon(kind); row.Icons[slot].enabled=kind!=0;
                    row.Labels[slot].text=kind==0 ? "끌어 놓기" : PassiveNames[kind];
                    row.Clear[slot].gameObject.SetActive(editable && kind!=0);
                }
            }
            for(int i=0;i<_passiveCards.Count;i++)
                _passiveCards[i].color=_chosenPassive==i+1 ? new Color(.1f,.31f,.37f) : new Color(.055f,.12f,.17f,1);
            _passiveSave.interactable=editable && selected!=null; _addPreset.interactable=editable;
            _passiveStatus.text=!PassiveLoadout.CanEdit ? "대기실 또는 사망 중 패시브를 변경할 수 있습니다." :
                _pendingPreset!=null ? "서버에서 적용을 확인하고 있습니다." :
                selected==null ? "+ 프리셋 추가로 새 구성을 만드세요. 현재 장착한 패시브는 유지됩니다." :
                _chosenPassive>0 ? PassiveNames[_chosenPassive]+" · 왼쪽 장착 칸을 선택하세요." :
                "패시브를 왼쪽 칸으로 끌어 놓거나, 이미지와 칸을 차례로 클릭하세요. 적용하면 전투에 사용됩니다.";
        }
        private void SelectPassivePreset(string id)
        {
            if(_pendingPreset!=null || _presetBook.Find(id)==null) return;
            _selectedPreset=id; _passiveDraft=(int[])_presetBook.Find(id).Choices.Clone(); RefreshPassives();
        }
        private void AddPassivePreset()
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null) return;
            var entry=_presetBook.Add(); PassivePresetStore.Save(_presetBook);
            BuildPresetRows(); SelectPassivePreset(entry.Id); Canvas.ForceUpdateCanvases();
            _presetScroll.StopMovement(); _presetScroll.verticalNormalizedPosition=0;
        }
        private void RenamePassivePreset(string id,string value)
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null) return;
            var entry=_presetBook.Find(id); if(entry==null) return;
            if(!string.IsNullOrWhiteSpace(value)) entry.Name=value.Trim();
            var row=_presetRows.Find(p=>p.Id==id); row?.Name?.SetTextWithoutNotify(entry.Name);
            PassivePresetStore.Save(_presetBook); RefreshPassives();
        }
        private void DeletePassivePreset(string id)
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null || id!=_selectedPreset) return;
            int index=_presetBook.Entries.FindIndex(entry=>entry.Id==id);
            if(!_presetBook.Remove(id)) return;
            EndPassiveDrag(); _chosenPassive=0;
            _selectedPreset=_presetBook.Entries.Count>0 ? _presetBook.Entries[Mathf.Min(index,_presetBook.Entries.Count-1)].Id : null;
            _passiveDraft=(int[])(_presetBook.Find(_selectedPreset)?.Choices ?? new int[2]).Clone();
            PassivePresetStore.Save(_presetBook); BuildPresetRows(); RefreshPassives();
            _passiveStatus.text="프리셋을 삭제했습니다. 현재 장착한 패시브는 유지됩니다.";
        }
        public void ChoosePassive(int kind)
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null || kind<1 || kind>13) return;
            _chosenPassive=kind; RefreshPassives();
        }
        public void ClickPassiveSlot(string id,int slot,bool clear)
        {
            if(clear) DropPassive(id,slot,0);
            else if(_chosenPassive>0) DropPassive(id,slot,_chosenPassive);
            else SelectPassivePreset(id);
        }
        public void DropPassive(string id,int slot,int kind)
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null || !_presetBook.SetSlot(id,slot,kind)) return;
            _chosenPassive=0; PassivePresetStore.Save(_presetBook); SelectPassivePreset(id);
        }
        private void EquipPassive(int kind,int slot)
            => DropPassive(_selectedPreset,slot,_passiveDraft[slot]==kind?0:kind);
        private void SavePassives()
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null || _presetBook.Find(_selectedPreset)==null || !PassiveLoadout.Validate(_passiveDraft)) return;
            _pendingPreset=_selectedPreset; _pendingChoices=(int[])_passiveDraft.Clone(); RefreshPassives();
            if(_passives!=null) { if(!_passives.Request(_pendingChoices)) PassiveRequestCompleted(false); }
            else { PassiveStore.Save(_pendingChoices); PassiveRequestCompleted(true); }
        }
        private void PassiveRequestCompleted(bool accepted)
        {
            if(_pendingPreset==null) return;
            accepted=accepted && PassivePresetBook.Same(CurrentPassives(),_pendingChoices);
            if(accepted) { _presetBook.ActiveId=_pendingPreset; PassivePresetStore.Save(_presetBook); }
            _pendingPreset=null; _pendingChoices=null; RefreshPassives();
            _passiveStatus.text=accepted ? "패시브 프리셋을 적용했습니다." : "적용되지 않았습니다. 대기실 또는 사망 중 다시 적용해 주세요.";
        }
        private void PassivesChanged() { if(_passivePanel!=null && _passivePanel.activeInHierarchy) RefreshPassives(); }
        public void BeginPassiveDrag(int kind,PointerEventData e)
        {
            if(!PassiveLoadout.CanEdit || _pendingPreset!=null) return;
            EndPassiveDrag();
            _dragGhost=PassiveIconView.Create(_passivePanel.transform,"DraggedPassive",Vector2.zero,76,kind).transform.parent.gameObject;
            _dragGhost.AddComponent<CanvasGroup>().blocksRaycasts=false;
            MovePassiveDrag(e);
        }
        public void MovePassiveDrag(PointerEventData e)
        {
            if(_dragGhost==null) return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_passivePanel.transform,e.position,e.pressEventCamera,out var point))
                ((RectTransform)_dragGhost.transform).anchoredPosition=point;
        }
        public void EndPassiveDrag()
        {
            if(_dragGhost==null) return;
            var ghost=_dragGhost; _dragGhost=null; ghost.SetActive(false);
            if(Application.isPlaying) Destroy(ghost); else DestroyImmediate(ghost);
        }
    }
}
