using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class PassivePresetUiTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
        private readonly Dictionary<string,string> _prefs=new();
        private GameObject _player,_root,_events;
        private JobGuidePanel _panel;
        private Scene _scene;
        private string _sceneName;
        private object _previousLocal,_previousPanel;
        private static FieldInfo Local=>typeof(StatManager).GetField("<Local>k__BackingField",Static);
        private static FieldInfo Instance=>typeof(JobGuidePanel).GetField("<Instance>k__BackingField",Static);
        private static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,Private|BindingFlags.Public).Invoke(obj,args);
        private static T Get<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Private).GetValue(obj);
        private static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private).SetValue(obj,value);
        private void Remember(string key) { _prefs[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetString(key):null; PlayerPrefs.DeleteKey(key); }
        [SetUp] public void Setup()
        {
            Remember(PassivePresetStore.Key);
            Remember((string)typeof(PassiveStore).GetProperty("Key",Static).GetValue(null));
            _scene=SceneManager.GetActiveScene(); _sceneName=_scene.name; _scene.name="Lobby";
            _previousLocal=Local.GetValue(null); _previousPanel=Instance.GetValue(null); Instance.SetValue(null,null);
            _player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(_player);
            EditorTestLifecycle.Invoke(_player.GetComponent<PassiveLoadout>(),"Awake");
            Local.SetValue(null,_player.GetComponent<StatManager>());
        }
        [TearDown] public void Cleanup()
        {
            if(_root!=null) { Call(_panel,"OnDisable"); Object.DestroyImmediate(_root); }
            if(_events!=null) Object.DestroyImmediate(_events);
            Object.DestroyImmediate(_player); Local.SetValue(null,_previousLocal); Instance.SetValue(null,_previousPanel);
            _scene.name=_sceneName;
            foreach(var item in _prefs) { if(item.Value==null) PlayerPrefs.DeleteKey(item.Key); else PlayerPrefs.SetString(item.Key,item.Value); }
            _prefs.Clear(); PlayerPrefs.Save();
        }
        private void Open()
        {
            _root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/JobGuide.prefab"));
            _panel=_root.GetComponent<JobGuidePanel>(); Call(_panel,"Awake"); Call(_panel,"Bind",_player.GetComponent<StatManager>());
            _panel.Open(); Call(_panel,"OpenPassives");
        }
        private PassivePresetBook Book=>Get<PassivePresetBook>(_panel,"_presetBook");
        private Transform Selection=>_root.transform.Find("Panel/PassiveSelection");
        private string Selected=>Get<string>(_panel,"_selectedPreset");

        [Test] public void LegacyPairMigratesOnceAndEveryPresetOwnsIndependentSlots()
        {
            PassiveStore.Save(new[]{11,12});
            var book=PassivePresetStore.Read(5);
            Assert.That(book.Entries.Count,Is.EqualTo(6)); Assert.That(book.ActiveId,Is.EqualTo("job-5"));
            Assert.That(book.Find("job-5").Choices,Is.EqualTo(new[]{11,12}));
            for(int i=0;i<5;i++) Assert.That(book.Entries[i].Choices,Is.EqualTo(new[]{0,0}));
            var custom=book.Add(); book.SetSlot(custom.Id,0,1); book.SetSlot(custom.Id,1,2);
            book.SetSlot("job-0",0,3); PassivePresetStore.Save(book);
            var restored=PassivePresetStore.Read(0);
            Assert.That(restored.ActiveId,Is.EqualTo("job-5"));
            Assert.That(restored.Find("job-5").Choices,Is.EqualTo(new[]{11,12}));
            Assert.That(restored.Find(custom.Id).Choices,Is.EqualTo(new[]{1,2}));
            Assert.That(restored.Find("job-0").Choices,Is.EqualTo(new[]{3,0}));
            restored.Find(custom.Id).Choices[0]=4;
            Assert.That(PassivePresetStore.Read().Find(custom.Id).Choices,Is.EqualTo(new[]{1,2}));
        }
        [Test] public void DuplicateDropSwapsAndInvalidDropCannotCorruptAnotherPreset()
        {
            var book=PassivePresetStore.Read(); string id=book.ActiveId;
            book.SetSlot(id,0,11); book.SetSlot(id,1,12); book.SetSlot(id,1,11);
            Assert.That(book.Find(id).Choices,Is.EqualTo(new[]{12,11}));
            Assert.That(book.SetSlot(id,2,1),Is.False); Assert.That(book.SetSlot("missing",0,1),Is.False);
            Assert.That(book.SetSlot(id,0,14),Is.False); Assert.That(book.Find("job-0").Choices,Is.EqualTo(new[]{0,0}));
            book.SetSlot(id,0,0); Assert.That(book.Find(id).Choices,Is.EqualTo(new[]{0,11}));
        }
        [Test] public void CustomPresetRenamesPersistsAndIsOnlyEquippedAfterApply()
        {
            Open(); string original=Book.ActiveId;
            Call(_panel,"AddPassivePreset"); string custom=Selected;
            Call(_panel,"RenamePassivePreset",custom,"돌격 조합");
            _panel.DropPassive(custom,0,11); _panel.DropPassive(custom,1,12);
            Assert.That(Book.Entries.Count,Is.EqualTo(7));
            Assert.That(Selection.Find("Presets/Content").childCount,Is.EqualTo(7));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{0,0}));
            Assert.That(Book.ActiveId,Is.EqualTo(original));
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("선택 중"));
            Call(_panel,"SavePassives");
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,12}));
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("사용 중"));
            _panel.Close(); _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Selected,Is.EqualTo(custom)); Assert.That(Book.Find(custom).Name,Is.EqualTo("돌격 조합"));
            Assert.That(Book.Find(original).Choices,Is.EqualTo(new[]{0,0}));
            Assert.That(Selection.Find("Presets/Content").childCount,Is.EqualTo(7),"Reopening does not leave duplicate rows.");
        }
        [Test] public void DragDropAndClickFallbackEquipIntoTheChosenOctagonalSlot()
        {
            Open(); _events=new GameObject("Passive UI events",typeof(EventSystem));
            var card=Selection.Find("List/Content/Passive7").gameObject;
            var e=new PointerEventData(_events.GetComponent<EventSystem>()) {pointerDrag=card,position=new Vector2(400,400)};
            ExecuteEvents.Execute(card,e,ExecuteEvents.beginDragHandler);
            Assert.That(Get<GameObject>(_panel,"_dragGhost").GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
            var slot=Selection.Find("Presets/Content/Preset-job-0/Slot0").gameObject;
            ExecuteEvents.Execute(slot,e,ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(card,e,ExecuteEvents.endDragHandler);
            Assert.That(Book.Find("job-0").Choices,Is.EqualTo(new[]{7,0}));
            Assert.That(Get<GameObject>(_panel,"_dragGhost"),Is.Null);
            _panel.ChoosePassive(11); _panel.ClickPassiveSlot("job-0",1,false);
            Assert.That(Book.Find("job-0").Choices,Is.EqualTo(new[]{7,11}));
            _panel.ClickPassiveSlot("job-0",0,true);
            Assert.That(Book.Find("job-0").Choices,Is.EqualTo(new[]{0,11}));
            _panel.BeginPassiveDrag(1,e); _panel.Close();
            Assert.That(Get<GameObject>(_panel,"_dragGhost"),Is.Null);
        }
        [Test] public void RejectedApplyNeverMarksDraftAsInUseAndAliveBattleIsReadOnly()
        {
            Open(); string active=Book.ActiveId;
            Call(_panel,"AddPassivePreset"); string custom=Selected; _panel.DropPassive(custom,0,11);
            Set(_panel,"_pendingPreset",custom); Set(_panel,"_pendingChoices",new[]{11,0});
            Call(_panel,"PassiveRequestCompleted",false);
            Assert.That(Book.ActiveId,Is.EqualTo(active)); Assert.That(Get<string>(_panel,"_pendingPreset"),Is.Null);
            _scene.name="Battle"; _panel.DropPassive(custom,1,12); Call(_panel,"SavePassives"); Call(_panel,"AddPassivePreset");
            Assert.That(Book.Find(custom).Choices,Is.EqualTo(new[]{11,0}));
            Assert.That(Book.Entries.Count,Is.EqualTo(7)); Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{0,0}));
        }
        [Test] public void ModalSortingCoversMenuAndEntryAlignsBelowCloseWithJobTitle()
        {
            for(int column=-1;column<=4;column++)
            {
                Assert.That(TopMenuLayout.IsVisible(column,false,false,false,true),Is.False);
                Assert.That(TopMenuLayout.IsVisible(column,true,true,true,true),Is.False);
                Assert.That(TopMenuLayout.IsVisible(column,false,false,false,false),Is.True);
            }
            Open(); var dialog=_root.transform.Find("Panel");
            Assert.That(dialog.GetComponent<Canvas>().overrideSorting,Is.True);
            Assert.That(dialog.GetComponent<Canvas>().sortingOrder,Is.GreaterThan(156));
            Assert.That(dialog.GetComponent<GraphicRaycaster>(),Is.Not.Null);
            var entry=(RectTransform)dialog.Find("Passives"); var title=(RectTransform)dialog.Find("JobName");
            Assert.That(entry.GetComponentInChildren<TMP_Text>().text,Is.EqualTo("패시브 설정"));
            Assert.That(entry.anchoredPosition.y+entry.sizeDelta.y/2,Is.EqualTo(title.anchoredPosition.y+title.sizeDelta.y/2).Within(.1f));
            Assert.That(entry.anchoredPosition.y,Is.LessThan(((RectTransform)dialog.Find("Close")).anchoredPosition.y));
        }
        [Test] public void AllThirteenCardsHaveUniqueSpritesOctagonMasksAndSmallerDescriptions()
        {
            Open(); var sprites=new HashSet<Sprite>();
            for(int kind=1;kind<=13;kind++)
            {
                var card=Selection.Find("List/Content/Passive"+kind); var sprite=PassiveIconView.Icon(kind);
                Assert.That(sprite,Is.Not.Null,"Icon "+kind); Assert.That(sprites.Add(sprite),Is.True);
                Assert.That(sprite.texture.width,Is.LessThanOrEqualTo(256));
                Assert.That(card.Find("Icon").GetComponent<OctagonGraphic>(),Is.Not.Null);
                Assert.That(card.Find("Icon").GetComponent<Mask>(),Is.Not.Null);
                Assert.That(card.Find("Description").GetComponent<TMP_Text>().fontSize,Is.LessThan(card.Find("Name").GetComponent<TMP_Text>().fontSize));
            }
        }
    }
}
