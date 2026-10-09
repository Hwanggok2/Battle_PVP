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
        private static readonly string[] JobNames={"힘 특화 · STR","체력 특화 · CON","민첩 특화 · AGI","방어 특화 · DEF","전략가","팔방미인"};
        public static PassivePresetBook Read(int currentJob=5)
        {
            PassivePresetBook book=null;
            try { book=JsonUtility.FromJson<PassivePresetBook>(PlayerPrefs.GetString(Key,"")); }
            catch(ArgumentException) { }
            if(book?.Entries!=null && book.Entries.Count>=6)
            {
                var ids=new HashSet<string>();
                bool valid=true;
                foreach(var entry in book.Entries)
                    if(entry==null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || !PassiveLoadout.Validate(entry.Choices)) {valid=false;break;}
                if(valid && book.Find(book.ActiveId)!=null) return book;
            }
            // Migrate the previous two slots once. Never use the same array for two presets.
            book=new PassivePresetBook(); currentJob=Mathf.Clamp(currentJob,0,5);
            for(int i=0;i<6;i++) book.Entries.Add(new PassivePreset {Id="job-"+i,Name=JobNames[i]});
            book.ActiveId=book.Entries[currentJob].Id;
            book.Find(book.ActiveId).Choices=(int[])PassiveStore.Read().Clone();
            return book;
        }
        public static void Save(PassivePresetBook book)
        {
            if(book?.Entries==null || book.Find(book.ActiveId)==null) return;
            foreach(var entry in book.Entries) if(!PassiveLoadout.Validate(entry.Choices)) return;
            PlayerPrefs.SetString(Key,JsonUtility.ToJson(book)); PlayerPrefs.Save();
        }
    }
}
