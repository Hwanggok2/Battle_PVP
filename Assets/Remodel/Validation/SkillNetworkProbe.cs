#if UNITY_EDITOR || BATTLE_PVP_NETWORK_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BattlePvp.Remodel.Validation
{
    public struct SkillProbePhase : NetworkMessage { public string Name; public uint Actor; public bool End; }
    public struct SkillProbeAck : NetworkMessage { public string Name; public int Seen; public float Travel; }
    // Included only in the explicit validation build, never in the shipped player.
    public sealed class SkillNetworkProbe : MonoBehaviour
    {
        public GameObject PlayerPrefab;
        const string Folder="Reports/TrapNetwork";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<string> _checks=new(), _errors=new();
        bool _host, _finished;
        GameObject _actor;
        NetworkConnectionToClient _remote;
        SkillProbePhase _phase;
        SkillProbeAck? _ack;
        int _seen;
        int _firstTrap, _secondTrap;
        float _travel;
        Vector3 _start;
        Camera _camera;
        IEnumerator Start()
        {
            _host=Environment.GetCommandLineArgs().Contains("-skill-host");
            Application.runInBackground=true; Application.targetFrameRate=120; QualitySettings.vSyncCount=0;
            Application.logMessageReceived+=Log;
            Directory.CreateDirectory(Folder);
            var light=new GameObject("Probe light",typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(40,-35,0);
            _camera=new GameObject("Probe camera",typeof(Camera)).GetComponent<Camera>(); _camera.transform.position=new Vector3(4,3,-6); _camera.transform.LookAt(Vector3.up);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(100,1,100);
            var transport=gameObject.AddComponent<kcp2k.KcpTransport>(); transport.port=17891; Transport.active=transport;
            NetworkClient.OnConnectedEvent=()=>{NetworkClient.connection.isAuthenticated=true;NetworkClient.Ready();};
            if(_host)
            {
                NetworkServer.OnConnectedEvent=c=>{c.isAuthenticated=true;if(c.connectionId!=0)_remote=c;};
                NetworkServer.Listen(8);
                NetworkServer.RegisterHandler<SkillProbeAck>((c,m)=>{if(c.connectionId!=0)_ack=m;});
                NetworkClient.ConnectHost(); HostMode.InvokeOnConnected();
            }
            else
            {
                NetworkClient.RegisterPrefab(PlayerPrefab);
                NetworkClient.RegisterHandler<SkillProbePhase>(OnPhase);
                NetworkClient.Connect("127.0.0.1");
            }
            var routine=_host ? Host() : Client();
            while(true)
            {
                object next;
                try { if(!routine.MoveNext()) break; next=routine.Current; }
                catch(Exception e) { _errors.Add(e.ToString()); break; }
                yield return next;
            }
            Finish();
        }
        void Log(string text,string trace,LogType type)
        { if(type is LogType.Error or LogType.Exception or LogType.Assert) _errors.Add(text+"\n"+trace); }
        void Check(bool ok,string text) { if(!ok) throw new InvalidOperationException(text); _checks.Add(text); }
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        void Equip(int job,JobSkillKind kind)
        {
            var skills=_actor.GetComponent<ExpandedSkillController>(); skills.CancelForLoadout(); skills.States.Clear();
            var stats=new StatContainer();
            if(job==0) stats.STR.Invested=30; else if(job==1) stats.CON.Invested=30; else if(job==2) stats.AGI.Invested=30; else if(job==3) stats.DEF.Invested=30;
            else if(job==4) {stats.STR.Invested=18;stats.CON.Invested=6;stats.AGI.Invested=stats.DEF.Invested=3;}
            else {stats.STR.Invested=stats.CON.Invested=8;stats.AGI.Invested=stats.DEF.Invested=7;}
            Check(_actor.GetComponent<StatManager>().TryApplyServerPreset(stats),"host stats accepted for "+kind);
            var loadout=_actor.GetComponent<SkillLoadout>(); var choices=SkillLoadout.Defaults();
            if(choices[job*2+1]==(int)kind) choices[job*2+1]=choices[job*2]; choices[job*2]=(int)kind;
            loadout.Choices.Clear(); foreach(int c in choices) loadout.Choices.Add(c);
            Set(skills,"_nextUse",0d);
        }
        IEnumerator Phase(string name,Action action,float duration,int expected,float travel=0)
        {
            _ack=null; _remote.Send(new SkillProbePhase{Name=name,Actor=_actor.GetComponent<NetworkIdentity>().netId});
            yield return new WaitForSeconds(.25f); action(); yield return new WaitForSeconds(duration);
            _remote.Send(new SkillProbePhase{Name=name,Actor=_actor.GetComponent<NetworkIdentity>().netId,End=true});
            double deadline=Time.realtimeSinceStartupAsDouble+8;
            while(!_ack.HasValue && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Check(_ack.HasValue && _ack.Value.Name==name,"remote completed "+name);
            Check((_ack.Value.Seen&expected)==expected,"remote observed "+name+" flags="+_ack.Value.Seen);
            if(travel>0) Check(Mathf.Abs(_ack.Value.Travel-travel)<.3f,"remote dash distance "+_ack.Value.Travel);
        }
        IEnumerator Host()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+40;
            while((_remote==null || !_remote.isReady) && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Check(_remote!=null && _remote.isReady,"separate participant connected over KCP");
            _actor=Instantiate(PlayerPrefab,new Vector3(0,.05f,0),Quaternion.identity); _actor.name="Network skill actor";
            _actor.GetComponent<PlayerInput>().enabled=false;
            NetworkServer.Spawn(_actor); Equip(4,JobSkillKind.Trap); yield return new WaitForSeconds(1);
            var health=_actor.GetComponent<HealthSystem>(); var skills=_actor.GetComponent<ExpandedSkillController>();
            var combat=_actor.GetComponent<PlayerCombat>();
            yield return Phase("shield",()=>health.GrantDecayingShield(80,15),.7f,1);
            yield return Phase("shield-hit",()=>health.ApplyDamage(10,DamageSource.Poison,_actor.transform.position+Vector3.forward+Vector3.up),.65f,1);
            Equip(4,JobSkillKind.StrategistRoll); yield return new WaitForSeconds(.3f);
            yield return Phase("dash",()=>Check((bool)Call(combat,"BeginAdvancedSkill",(int)JobSkillKind.StrategistRoll,Vector3.forward,NetworkTime.time,1u),"host dash accepted"),1.1f,3,3.6f);
            Equip(5,JobSkillKind.PolymathRoll); yield return new WaitForSeconds(.3f);
            yield return Phase("roll",()=>Check((bool)Call(combat,"BeginAdvancedSkill",(int)JobSkillKind.PolymathRoll,Vector3.right,NetworkTime.time,2u),"host roll accepted"),1.1f,3,3.6f);
            Equip(0,JobSkillKind.Hook); yield return new WaitForSeconds(.3f);
            yield return Phase("hook",()=>Check(skills.TryUse(0,Vector3.forward),"host hook accepted"),2.2f,3);
            Equip(2,JobSkillKind.Knife); yield return new WaitForSeconds(.3f);
            yield return Phase("knife-ready",()=>Check(skills.TryUse(0,Vector3.forward),"host knife ready"),.7f,3);
            yield return Phase("knife-throw",()=>Call(skills,"ThrowKnife",Vector3.forward),.7f,1);
            Equip(3,JobSkillKind.Fortify); yield return new WaitForSeconds(.3f);
            yield return Phase("fortify",()=>Check(skills.TryUse(0,Vector3.forward),"host fortify accepted"),1f,3);
            Equip(0,JobSkillKind.Charge); yield return new WaitForSeconds(.3f);
            yield return Phase("charge",()=>Check(skills.TryUse(0,Vector3.forward),"host charge accepted"),.8f,3);
            Call(skills,"EndCharge"); yield return new WaitForSeconds(.3f);
            Equip(4,JobSkillKind.Trap); yield return new WaitForSeconds(.3f);
            yield return Phase("trap-place",()=>{Check(skills.TryUse(0,Vector3.forward),"host trap preview"); Check(skills.TryGetTrapPlacement(out var p)&&skills.ConfirmTrap(p),"host trap accepted");},1.8f,3);
            var placed=skills.Traps.Single();
            yield return Phase("trap-persist",()=>Equip(4,JobSkillKind.Dice),.7f,1);
            Check(skills.Traps[placed.Key].ExpiresAt==placed.Value.ExpiresAt,"job/loadout change preserves trap expiry");
            yield return Phase("dice",()=>Check(skills.TryUse(0,Vector3.forward),"host dice accepted"),1.3f,3);
            var target=Instantiate(PlayerPrefab,placed.Value.Position+Vector3.up*.03f,Quaternion.identity); target.GetComponent<PlayerInput>().enabled=false;
            // Spawn the target away from the trap until the participant has entered the phase.
            target.transform.position+=Vector3.right*5; NetworkServer.Spawn(target);
            var targetStats=new StatContainer();targetStats.CON.Invested=30;Check(target.GetComponent<StatManager>().TryApplyServerPreset(targetStats),"trap target initialized");
            yield return Phase("trap-close",()=>{var c=target.GetComponent<CharacterController>();c.enabled=false;target.transform.position=placed.Value.Position+Vector3.up*.03f;c.enabled=true;Physics.SyncTransforms();},1.2f,3);
            Check(!skills.Traps.ContainsKey(placed.Key),"triggered trap removed on host");
            Equip(4,JobSkillKind.Trap);
            for(int i=0;i<2;i++)
            {
                var cc=_actor.GetComponent<CharacterController>(); cc.enabled=false; _actor.transform.position+=Vector3.right*2; cc.enabled=true; Physics.SyncTransforms();
                Check(skills.TryUse(0,Vector3.forward),"expiry trap preview accepted");
                Check(skills.TryGetTrapPlacement(out var point)&&skills.ConfirmTrap(point),"expiry trap placement accepted");
                yield return new WaitForSeconds(1.2f);
                Check(Math.Abs(skills.Traps.Values.Max(t=>t.ExpiresAt)-skills.Now-59.9)<.15,"new trap receives its own sixty-second expiry");
            }
            Check(skills.Traps.Count==2,"two traps wait for separate expiry times");
            yield return Phase("trap-expiry",()=>Equip(4,JobSkillKind.Dice),61f,15);
            Check(skills.Traps.Count==0,"both traps expired independently on host");
            _remote.Send(new SkillProbePhase{Name="finish"}); yield return new WaitForSeconds(.5f);
        }
        IEnumerator Client()
        { double deadline=Time.realtimeSinceStartupAsDouble+180; while(!_finished && Time.realtimeSinceStartupAsDouble<deadline) yield return null; Check(_finished,"host completed all phases"); }
        void OnPhase(SkillProbePhase phase)
        {
            if(phase.Name=="finish") { _finished=true;return; }
            if(phase.End)
            {
                _checks.Add(phase.Name+" observed flags="+_seen+" travel="+_travel);
                NetworkClient.Send(new SkillProbeAck{Name=phase.Name,Seen=_seen,Travel=_travel}); _phase=default; return;
            }
            _phase=phase; _seen=0; _travel=0;
            if(NetworkClient.spawned.TryGetValue(phase.Actor,out var id)) { _actor=id.gameObject;_start=_actor.transform.position; }
            if(phase.Name=="trap-expiry" && _actor!=null)
            {
                var traps=_actor.GetComponent<ExpandedSkillController>().Traps.OrderBy(p=>p.Value.ExpiresAt).ToArray();
                if(traps.Length==2) { _firstTrap=traps[0].Key; _secondTrap=traps[1].Key; }
            }
        }
        void Update()
        {
            if(_host || string.IsNullOrEmpty(_phase.Name) || _actor==null) return;
            var skills=_actor.GetComponent<ExpandedSkillController>(); var a=_actor.GetComponent<Animator>();
            bool State(string layer,string name) {int i=a.GetLayerIndex(layer);return i>=0 && a.GetLayerWeight(i)>.5f && (a.GetCurrentAnimatorStateInfo(i).IsName(name)||a.GetNextAnimatorStateInfo(i).IsName(name));}
            bool Child(string name)=>_actor.GetComponentsInChildren<Transform>().Any(t=>t.name==name && t.gameObject.activeInHierarchy);
            switch(_phase.Name)
            {
                case "shield": if(_actor.GetComponent<HealthSystem>().CurrentShield>0 && Child("Preset yellow shield")) _seen|=1; break;
                case "shield-hit":
                    var shield=_actor.transform.Find("Preset yellow shield");
                    if(shield!=null) { var block=new MaterialPropertyBlock(); shield.GetComponent<Renderer>().GetPropertyBlock(block); float age=block.GetFloat("_ImpactAge"); if(age>=0 && age<.35f) _seen|=1; }
                    break;
                case "dash": case "roll":
                    _travel=Mathf.Max(_travel,Vector3.ProjectOnPlane(_actor.transform.position-_start,Vector3.up).magnitude);
                    if(_travel>1) _seen|=1;
                    var kind=_phase.Name=="dash"?JobSkillKind.StrategistRoll:JobSkillKind.PolymathRoll;var data=SkillPresentationCatalog.Data((int)kind);
                    if(data!=null && (a.GetCurrentAnimatorStateInfo(data.CastAnimationLayer).IsName(data.CastAnimationStateName)||a.GetNextAnimatorStateInfo(data.CastAnimationLayer).IsName(data.CastAnimationStateName))) _seen|=2;
                    break;
                case "hook": if(State("ExpandedUpperBody","Skill_STR_Hook")) _seen|=1;if(State("ExpandedUpperBody","HookRetrieve")) _seen|=2; break;
                case "knife-ready": if(State("ExpandedUpperBody","KnifeReady")) _seen|=1;if(Child("Knife(Clone)")) _seen|=2; break;
                case "knife-throw": if(State("ExpandedUpperBody","Skill_AGI_Knife")) _seen|=1; break;
                case "fortify": if(a.GetCurrentAnimatorStateInfo(0).IsName("Skill_DEF_Fortify"))_seen|=1;if(skills.Active(JobSkillKind.Fortify))_seen|=2; break;
                case "charge": if(State("ExpandedSkills","Skill_SHARED_Charge"))_seen|=1;if(skills.IsCharging && Vector3.Distance(_start,_actor.transform.position)>.5f)_seen|=2; break;
                case "trap-place": if(State("ExpandedSkills","Skill_SHARED_Trap"))_seen|=1;if(skills.Traps.Count>0 && FindObjectsByType<SkillTrapVisual>(FindObjectsSortMode.None).Length>0)_seen|=2; break;
                case "trap-persist": if(skills.Traps.Count>0 && FindObjectsByType<SkillTrapVisual>(FindObjectsSortMode.None).Length>0)_seen|=1; break;
                case "dice": if(skills.Active(JobSkillKind.Dice))_seen|=1;if(Child("DiceAura(Clone)"))_seen|=2; break;
                case "trap-close":
                    foreach(var trap in FindObjectsByType<SkillTrapVisual>(FindObjectsSortMode.None))
                        if(Quaternion.Angle(trap.transform.Find("TrapLeftJaw").localRotation,Quaternion.identity)>30)_seen|=1;
                    if((_seen&1)!=0 && skills.Traps.Count==0 && FindObjectsByType<SkillTrapVisual>(FindObjectsSortMode.None).Length==0)_seen|=2;
                    break;
                case "trap-expiry":
                    if(skills.Traps.TryGetValue(_firstTrap,out var first) && first.Closed)_seen|=1;
                    if(!skills.Traps.ContainsKey(_firstTrap) && skills.Traps.TryGetValue(_secondTrap,out var remaining) && !remaining.Closed)_seen|=2;
                    if(skills.Traps.TryGetValue(_secondTrap,out var second) && second.Closed)_seen|=4;
                    if((_seen&5)==5 && skills.Traps.Count==0 && FindObjectsByType<SkillTrapVisual>(FindObjectsSortMode.None).Length==0)_seen|=8;
                    break;
            }
        }
        [Serializable] sealed class Result {public bool passed;public string[] checks,errors;}
        void Finish()
        {
            Application.logMessageReceived-=Log;
            File.WriteAllText(Folder+(_host?"/host.json":"/participant.json"),JsonUtility.ToJson(new Result{passed=_errors.Count==0,checks=_checks.ToArray(),errors=_errors.ToArray()},true));
            Application.Quit(_errors.Count==0?0:1);
        }
    }
}
#endif
