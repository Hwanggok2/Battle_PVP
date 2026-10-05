using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorData
{
    public static class SkillWorkbookImporter
    {
        public const string AssetPath="Assets/Resources/SkillGameData.asset";
        [MenuItem("Battle PvP/Data/Import Skill Workbooks")]
        public static void Import() => ImportFrom("GameData");
        public static void ImportFrom(string folder)
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before importing game data.");
            var candidate=Parse(folder);
            var existing=AssetDatabase.LoadAssetAtPath<SkillGameData>(AssetPath);
            string backup=existing!=null ? EditorJsonUtility.ToJson(existing) : null;
            try
            {
                if(existing==null) { existing=ScriptableObject.CreateInstance<SkillGameData>(); AssetDatabase.CreateAsset(existing,AssetPath); }
                existing.Skills=candidate.Skills; existing.Pools=candidate.Pools; existing.Jobs=candidate.Jobs; existing.Strings=candidate.Strings;
                existing.Reindex(); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets();
                Debug.Log($"[SkillData] Imported {existing.Skills.Length} skills, {existing.Jobs.Length} jobs, {existing.Strings.Length} strings.");
            }
            catch
            {
                if(backup!=null) { EditorJsonUtility.FromJsonOverwrite(backup,existing); existing.Reindex(); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets(); }
                else if(existing!=null) AssetDatabase.DeleteAsset(AssetPath);
                throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }
        public static SkillGameData Parse(string folder)
        {
            var result=ScriptableObject.CreateInstance<SkillGameData>();
            try
            {
                using var skillBook=new OpenXmlWorkbookReader(Path.Combine(folder,"GameData_Skill.xlsx"));
                using var jobBook=new OpenXmlWorkbookReader(Path.Combine(folder,"GameData_Character.xlsx"));
                using var stringBook=new OpenXmlWorkbookReader(Path.Combine(folder,"GameData_String.xlsx"));
                var strings=new Dictionary<string,SkillString>(StringComparer.Ordinal);
                foreach(string sheet in stringBook.SheetNames)
                foreach(var row in Rows(stringBook,sheet))
                {
                    var value=new SkillString { SearchKey=Required(row,"SearchKey"), Content_Kor=Required(row,"Content_Kor"),Content_Eng=Required(row,"Content_Eng"),FormatArgCount=Int(row,"FormatArgCount") };
                    ValidateFormat(value.Content_Kor,value.FormatArgCount); ValidateFormat(value.Content_Eng,value.FormatArgCount);
                    if(!strings.TryAdd(value.SearchKey,value)) throw new InvalidDataException("Duplicate string: "+value.SearchKey);
                }
                var skills=new Dictionary<string,SkillDefinition>(StringComparer.Ordinal); var kinds=new HashSet<int>();
                foreach(var row in Rows(skillBook,"SkillDefinition"))
                {
                    string export=Required(row,"ExportType"); if(export=="NONE") continue;
                    if(export!="USE") throw new InvalidDataException("ExportType must be USE or NONE.");
                    var skill=new SkillDefinition { Id=Required(row,"SkillId"),Kind=Int(row,"Kind"),NameKey=Required(row,"NameKey"),DescriptionKey=Required(row,"DescriptionKey"),DescriptionArgs=row["DescriptionArgs"],Cast=Number(row,"CastSeconds"),Duration=Number(row,"DurationSeconds"),Cooldown=Number(row,"CooldownSeconds") };
                    if(!Enum.IsDefined(typeof(JobSkillKind),skill.Kind) || !kinds.Add(skill.Kind) || !skills.TryAdd(skill.Id,skill)) throw new InvalidDataException("Unknown or duplicate skill: "+skill.Id);
                    if(skill.Cast<0 || skill.Duration<0 || skill.Cooldown<0) throw new InvalidDataException("Negative skill timing: "+skill.Id);
                    RequireString(strings,skill.NameKey); RequireString(strings,skill.DescriptionKey);
                }
                var parameters=skills.Keys.ToDictionary(id=>id,id=>new Dictionary<string,float>(StringComparer.Ordinal));
                foreach(var row in Rows(skillBook,"SkillParameter"))
                {
                    string id=Required(row,"SkillId"),key=Required(row,"Key");
                    if(!parameters.ContainsKey(id) || !parameters[id].TryAdd(key,Number(row,"Value"))) throw new InvalidDataException("Unknown skill or duplicate parameter: "+id+"/"+key);
                }
                foreach(var skill in skills.Values)
                {
                    skill.Parameters=parameters[skill.Id].Select(p=>new SkillParameter{Key=p.Key,Value=p.Value}).ToArray();
                    string[] args=string.IsNullOrEmpty(skill.DescriptionArgs) ? Array.Empty<string>() : skill.DescriptionArgs.Split(',');
                    if(args.Length!=strings[skill.DescriptionKey].FormatArgCount) throw new InvalidDataException("Description argument count: "+skill.Id);
                    foreach(string key in args) if(!parameters[skill.Id].ContainsKey(key) && key!="CastSeconds" && key!="DurationSeconds" && key!="CooldownSeconds") throw new InvalidDataException("Missing description parameter: "+skill.Id+"/"+key);
                    ValidateParameters(skill);
                }
                var jobs=new Dictionary<int,JobDefinition>();
                foreach(var row in Rows(jobBook,"JobDefinition"))
                {
                    var job=new JobDefinition{Index=Int(row,"JobIndex"),Id=Required(row,"JobId"),NameKey=Required(row,"NameKey"),RequirementKey=Required(row,"RequirementKey"),DescriptionKey=Required(row,"DescriptionKey")};
                    if(job.Index<0 || job.Index>5 || Int(row,"SlotCount")!=2 || !jobs.TryAdd(job.Index,job)) throw new InvalidDataException("Each job must have a unique index and two slots.");
                    RequireString(strings,job.NameKey); RequireString(strings,job.RequirementKey); RequireString(strings,job.DescriptionKey);
                }
                if(jobs.Count!=6) throw new InvalidDataException("Expected six jobs.");
                var pools=new List<JobSkillPool>(); var pairs=new HashSet<string>();
                foreach(var row in Rows(jobBook,"JobSkillPool").OrderBy(r=>Int(r,"SortOrder")))
                {
                    int job=Int(row,"JobIndex"),slot=Int(row,"DefaultSlot"); string id=Required(row,"SkillId");
                    if(!jobs.ContainsKey(job) || !skills.ContainsKey(id) || slot< -1 || slot>1 || !pairs.Add(job+"/"+id)) throw new InvalidDataException("Invalid job skill: "+job+"/"+id);
                    pools.Add(new JobSkillPool{Job=job,Kind=skills[id].Kind,DefaultSlot=slot});
                }
                foreach(int job in jobs.Keys)
                {
                    if(pools.Count(p=>p.Job==job)>4) throw new InvalidDataException("Current selection UI supports at most four skills per job.");
                    for(int slot=0;slot<2;slot++) if(pools.Count(p=>p.Job==job && p.DefaultSlot==slot)!=1) throw new InvalidDataException("Every job requires two distinct default skills.");
                }
                result.Skills=skills.Values.ToArray(); result.Jobs=jobs.OrderBy(p=>p.Key).Select(p=>p.Value).ToArray(); result.Pools=pools.ToArray(); result.Strings=strings.Values.ToArray();
                result.Reindex(); return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }
        private static void RequireString(Dictionary<string,SkillString> strings,string key) { if(!strings.ContainsKey(key)) throw new InvalidDataException("Missing String key: "+key); }
        public static void ValidateFormat(string value,int count)
        {
            if(count<0 || count>64) throw new InvalidDataException("Invalid format argument count.");
            var indexes=Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})").Cast<Match>().Select(m=>int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x=>x).ToArray();
            if(!indexes.SequenceEqual(Enumerable.Range(0,count))) throw new InvalidDataException("String arguments must be contiguous: "+value);
            try { string.Format(CultureInfo.InvariantCulture,value,Enumerable.Repeat<object>(1f,count).ToArray()); }
            catch(FormatException) { throw new InvalidDataException("Invalid format string: "+value); }
        }
        private static void ValidateParameters(SkillDefinition skill)
        {
            foreach(var p in skill.Parameters)
                if(!float.IsFinite(p.Value) || (p.Value<0 && !p.Key.StartsWith("Face",StringComparison.Ordinal))) throw new InvalidDataException("Invalid value: "+skill.Id+"/"+p.Key);
            string[] required=skill.Kind switch {
                100=>new[]{"Range","ProjectileSpeed","PullSpeed","Radius","StopDistance"},
                101=>new[]{"MoveMultiplier","IncomingMultiplier"},102=>new[]{"AccelerationSeconds","InitialSpeedMultiplier","MaximumAgiMultiplier","TurnDegreesPerSecond","DamageMultiplier","StunSeconds"},
                103=>new[]{"BreakDistance","BonusSeconds","AttackMultiplier"},104=>new[]{"MaxCharges","RechargeSeconds","DamageMultiplier","Range","ProjectileSpeed","Radius","ThrowInterval"},
                105=>new[]{"HealthDrainRatio","MoveMultiplier","AttackSpeedMultiplier","AttackMultiplier","RecoveryRegenMultiplier"},106=>new[]{"HealthCostRatio","HealRatio","RegenMultiplier"},
                107=>new[]{"StunSeconds","IncomingMultiplier"},108=>new[]{"ReflectMultiplier"},109=>new[]{"DefenseMultiplier"},110=>new[]{"Lifetime","MaxHealthDamageRatio","RootSeconds","PlaceDistance"},
                111=>new[]{"Face1","Face2","Face3","Face4","Face5","Face6"},112=>new[]{"Range"},_=>Array.Empty<string>()};
            foreach(string key in required)
            {
                if(!skill.Parameters.Any(p=>p.Key==key)) throw new InvalidDataException("Missing skill parameter: "+skill.Id+"/"+key);
                float value=skill.Value(key);
                if(key.StartsWith("Face",StringComparison.Ordinal)) { if(value<=-1 || value>1) throw new InvalidDataException("Dice must stay within (-1, 1]."); }
                else if(value<=0 || value>100) throw new InvalidDataException("Skill parameter outside (0, 100]: "+skill.Id+"/"+key);
            }
            if(skill.Kind==104 && (skill.Value("MaxCharges")%1!=0 || skill.Value("MaxCharges")>10)) throw new InvalidDataException("Knife charge count must be an integer from 1 to 10.");
            if(skill.Kind is 101 or 102 or 106 or 107 or 108 or 109 or 111 && skill.Duration<=0) throw new InvalidDataException("Timed skill duration must be positive: "+skill.Id);
            foreach(var parameter in skill.Parameters)
                if(parameter.Key is "HealthDrainRatio" or "HealthCostRatio" or "HealRatio" or "MaxHealthDamageRatio" && parameter.Value>1)
                    throw new InvalidDataException("Health ratios cannot exceed 1: "+skill.Id+"/"+parameter.Key);
            if(skill.Kind==111) for(int face=1;face<=6;face++)
                if(face<=3 ? skill.Value("Face"+face)>=0 : skill.Value("Face"+face)<=0) throw new InvalidDataException("Dice faces 1–3 must reduce stats and 4–6 must increase them.");
            if(skill.Kind==11)
            {
                float total=skill.Parameters.Where(p=>p.Key.StartsWith("TargetPreset",StringComparison.Ordinal)).Sum(p=>p.Value);
                if(total!=0 && Mathf.Abs(total-30)>.001f) throw new InvalidDataException("Preset investment must total 30, or 0 for the default allocation.");
            }
        }
        private static List<Dictionary<string,string>> Rows(OpenXmlWorkbookReader book,string name)
        {
            var sheet=book.ReadSheet(name); var header=sheet.Rows.FirstOrDefault(r=>r.RowNumber==1);
            if(header==null || header.IsEmpty || header.Cells.Distinct().Count()!=header.Cells.Count) throw new InvalidDataException("Missing/duplicate header: "+name);
            for(int line=2;line<=4;line++)
            {
                var meta=sheet.Rows.FirstOrDefault(r=>r.RowNumber==line);
                if(meta==null || meta.Cells.Count!=header.Cells.Count || meta.Cells.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Four metadata rows required: "+name);
                if(line==2 && meta.Cells.Any(t=>t!="string" && t!="float" && t!="int")) throw new InvalidDataException("Unsupported column type: "+name);
                if(line==3 && meta.Cells.Any(t=>t!="All")) throw new InvalidDataException("Skill tables require All scope: "+name);
            }
            var result=new List<Dictionary<string,string>>();
            foreach(var row in sheet.Rows.Where(r=>r.RowNumber>=5 && !r.IsEmpty))
            { var value=new Dictionary<string,string>(StringComparer.Ordinal); for(int col=0;col<header.Cells.Count;col++) value.Add(header.GetCell(col),row.GetCell(col)); result.Add(value); }
            return result;
        }
        private static string Required(Dictionary<string,string> row,string key) { if(!row.TryGetValue(key,out var value) || string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Missing field: "+key); return value; }
        private static float Number(Dictionary<string,string> row,string key) { if(!float.TryParse(Required(row,key),NumberStyles.Float,CultureInfo.InvariantCulture,out float value) || !float.IsFinite(value)) throw new InvalidDataException("Invalid number: "+key); return value; }
        private static int Int(Dictionary<string,string> row,string key) { float value=Number(row,key); if(value%1!=0 || value<int.MinValue || value>int.MaxValue) throw new InvalidDataException("Invalid integer: "+key); return (int)value; }
    }
}
