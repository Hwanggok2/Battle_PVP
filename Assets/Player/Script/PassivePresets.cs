using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Combat
{
    [Serializable]
    public sealed class PassivePreset
    {
        public string Id, Name;
        public int[] Choices = new int[2];
    }

    [Serializable]
    public sealed class PassivePresetBook
    {
        public int ConceptDefaultsVersion;
        public string ActiveId;
        public List<PassivePreset> Entries = new();
        public PassivePreset Find(string id) => Entries.Find(p => p.Id == id);
        public PassivePreset Add()
        {
            int number=1;
            while (Entries.Exists(p=>p.Name=="프리셋 "+number)) number++;
            var entry=new PassivePreset {Id=Guid.NewGuid().ToString("N"),Name="프리셋 "+number};
            Entries.Add(entry); return entry;
        }
        public bool Remove(string id)
        {
            var entry=Find(id);
            if(entry==null) return false;
            Entries.Remove(entry);
            // Deleting a saved recipe does not equip another one or clear the applied slots.
            if(ActiveId==id) ActiveId=null;
            return true;
        }
        public bool SetSlot(string id,int slot,int kind)
        {
            var entry=Find(id);
            if(entry==null || slot<0 || slot>1 || kind<0 || kind>13) return false;
            var next=(int[])entry.Choices.Clone();
            int other=1-slot;
            if(kind!=0 && next[other]==kind) next[other]=next[slot];
            next[slot]=kind; entry.Choices=next;
            return true;
        }
        public static bool Same(int[] a,int[] b) => a!=null && b!=null && a.Length==2 && b.Length==2 && a[0]==b[0] && a[1]==b[1];
    }

    public static class PassivePresetStore
    {
        public static string Key => "BattlePvp.PassivePresets.v1."+(PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        private const int ConceptDefaultsVersion=1;
        private static readonly string[] LegacyNames={"힘 특화 · STR","체력 특화 · CON","민첩 특화 · AGI","방어 특화 · DEF","전략가","팔방미인"};
        private static void AddConcepts(PassivePresetBook book)
        {
            Add("counter","반격",PassiveKind.Counterattack,PassiveKind.HealingShield);
            Add("stats","스탯 강화",PassiveKind.Vitality,PassiveKind.Ironclad);
            Add("mobility","기동전",PassiveKind.DoubleJump,PassiveKind.Haste);
            Add("assassin","암살",PassiveKind.Backstab,PassiveKind.Haste);
            Add("sniper","저격",PassiveKind.Sniper,PassiveKind.Scholar);
            Add("berserker","광전사",PassiveKind.Berserker,PassiveKind.Victory);
            Add("control","제압",PassiveKind.Concussion,PassiveKind.Haste);
            Add("survival","생존",PassiveKind.Purification,PassiveKind.Vitality);

            void Add(string id,string name,PassiveKind first,PassiveKind second)
            {
                id="concept-"+id;
                if(book.Find(id)==null)
                    book.Entries.Add(new PassivePreset {Id=id,Name=name,Choices=new[]{(int)first,(int)second}});
            }
        }
        private static void MigrateConcepts(PassivePresetBook book)
        {
            if(book.ConceptDefaultsVersion>=ConceptDefaultsVersion) return;
            // Respect an intentionally emptied list. Replace only untouched, unequipped legacy placeholders.
            if(book.Entries.Count>0)
            {
                for(int i=0;i<LegacyNames.Length;i++)
                {
                    var entry=book.Find("job-"+i);
                    if(entry!=null && entry.Id!=book.ActiveId && entry.Name==LegacyNames[i] &&
                       entry.Choices[0]==0 && entry.Choices[1]==0) book.Entries.Remove(entry);
                }
                AddConcepts(book);
            }
            book.ConceptDefaultsVersion=ConceptDefaultsVersion;
            Save(book);
        }
        public static PassivePresetBook Read()
        {
            PassivePresetBook book=null;
            try { book=JsonUtility.FromJson<PassivePresetBook>(PlayerPrefs.GetString(Key,"")); }
            catch(ArgumentException) { }
            if(book?.Entries!=null)
            {
                var ids=new HashSet<string>();
                bool valid=true;
                foreach(var entry in book.Entries)
                    if(entry==null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || !PassiveLoadout.Validate(entry.Choices)) {valid=false;break;}
                if(valid)
                {
                    if(book.Find(book.ActiveId)==null) book.ActiveId=null;
                    MigrateConcepts(book);
                    return book;
                }
            }
            book=new PassivePresetBook {ConceptDefaultsVersion=ConceptDefaultsVersion};
            AddConcepts(book);
            var applied=PassiveStore.Read();
            if(applied[0]!=0 || applied[1]!=0)
            {
                book.ActiveId="legacy-equipped";
                book.Entries.Add(new PassivePreset {Id=book.ActiveId,Name="기존 장착",Choices=(int[])applied.Clone()});
            }
            Save(book);
            return book;
        }
        public static void Save(PassivePresetBook book)
        {
            if(book?.Entries==null || (!string.IsNullOrEmpty(book.ActiveId) && book.Find(book.ActiveId)==null)) return;
            foreach(var entry in book.Entries) if(!PassiveLoadout.Validate(entry.Choices)) return;
            PlayerPrefs.SetString(Key,JsonUtility.ToJson(book)); PlayerPrefs.Save();
        }
    }
}
