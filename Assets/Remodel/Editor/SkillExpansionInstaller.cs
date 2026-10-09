using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.EditorData;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    public static class SkillExpansionInstaller
    {
        private const string Folder="Assets/Remodel/Skills";
        [MenuItem("Battle PvP/Skills/Install Expanded Skills")]
        public static void Install()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing skills.");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Folder+"/Icons"); Directory.CreateDirectory(Folder+"/Animations"); Directory.CreateDirectory("Assets/Player/skill/Expanded"); AssetDatabase.Refresh();
            SkillWorkbookImporter.Import();
            var data=AssetDatabase.LoadAssetAtPath<SkillGameData>(SkillWorkbookImporter.AssetPath);
            var catalog=AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>("Assets/Resources/SkillPresentationCatalog.asset");
            if(catalog==null) { catalog=ScriptableObject.CreateInstance<SkillPresentationCatalog>(); AssetDatabase.CreateAsset(catalog,"Assets/Resources/SkillPresentationCatalog.asset"); }
            catalog.Font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
            var metal=Material("SkillMetal",new Color(.10f,.14f,.19f));
            var cyan=Material("SkillCyan",new Color(.1f,1f,.95f));
            var white=Material("SkillWhite",Color.white);
            var models=new Dictionary<int,GameObject>{[100]=Model("Hook",metal,cyan),[104]=Model("Knife",metal,cyan),[110]=Model("Trap",metal,cyan),[111]=Model("DiceAura",white,white)};
            var entries=new List<SkillPresentationEntry>();
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            int layer=Array.FindIndex(controller.layers,l=>l.name=="ExpandedSkills");
            if(layer<0) { controller.AddLayer("ExpandedSkills"); layer=controller.layers.Length-1; }
            var layers=controller.layers; layers[layer].defaultWeight=0; controller.layers=layers;
            var machine=controller.layers[layer].stateMachine;
            var idle=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Empty") ?? machine.AddState("Empty"); idle.writeDefaultValues=false; machine.defaultState=idle;
            var ready=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="KnifeReady") ?? machine.AddState("KnifeReady");
            var readyClip=Clip("AGI_KnifeReady",AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Skill/Bow_Release3.anim"),1,104);
            foreach(var binding in AnimationUtility.GetCurveBindings(readyClip))
            {
                float value=AnimationUtility.GetEditorCurve(readyClip,binding).Evaluate(0);
                AnimationUtility.SetEditorCurve(readyClip,binding,AnimationCurve.Constant(0,1,value));
            }
            ready.motion=readyClip; ready.writeDefaultValues=false;
            foreach(var definition in data.Skills)
            {
                var skill=FindSkill(definition.Kind);
                if(definition.Kind>=100)
                {
                    if(skill==null) { skill=ScriptableObject.CreateInstance<JobSkillData>(); AssetDatabase.CreateAsset(skill,"Assets/Player/skill/Expanded/"+definition.Id+".asset"); }
                    var so=new SerializedObject(skill);
                    so.FindProperty("_skillKind").intValue=definition.Kind; so.FindProperty("_displayName").stringValue=SkillGameData.Text(definition.NameKey);
                    so.FindProperty("_castSeconds").floatValue=definition.Cast; so.FindProperty("_durationSeconds").floatValue=definition.Duration; so.FindProperty("_cooldownSeconds").floatValue=definition.Cooldown;
                    so.FindProperty("_iconSprite").objectReferenceValue=Icon(definition.Kind);
                    string stateName="Skill_"+definition.Id; so.FindProperty("_castAnimationStateName").stringValue=definition.Kind is 107 or 108 ? "" : stateName; so.FindProperty("_castAnimationLayer").intValue=layer;
                    string sound=definition.Kind switch {100 or 104=>"skill-dash",101 or 105=>"skill-blood-charge",102=>"skill-roll",103=>"skill-venom",107=>"skill-kick",108 or 109=>"skill-taunt",110=>"skill-weapon-swap",_=>"skill-reconfigure"};
                    so.FindProperty("_useSfx").objectReferenceValue=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/CombatAudio/"+sound+".wav"); so.FindProperty("_sfxVolume").floatValue=.7f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    string source=definition.Kind switch {101 or 105=>"Skill/Battle_Cry.anim",110=>"Move/Crouch Walk.anim",104=>"Skill/Bow_Release3.anim",_=>"Skill/Preset.anim"};
                    var clip=definition.Kind==(int)JobSkillKind.Charge ? ChargeClip() : Clip(definition.Id,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/"+source),Mathf.Max(.3f,definition.Cast),definition.Kind);
                    var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==stateName) ?? machine.AddState(stateName);
                    state.motion=clip; state.writeDefaultValues=false;
                    foreach(var transition in state.transitions) state.RemoveTransition(transition);
                    if(definition.Kind==(int)JobSkillKind.Charge) ConfigureChargeState(state,clip);
                    else { var exit=state.AddTransition(idle); exit.hasExitTime=true; exit.exitTime=.95f; exit.duration=.1f; exit.hasFixedDuration=true; }
                }
                if (skill != null) { var binding = new SerializedObject(skill); binding.FindProperty("_useGameData").boolValue = true; binding.ApplyModifiedPropertiesWithoutUndo(); }
                entries.Add(new SkillPresentationEntry{Kind=definition.Kind,Data=skill,Prefab=models.TryGetValue(definition.Kind,out var model)?model:null});
            }
            catalog.Entries=entries.ToArray(); EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(controller);
            var player=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                if(player.GetComponent<SkillLoadout>()==null) player.AddComponent<SkillLoadout>();
                if(player.GetComponent<PassiveLoadout>()==null) player.AddComponent<PassiveLoadout>();
                if(player.GetComponent<ExpandedSkillController>()==null) player.AddComponent<ExpandedSkillController>();
                PrefabUtility.SaveAsPrefabAsset(player,"Assets/Prefabs/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets(); SkillInteractionInstaller.Apply(); JobGuideBuilder.Apply(); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Reports/SkillExpansion");
            File.WriteAllText("Reports/SkillExpansion/install.json",JsonUtility.ToJson(new InstallResult{skills=data.Skills.Length,jobs=data.Jobs.Length,strings=data.Strings.Length,models=models.Count,animations=data.Skills.Count(s=>s.Kind>=100)},true));
            Debug.Log("[SkillExpansion] Installation complete.");
        }
        [Serializable] private sealed class InstallResult { public int skills,jobs,strings,models,animations; }
        private static JobSkillData FindSkill(int kind) => AssetDatabase.FindAssets("t:JobSkillData",new[]{"Assets/Player/skill"}).Select(g=>AssetDatabase.LoadAssetAtPath<JobSkillData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(s=>(int)s.SkillKind==kind);
        private static Material Material(string name,Color color)
        {
            string path=Folder+"/"+name+".mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { mat=new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat,path); }
            mat.SetColor("_BaseColor",color); EditorUtility.SetDirty(mat); return mat;
        }
        private static GameObject Part(string name,Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial=material; return go;
        }
        private static GameObject Model(string name,Material metal,Material glow)
        {
            var root=new GameObject(name);
            try
            {
                if(name=="Knife")
                {
                    Part("Grip",root.transform,PrimitiveType.Cube,new Vector3(0,0,-.16f),new Vector3(.06f,.045f,.18f),metal);
                    Part("Blade",root.transform,PrimitiveType.Cube,new Vector3(0,0,.07f),new Vector3(.10f,.025f,.29f),glow).transform.localRotation=Quaternion.Euler(0,0,45);
                    Part("Guard",root.transform,PrimitiveType.Cube,Vector3.zero,new Vector3(.18f,.055f,.035f),metal);
                }
                else if(name=="Hook")
                {
                    Part("Shaft",root.transform,PrimitiveType.Cube,Vector3.zero,new Vector3(.055f,.055f,.35f),metal);
                    for(int i=0;i<3;i++)
                    {
                        float angle=i*120*Mathf.Deg2Rad;
                        Part("Claw",root.transform,PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*.11f,Mathf.Sin(angle)*.11f,.07f),new Vector3(.045f,.045f,.20f),glow).transform.localRotation=Quaternion.Euler(Mathf.Sin(angle)*-40,Mathf.Cos(angle)*40,0);
                    }
                }
                else if(name=="Trap")
                {
                    Part("Pressure plate",root.transform,PrimitiveType.Cylinder,Vector3.zero,new Vector3(.44f,.025f,.44f),metal);
                    Part("Sensor",root.transform,PrimitiveType.Cylinder,new Vector3(0,.055f,0),new Vector3(.18f,.008f,.18f),glow);
                    for(int i=0;i<16;i++)
                    {
                        float angle=i*Mathf.PI*2/16;
                        var jaw=Part("Jaw tooth",root.transform,PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*.32f,.09f,Mathf.Sin(angle)*.32f),new Vector3(.10f,.07f,.055f),i%2==0?glow:metal);
                        jaw.transform.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,25);
                    }
                    for(int i=-1;i<=1;i+=2) Part("Hinge",root.transform,PrimitiveType.Cube,new Vector3(.37f*i,.015f,0),new Vector3(.13f,.10f,.20f),metal);
                }
                else
                {
                    for(int i=0;i<8;i++)
                    {
                        var arrow=new GameObject("Arrow"+i); arrow.transform.SetParent(root.transform,false);
                        Part("Stem",arrow.transform,PrimitiveType.Cube,Vector3.zero,new Vector3(.025f,.16f,.025f),glow);
                        Part("Left",arrow.transform,PrimitiveType.Cube,new Vector3(-.035f,.07f,0),new Vector3(.025f,.11f,.025f),glow).transform.localRotation=Quaternion.Euler(0,0,-45);
                        Part("Right",arrow.transform,PrimitiveType.Cube,new Vector3(.035f,.07f,0),new Vector3(.025f,.11f,.025f),glow).transform.localRotation=Quaternion.Euler(0,0,45);
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+name+".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [MenuItem("Battle PvP/Skills/Apply Charge Run Animation")]
        public static void InstallChargeAnimation()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before changing animations.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            var machine=controller.layers.First(l=>l.name=="ExpandedSkills").stateMachine;
            var state=machine.states.First(s=>s.state.name=="Skill_SHARED_Charge").state;
            ConfigureChargeState(state,ChargeClip());
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        }
        private static AnimationClip ChargeClip()
        {
            const string sourcePath=Folder+"/Source/Quaternius/UAL1_Standard.fbx";
            var source=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().FirstOrDefault(c=>c.name=="Charge_Sprint");
            if(source==null || !source.humanMotion) throw new InvalidDataException("Missing humanoid Charge_Sprint animation: "+sourcePath);
            string path=Folder+"/Animations/SHARED_Charge.anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,path); }
            // Copy baked humanoid motion, preserving the destination GUID and original cadence.
            EditorUtility.CopySerialized(source,clip); clip.name="SHARED_Charge";
            AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=true; settings.loopBlend=true;
            settings.keepOriginalOrientation=true; settings.keepOriginalPositionXZ=true;
            settings.loopBlendOrientation=true; settings.loopBlendPositionXZ=true; settings.loopBlendPositionY=true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            EditorUtility.SetDirty(clip); return clip;
        }
        private static void ConfigureChargeState(AnimatorState state,AnimationClip clip)
        {
            state.motion=clip; state.writeDefaultValues=false;
            state.speed=1; state.speedParameter="LocomotionRate"; state.speedParameterActive=true;
            // Loop until the replicated charge state ends; a one-shot exit cuts off long charges.
            foreach(var transition in state.transitions) state.RemoveTransition(transition);
            EditorUtility.SetDirty(state);
        }
        private static AnimationClip Clip(string id,AnimationClip source,float length,int kind)
        {
            if(source==null) throw new InvalidDataException("Missing source motion for "+id);
            string path=Folder+"/Animations/"+id+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,path); }
            clip.ClearCurves(); clip.frameRate=60;
            foreach(var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve=AnimationUtility.GetEditorCurve(source,binding);
                var keys=curve.keys;
                for(int i=0;i<keys.Length;i++) { keys[i].time*=length/Mathf.Max(.001f,source.length); keys[i].inTangent*=source.length/length; keys[i].outTangent*=source.length/length; }
                AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
            }
            AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=false; AnimationUtility.SetAnimationClipSettings(clip,settings);
            // Humanoid overlays are authored in editor and retargeted by the existing avatar.
            if(kind==100 || kind==104 || kind==112)
            {
                Muscle(clip,"Right Arm Front-Back",length,.25f,-.55f);
                Muscle(clip,"Right Forearm Stretch",length,-.65f,.75f);
                Muscle(clip,"Left Arm Down-Up",length,-.65f,-.65f);
                Muscle(clip,"Left Arm Front-Back",length,.1f,.1f);
                Muscle(clip,"Left Forearm Stretch",length,-.1f,-.1f);
            }
            if(kind==110)
            {
                Muscle(clip,"Left Upper Leg Front-Back",length,.65f,.65f); Muscle(clip,"Right Upper Leg Front-Back",length,.65f,.65f);
                Muscle(clip,"Left Lower Leg Stretch",length,-.8f,-.8f); Muscle(clip,"Right Lower Leg Stretch",length,-.8f,-.8f);
            }
            EditorUtility.SetDirty(clip); return clip;
        }
        private static void Muscle(AnimationClip clip,string name,float length,float start,float end)
        {
            if(!HumanTrait.MuscleName.Contains(name)) throw new InvalidDataException("Unknown humanoid muscle: "+name);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),name),new AnimationCurve(new Keyframe(0,start),new Keyframe(length*.55f,end),new Keyframe(length,end)));
        }
        private static Sprite Icon(int kind)
        {
            // Code-native line symbols, kept reproducible alongside the UI; no external artwork dependency.
            const int size=128; var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels=new Color32[size*size];
            Color color=kind is 100 or 101 or 102 ? new Color(1,.3f,.22f) : kind is 103 or 104 ? new Color(.25f,1,.45f) : kind is 105 or 106 ? new Color(.25f,.65f,1) : new Color(.7f,.6f,1);
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) { float d=Vector2.Distance(new Vector2(x,y),new Vector2(63.5f,63.5f)); pixels[y*size+x]=d<61 ? new Color(.035f,.075f,.12f,1) : Color.clear; if(d>57 && d<60) pixels[y*size+x]=color; }
            void Line(float ax,float ay,float bx,float by)
            {
                var a=new Vector2(ax,ay); var b=new Vector2(bx,by); Vector2 ab=b-a;
                for(int y=8;y<120;y++) for(int x=8;x<120;x++) { Vector2 p=new Vector2(x,y); float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.01f,ab.sqrMagnitude)); if(Vector2.Distance(p,a+ab*t)<3) pixels[y*size+x]=color; }
            }
            void Dot(int x,int y) { Line(x,y,x+.1f,y); }
            switch(kind)
            {
                case 100: Line(63,104,63,39); Line(63,39,42,27); Line(42,27,27,43); Line(27,43,30,59); break;
                case 101: for(int i=0;i<3;i++){Line(35+i*19,33,48+i*19,64);Line(48+i*19,64,35+i*19,95);} break;
                case 102: for(int i=0;i<3;i++){Line(28+i*25,37,49+i*25,64);Line(49+i*25,64,28+i*25,91);} break;
                case 103: Line(24,64,50,87);Line(50,87,79,87);Line(79,87,104,64);Line(104,64,79,41);Line(79,41,50,41);Line(50,41,24,64);Line(29,25,100,105);break;
                case 104: Line(37,27,83,85);Line(83,85,101,105);Line(101,105,72,86);Line(51,36,90,91);Line(35,55,64,33);break;
                case 105: Line(32,27,92,100);Line(95,27,35,100);Line(24,86,45,108);Line(85,108,105,86);break;
                case 106: Line(27,65,46,65);Line(46,65,55,86);Line(55,86,69,38);Line(69,38,79,65);Line(79,65,103,65);break;
                case 107: Line(42,26,79,85);Line(54,89,92,68);Line(54,89,67,109);Line(67,109,105,87);Line(105,87,92,68);break;
                case 108: for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Line(64+Mathf.Cos(a)*20,64+Mathf.Sin(a)*20,64+Mathf.Cos(a)*42,64+Mathf.Sin(a)*42);} break;
                case 109: Line(31,96,96,96);Line(96,96,92,53);Line(92,53,64,27);Line(64,27,35,53);Line(35,53,31,96);Line(64,42,64,88);break;
                case 110: Line(27,47,43,27);Line(43,27,87,27);Line(87,27,102,47);for(int i=0;i<4;i++){Line(32+i*20,48,42+i*15,70);Line(32+i*20,94,42+i*15,74);}break;
                case 111: Line(31,31,97,31);Line(97,31,97,97);Line(97,97,31,97);Line(31,97,31,31);Dot(48,48);Dot(80,80);Dot(64,64);Dot(48,80);Dot(80,48);break;
                default: Line(27,37,77,37);Line(27,37,27,86);Line(27,86,77,86);Line(77,86,77,37);Line(51,57,101,57);Line(101,57,101,105);Line(101,105,51,105);break;
            }
            texture.SetPixels32(pixels); texture.Apply(); string path=Folder+"/Icons/Skill_"+kind+".png"; File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single; importer.spritePixelsPerUnit=128; importer.mipmapEnabled=false; importer.alphaIsTransparency=true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
