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
        private void ClickPreset(string id)=>Selection.Find("Presets/Content/Preset-"+id).GetComponent<Button>().onClick.Invoke();

        [Test] public void LegacyPairMigratesOnceAndEveryPresetOwnsIndependentSlots()
        {
            PassiveStore.Save(new[]{11,12});
            var book=PassivePresetStore.Read();
            Assert.That(book.Entries.Count,Is.EqualTo(9)); Assert.That(book.ActiveId,Is.EqualTo("legacy-equipped"));
            Assert.That(book.Find("legacy-equipped").Choices,Is.EqualTo(new[]{11,12}));
            Assert.That(book.Find("concept-counter").Choices,Is.EqualTo(new[]{1,4}));
            var custom=book.Add(); book.SetSlot(custom.Id,0,1); book.SetSlot(custom.Id,1,2);
            book.SetSlot("concept-stats",0,3); PassivePresetStore.Save(book);
            var restored=PassivePresetStore.Read();
            Assert.That(restored.ActiveId,Is.EqualTo("legacy-equipped"));
            Assert.That(restored.Find("legacy-equipped").Choices,Is.EqualTo(new[]{11,12}));
            Assert.That(restored.Find(custom.Id).Choices,Is.EqualTo(new[]{1,2}));
            Assert.That(restored.Find("concept-stats").Choices,Is.EqualTo(new[]{3,13}));
            Assert.That(restored.Find("concept-survival").Choices,Is.EqualTo(new[]{3,12}));
            restored.Find(custom.Id).Choices[0]=4;
            Assert.That(PassivePresetStore.Read().Find(custom.Id).Choices,Is.EqualTo(new[]{1,2}));
        }
        [Test] public void ConceptsCoverAllPassivesWithoutAutomaticallyEquipping()
        {
            var book=PassivePresetStore.Read();
            Assert.That(book.Entries.Count,Is.EqualTo(8)); Assert.That(book.ActiveId,Is.Null);
            var covered=new HashSet<int>();
            foreach(var entry in book.Entries)
            {
                Assert.That(PassiveLoadout.Validate(entry.Choices),Is.True);
                foreach(int kind in entry.Choices) { Assert.That(kind,Is.GreaterThan(0)); covered.Add(kind); }
                foreach(var other in book.Entries)
                    if(entry!=other) Assert.That(entry.Choices,Is.Not.SameAs(other.Choices));
            }
            Assert.That(covered.Count,Is.EqualTo(13));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{0,0}));
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{0,0}));
        }
        [Test] public void ConceptMigrationPreservesEditedAndAppliedLegacyPresetsAndRunsOnlyOnce()
        {
            var book=new PassivePresetBook {ActiveId="job-2"};
            string[] names={"힘 특화 · STR","체력 특화 · CON","민첩 특화 · AGI","방어 특화 · DEF","전략가","팔방미인"};
            for(int i=0;i<names.Length;i++) book.Entries.Add(new PassivePreset {Id="job-"+i,Name=names[i]});
            book.Find("job-0").Name="내 방어 조합";
            book.SetSlot("job-1",0,11); book.SetSlot("job-2",0,7);
            var custom=book.Add(); custom.Name="내 공격 조합"; book.SetSlot(custom.Id,0,6);
            PassiveStore.Save(new[]{7,0}); PassivePresetStore.Save(book);
            var migrated=PassivePresetStore.Read();
            Assert.That(migrated.Entries.Count,Is.EqualTo(12));
            Assert.That(migrated.Find("job-0").Name,Is.EqualTo("내 방어 조합"));
            Assert.That(migrated.Find("job-1").Choices,Is.EqualTo(new[]{11,0}));
            Assert.That(migrated.ActiveId,Is.EqualTo("job-2"));
            Assert.That(migrated.Find("job-2").Choices,Is.EqualTo(new[]{7,0}));
            Assert.That(migrated.Find(custom.Id).Name,Is.EqualTo("내 공격 조합"));
            Assert.That(migrated.Find(custom.Id).Choices,Is.EqualTo(new[]{6,0}));
            for(int i=3;i<6;i++) Assert.That(migrated.Find("job-"+i),Is.Null);
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{7,0}));
            migrated.Remove("concept-counter"); PassivePresetStore.Save(migrated);
            Assert.That(PassivePresetStore.Read().Entries.Count,Is.EqualTo(11));
            Assert.That(PassivePresetStore.Read().Find("concept-counter"),Is.Null);
        }
        [Test] public void ConceptMigrationPreservesIntentionallyEmptyLegacyBook()
        {
            PassivePresetStore.Save(new PassivePresetBook());
            Assert.That(PassivePresetStore.Read().Entries,Is.Empty);
            Assert.That(PassivePresetStore.Read().ConceptDefaultsVersion,Is.EqualTo(1));
        }
        [Test] public void DuplicateDropSwapsAndInvalidDropCannotCorruptAnotherPreset()
        {
            var book=PassivePresetStore.Read(); string id=book.Entries[0].Id;
            book.SetSlot(id,0,11); book.SetSlot(id,1,12); book.SetSlot(id,1,11);
            Assert.That(book.Find(id).Choices,Is.EqualTo(new[]{12,11}));
            Assert.That(book.SetSlot(id,2,1),Is.False); Assert.That(book.SetSlot("missing",0,1),Is.False);
            Assert.That(book.SetSlot(id,0,14),Is.False); Assert.That(book.Find("concept-stats").Choices,Is.EqualTo(new[]{12,13}));
            book.SetSlot(id,0,0); Assert.That(book.Find(id).Choices,Is.EqualTo(new[]{0,11}));
        }
        [Test] public void CustomPresetRenamesPersistsAndSlotEditsEquipImmediately()
        {
            Open(); string original=Book.ActiveId;
            Call(_panel,"AddPassivePreset"); string custom=Selected;
            Assert.That(Book.ActiveId,Is.EqualTo(original),"Creating an empty recipe preserves the current loadout.");
            Call(_panel,"RenamePassivePreset",custom,"돌격 조합");
            _panel.DropPassive(custom,0,11); _panel.DropPassive(custom,1,12);
            Assert.That(Book.Entries.Count,Is.EqualTo(9));
            Assert.That(Selection.Find("Presets/Content").childCount,Is.EqualTo(9));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{11,12}));
            Assert.That(Book.ActiveId,Is.EqualTo(custom));
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,12}));
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("사용 중"));
            _panel.Close(); _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Selected,Is.EqualTo(custom)); Assert.That(Book.Find(custom).Name,Is.EqualTo("돌격 조합"));
            Assert.That(Book.Entries[0].Choices,Is.EqualTo(new[]{1,4}));
            Assert.That(Selection.Find("Presets/Content").childCount,Is.EqualTo(9),"Reopening does not leave duplicate rows.");
        }
        [Test] public void DragDropAndClickFallbackEquipIntoTheChosenOctagonalSlot()
        {
            Open(); _events=new GameObject("Passive UI events",typeof(EventSystem));
            var card=Selection.Find("List/Content/Passive7").gameObject;
            var e=new PointerEventData(_events.GetComponent<EventSystem>()) {pointerDrag=card,position=new Vector2(400,400)};
            ExecuteEvents.Execute(card,e,ExecuteEvents.beginDragHandler);
            Assert.That(Get<GameObject>(_panel,"_dragGhost").GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
            var slot=Selection.Find("Presets/Content/Preset-concept-counter/Slot0").gameObject;
            ExecuteEvents.Execute(slot,e,ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(card,e,ExecuteEvents.endDragHandler);
            Assert.That(Book.Find("concept-counter").Choices,Is.EqualTo(new[]{7,4}));
            Assert.That(Get<GameObject>(_panel,"_dragGhost"),Is.Null);
            _panel.ChoosePassive(11); _panel.ClickPassiveSlot("concept-counter",1,false);
            Assert.That(Book.Find("concept-counter").Choices,Is.EqualTo(new[]{7,11}));
            _panel.ClickPassiveSlot("concept-counter",0,true);
            Assert.That(Book.Find("concept-counter").Choices,Is.EqualTo(new[]{0,11}));
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{0,11}));
            _panel.BeginPassiveDrag(1,e); _panel.Close();
            Assert.That(Get<GameObject>(_panel,"_dragGhost"),Is.Null);
        }
        [Test] public void RejectedApplyNeverMarksDraftAsInUseAndAliveBattleIsReadOnly()
        {
            Open(); string active=Book.ActiveId;
            Call(_panel,"AddPassivePreset"); string custom=Selected;
            Book.SetSlot(custom,0,11); PassivePresetStore.Save(Book);
            Set(_panel,"_pendingPreset",custom); Set(_panel,"_pendingChoices",new[]{11,0});
            Call(_panel,"PassiveRequestCompleted",false);
            Assert.That(Book.ActiveId,Is.EqualTo(active)); Assert.That(Get<string>(_panel,"_pendingPreset"),Is.Null);
            _scene.name="Battle"; _panel.DropPassive(custom,1,12); ClickPreset(custom); Call(_panel,"AddPassivePreset");
            Assert.That(Book.Find(custom).Choices,Is.EqualTo(new[]{11,0}));
            Assert.That(Book.Entries.Count,Is.EqualTo(9)); Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{0,0}));
        }
        [Test] public void EveryPresetClickEquipsAndStatChangesNeverSelectAnotherPreset()
        {
            var stats=_player.GetComponent<StatManager>();
            stats.ApplyLocalSceneStats(new StatContainer {STR=new StatSlot {Invested=30}});
            Open();
            Assert.That(Book.ActiveId,Is.Null);
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("선택 중"));
            Assert.That(Selection.Find("Save"),Is.Null,"No separate apply button remains in the passive panel.");
            foreach(var entry in Book.Entries)
            {
                ClickPreset(entry.Id);
                Assert.That(Book.ActiveId,Is.EqualTo(entry.Id));
                Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(entry.Choices));
                Assert.That(PassiveStore.Read(),Is.EqualTo(entry.Choices));
                Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("사용 중"));
            }
            ClickPreset("concept-sniper");
            Assert.That(Selected,Is.EqualTo("concept-sniper"));
            stats.ApplyLocalSceneStats(new StatContainer {CON=new StatSlot {Invested=30}});
            Assert.That(JobGuideContent.IndexOf(stats.CurrentIdentity),Is.EqualTo(1));
            Assert.That(Selected,Is.EqualTo("concept-sniper"));
            Assert.That(Book.ActiveId,Is.EqualTo("concept-sniper"));
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{7,9}));
            _panel.Close(); _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Selected,Is.EqualTo("concept-sniper"));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{7,9}));
        }

        [Test] public void EveryDefaultNameCanBeEditedAndOnlySelectedRowShowsDelete()
        {
            Open();
            for(int i=0;i<Book.Entries.Count;i++)
            {
                string id=Book.Entries[i].Id;
                var row=Selection.Find("Presets/Content/Preset-"+id);
                var name=row.Find("Name").GetComponent<TMP_InputField>();
                Assert.That(name,Is.Not.Null);
                name.onSelect.Invoke(""); name.onEndEdit.Invoke("내 조합 "+i);
                Assert.That(Selected,Is.EqualTo(id));
                Assert.That(Book.Find(id).Name,Is.EqualTo("내 조합 "+i));
                Assert.That(PassivePresetStore.Read().Find(id).Name,Is.EqualTo("내 조합 "+i));
                for(int j=0;j<Book.Entries.Count;j++)
                    Assert.That(Selection.Find("Presets/Content/Preset-"+Book.Entries[j].Id+"/DeletePreset").gameObject.activeSelf,Is.EqualTo(i==j));
                var remove=(RectTransform)row.Find("DeletePreset");
                var badge=(RectTransform)row.Find("ActiveBadge");
                Assert.That(remove.sizeDelta,Is.EqualTo(new Vector2(22,22)));
                Assert.That(remove.anchoredPosition.y,Is.GreaterThan(0));
                Assert.That(remove.anchoredPosition.x-remove.rect.width/2,Is.GreaterThan(badge.anchoredPosition.x+badge.rect.width/2));
            }
            Assert.That(Book.ActiveId,Is.EqualTo(Book.Entries[Book.Entries.Count-1].Id));
            Assert.That(PassiveStore.Read(),Is.EqualTo(Book.Entries[Book.Entries.Count-1].Choices));
        }

        [Test] public void DeletingDefaultAndEquippedPresetsPersistsWithoutChangingAppliedSlots()
        {
            Open();
            _panel.DropPassive("concept-berserker",0,11); _panel.DropPassive("concept-berserker",1,12);
            ClickPreset("concept-sniper");
            Selection.Find("Presets/Content/Preset-concept-sniper/DeletePreset").GetComponent<Button>().onClick.Invoke();
            Assert.That(Book.Find("concept-sniper"),Is.Null);
            Assert.That(Book.ActiveId,Is.Null);
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{7,9}),"Deleting the selected recipe preserves its equipped pair.");
            Assert.That(PassivePresetStore.Read().Entries.Count,Is.EqualTo(7));
            Assert.That(PassivePresetStore.Read().Find("concept-sniper"),Is.Null,"Deleted defaults must never regenerate.");
            ClickPreset("concept-berserker");
            Selection.Find("Presets/Content/Preset-concept-berserker/DeletePreset").GetComponent<Button>().onClick.Invoke();
            Assert.That(Book.ActiveId,Is.Null);
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,12}));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{11,12}));
            _panel.Close(); _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Book.Entries.Count,Is.EqualTo(6)); Assert.That(Book.ActiveId,Is.Null);
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("선택 중"));
        }

        [Test] public void EmptyPresetListSurvivesReopenAndNewPresetSlotsEquipImmediately()
        {
            Open();
            _panel.DropPassive("concept-counter",0,11); _panel.DropPassive("concept-counter",1,0);
            while(Book.Entries.Count>0) Call(_panel,"DeletePassivePreset",Selected);
            _panel.Close(); _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Book.Entries,Is.Empty);
            Assert.That(Selection.Find("Save"),Is.Null);
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,0}));
            Call(_panel,"AddPassivePreset"); string id=Selected;
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,0}));
            _panel.DropPassive(id,0,7);
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{7,0}));
            Assert.That(PassivePresetStore.Read().ActiveId,Is.EqualTo(id));
        }

        [Test] public void PendingApplyAllowsRenamingButPreventsDeletionAndAliveBattleCannotEdit()
        {
            Open(); string id=Selected, name=Book.Find(id).Name;
            Set(_panel,"_pendingPreset",id); Set(_panel,"_pendingChoices",new[]{11,0});
            Call(_panel,"DeletePassivePreset",id); Call(_panel,"RenamePassivePreset",id,"변경");
            Assert.That(Book.Entries.Count,Is.EqualTo(8)); Assert.That(Book.Find(id).Name,Is.EqualTo("변경"));
            Call(_panel,"PassiveRequestCompleted",false); _scene.name="Battle";
            Call(_panel,"DeletePassivePreset",id); Call(_panel,"RenamePassivePreset",id,name);
            Assert.That(Book.Entries.Count,Is.EqualTo(8)); Assert.That(Book.Find(id).Name,Is.EqualTo("변경"));
        }

        [TestCase(true)] [TestCase(false)]
        public void RapidSelectionsAndSlotEditsKeepTheLatestChoiceWhileWaitingForServer(bool accepted)
        {
            Open(); ClickPreset("concept-counter");
            var loadout=_player.GetComponent<PassiveLoadout>();
            // A sniper request is in flight; later clicks must not overwrite its acknowledgment.
            Set(_panel,"_pendingPreset","concept-sniper"); Set(_panel,"_pendingChoices",new[]{7,9});
            ClickPreset("concept-berserker"); ClickPreset("concept-stats");
            _panel.DropPassive("concept-stats",0,11);
            Assert.That(Get<string>(_panel,"_queuedPreset"),Is.EqualTo("concept-stats"));
            Assert.That(Get<string>(_panel,"_pendingPreset"),Is.EqualTo("concept-sniper"));
            Assert.That(Book.ActiveId,Is.EqualTo("concept-counter"));
            Assert.That(loadout.Snapshot(),Is.EqualTo(new[]{1,4}));
            var name=Selection.Find("Presets/Content/Preset-concept-stats/Name").GetComponent<TMP_InputField>();
            Assert.That(name.interactable,Is.True,"Network acknowledgment must not interrupt name editing.");
            name.onEndEdit.Invoke("빠른 수비");
            if(accepted) { Call(loadout,"Apply",new object[]{new[]{7,9}}); PassiveStore.Save(new[]{7,9}); }
            Call(_panel,"PassiveRequestCompleted",accepted);
            Assert.That(Book.ActiveId,Is.EqualTo(accepted?"concept-sniper":"concept-counter"));
            Assert.That(Selection.Find("ActiveBadge").GetComponent<TMP_Text>().text,Is.EqualTo("선택 중"));
            _panel.Close(); Call(_panel,"FlushPassiveSelection");
            Assert.That(loadout.Snapshot(),Is.EqualTo(new[]{11,13}));
            Assert.That(Book.ActiveId,Is.EqualTo("concept-stats"));
            Assert.That(PassivePresetStore.Read().ActiveId,Is.EqualTo("concept-stats"));
            Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{11,13}));
            Assert.That(Get<string>(_panel,"_queuedPreset"),Is.Null);
            _panel.Open(); Call(_panel,"OpenPassives");
            Assert.That(Selected,Is.EqualTo("concept-stats"));
            Assert.That(Book.Find(Selected).Name,Is.EqualTo("빠른 수비"));
        }

        [Test] public void QueuedSelectionIsCancelledOnRespawnAndDeadPlayersCanSelectAgain()
        {
            Open(); ClickPreset("concept-counter");
            Set(_panel,"_pendingPreset","concept-sniper"); Set(_panel,"_pendingChoices",new[]{7,9});
            ClickPreset("concept-stats"); Call(_panel,"PassiveRequestCompleted",false);
            _scene.name="Battle"; Call(_panel,"FlushPassiveSelection");
            Assert.That(Get<string>(_panel,"_queuedPreset"),Is.Null);
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{1,4}));
            Set(_player.GetComponent<HealthSystem>(),"_isDead",true);
            ClickPreset("concept-sniper");
            Assert.That(_player.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{7,9}));
            Assert.That(Book.ActiveId,Is.EqualTo("concept-sniper"));
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
