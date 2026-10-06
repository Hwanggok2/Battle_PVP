using System;
using System.Collections.Generic;
using BattlePvp.UI;
using TMPro;
using UnityEngine;

namespace BattlePvp.Combat
{
    public sealed class SkillExpansionVisuals : IDisposable
    {
        private readonly ExpandedSkillController _owner;
        private Animator _animator;
        private AudioSource _audio;
        private readonly SkillStealthPresentation _stealth;
        private readonly SkillTrapPreview _trapPreview = new();
        private readonly DefenseSkillVfx _defense = new();
        private readonly SkillBuffAura _buffAura = new();
        private readonly Dictionary<int,GameObject> _traps = new();
        private readonly List<int> _staleTraps = new();
        private readonly Dictionary<Renderer,bool> _heldWeapons = new();
        private Renderer[][] _auraRenderers;
        private readonly MaterialPropertyBlock _auraColor = new();
        private GameObject _diceAura, _diceCanvas, _readyKnife, _heldHook;
        private SkillProjectileVisual _hookProjectile;
        private double _localHookHeldUntil, _localKnifeHeldUntil;
        private TMP_Text _diceText;
        private RectTransform _die;
        private readonly List<UnityEngine.UI.Image> _pips = new();
        private bool _disposed;
        private bool _chargeInterrupted;
        private AnimatorCullingMode? _savedCulling;
        private readonly Dictionary<int,int> _pendingStates = new();
        private const string ChargeState = "Skill_SHARED_Charge";
        private void Animate(int layer,string state,float blend)
        {
            if(layer<0 || !_animator.HasState(layer,Animator.StringToHash(state))) return;
            SetAnimationCulling(true);
            _pendingStates[layer]=Animator.StringToHash(state);
            _animator.SetLayerWeight(layer,1);
            _animator.CrossFadeInFixedTime(state,blend,layer,0);
        }
        private void SetAnimationCulling(bool active)
        {
            if(_animator==null) return;
            if(active)
            {
                _savedCulling??=_animator.cullingMode;
                _animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            }
            else if(_savedCulling.HasValue)
            { _animator.cullingMode=_savedCulling.Value; _savedCulling=null; }
        }
        private bool HasPendingState(int layer)
        {
            if(!_pendingStates.TryGetValue(layer,out int requested)) return false;
            var current=_animator.GetCurrentAnimatorStateInfo(layer);
            var next=_animator.GetNextAnimatorStateInfo(layer);
            if(current.shortNameHash==requested || (_animator.IsInTransition(layer) && next.shortNameHash==requested))
            { _pendingStates.Remove(layer); return false; }
            return true;
        }
        public SkillExpansionVisuals(ExpandedSkillController owner) { _owner=owner; _stealth=new SkillStealthPresentation(owner.transform); }
        public void Resume() => _disposed=false;
        public void Play(JobSkillKind kind,Vector3 point)
        {
            if(_owner==null) return;
            _disposed=false;
            if(kind==JobSkillKind.Hook) _localHookHeldUntil=_owner.Now+ExpandedSkillController.Value(kind,"ThrowReleaseSeconds",.24f);
            if(kind==JobSkillKind.Knife) _localKnifeHeldUntil=_owner.Now+ExpandedSkillController.Value(kind,"ThrowReleaseSeconds",.1f);
            if(kind==JobSkillKind.Charge) _chargeInterrupted=false;
            var data=SkillPresentationCatalog.Data((int)kind);
            if(_animator==null) _animator=_owner.GetComponentInChildren<Animator>();
            if(data!=null && _animator!=null && !string.IsNullOrEmpty(data.CastAnimationStateName))
            {
                int layer=kind==JobSkillKind.Fortify ? 0 : _animator.GetLayerIndex(kind==JobSkillKind.Hook || kind==JobSkillKind.Knife ? "ExpandedUpperBody" : "ExpandedSkills");
                if(layer>=0 && _animator.HasState(layer,Animator.StringToHash(data.CastAnimationStateName)))
                    Animate(layer,data.CastAnimationStateName,.06f);
            }
            if(data!=null && data.UseSfx!=null)
            {
                if(_audio==null) { _audio=_owner.gameObject.AddComponent<AudioSource>(); _audio.playOnAwake=false; _audio.minDistance=2; _audio.maxDistance=24; _audio.dopplerLevel=0; _audio.rolloffMode=AudioRolloffMode.Linear; }
                _audio.spatialBlend=_owner.Owner ? 0 : 1;
                _audio.PlayOneShot(data.UseSfx,data.SfxVolume*LocalGameSettings.Current.effects);
            }
        }
        public void Projectile(JobSkillKind kind,Vector3 start,Vector3 direction,float duration)
        {
            var prefab=SkillPresentationCatalog.Instance?.Find((int)kind)?.Prefab;
            if(prefab==null) return;
            var go=CombatVisualPool.Rent(prefab,start,Quaternion.LookRotation(direction),_owner.gameObject.scene);
            var flight=go.GetComponent<SkillProjectileVisual>() ?? go.AddComponent<SkillProjectileVisual>();
            flight.Configure(_owner,kind,start,direction,duration);
            go.SetActive(true);
            if(kind==JobSkillKind.Hook) { _hookProjectile=flight; _localHookHeldUntil=0; }
            else if(kind==JobSkillKind.Knife) _localKnifeHeldUntil=0;
        }
        public void RetrieveHook(Vector3 point,float seconds)
        {
            PlayHookRetrieval(seconds);
            if(_hookProjectile!=null && _hookProjectile.IsOwnedBy(_owner)) _hookProjectile.BeginRetrieval(point,seconds);
        }
        private void PlayHookRetrieval(float seconds)
        {
            if(_owner==null) return;
            if(_animator==null) _animator=_owner.GetComponentInChildren<Animator>();
            if(_animator!=null)
            {
                // The source recovery clip is 18 frames at 30 FPS; retime it to the server's actual pull.
                _animator.SetFloat("HookRetrieveRate",.6f/Mathf.Max(.1f,seconds));
                Animate(_animator.GetLayerIndex("ExpandedUpperBody"),"HookRetrieve",.07f);
            }
        }
        public void Trap(int id,Vector3 point,double expiry)
        {
            if(_traps.ContainsKey(id)) return;
            var prefab=SkillPresentationCatalog.Instance?.Find((int)JobSkillKind.Trap)?.Prefab;
            if(prefab==null) return;
            var go=UnityEngine.Object.Instantiate(prefab,point,Quaternion.identity); _traps.Add(id,go);
        }
        public void RemoveTrap(int id) { if(_traps.TryGetValue(id,out var go)) DestroyVisual(go); _traps.Remove(id); }
        public void TickTraps()
        {
            if(_owner==null) return;
            double now=_owner.Now;
            foreach(var pair in _owner.Traps)
            {
                var state=pair.Value;
                double closedAt=state.Closed ? state.ClosedAt : state.ExpiresAt;
                if(now>=closedAt+SkillTrapVisual.CloseLifetime) continue;
                Trap(pair.Key,state.Position,state.ExpiresAt);
                if(_traps.TryGetValue(pair.Key,out var go)) go.GetComponent<SkillTrapVisual>()?.Render(now,closedAt);
            }
            _staleTraps.Clear();
            foreach(var pair in _traps)
            {
                // If a long frame coalesces close and removal into one network update,
                // still play the close locally instead of popping the model out of view.
                if(!_owner.Traps.TryGetValue(pair.Key,out var state))
                {
                    var visual=pair.Value.GetComponent<SkillTrapVisual>();
                    if(visual==null || visual.RenderRemoved(now)) _staleTraps.Add(pair.Key);
                }
                else if(now>=(state.Closed ? state.ClosedAt : state.ExpiresAt)+SkillTrapVisual.CloseLifetime)
                    _staleTraps.Add(pair.Key);
            }
            foreach(int id in _staleTraps) RemoveTrap(id);
        }
        public void Tick()
        {
            if(_disposed || _owner==null) return;
            if(_animator==null) _animator=_owner.GetComponentInChildren<Animator>();
            if(_animator!=null)
            {
                int layer=_animator.GetLayerIndex("ExpandedSkills");
                bool pendingFull=layer>=0 && HasPendingState(layer);
                if(layer>=0 && !pendingFull && !_animator.IsInTransition(layer))
                {
                    var state=_animator.GetCurrentAnimatorStateInfo(layer);
                    bool charging=_owner.IsCharging && !_chargeInterrupted;
                    if(!_owner.IsCharging) _chargeInterrupted=false;
                    if(charging && state.IsName("Empty")) Animate(layer,ChargeState,.12f);
                    else if(!charging && state.IsName(ChargeState)) _animator.CrossFadeInFixedTime("Empty",.1f,layer);
                    else if(state.IsName("Empty")) _animator.SetLayerWeight(layer,0);
                    else _animator.SetLayerWeight(layer,1);
                }
                bool fortify=_owner.Active(JobSkillKind.Fortify);
                bool pendingPosture=HasPendingState(0);
                if(!pendingPosture && !_animator.IsInTransition(0))
                {
                    var posture=_animator.GetCurrentAnimatorStateInfo(0);
                    if(fortify)
                    {
                        // Do not let a fast attack's global Animator speed shorten the three-second posture.
                        float duration=ExpandedSkillController.Value(JobSkillKind.Fortify,"DurationSeconds",3);
                        float phase=Mathf.Clamp01(1-(float)(_owner.Read(JobSkillKind.Fortify).ActiveUntil-_owner.Now)/duration);
                        if(!posture.IsName("Skill_DEF_Fortify") || Mathf.Abs(posture.normalizedTime-phase)>.04f)
                            _animator.Play("Skill_DEF_Fortify",0,phase);
                    }
                    else if(posture.IsName("Skill_DEF_Fortify")) _animator.CrossFadeInFixedTime("Movement",.1f,0);
                }
                int upper=_animator.GetLayerIndex("ExpandedUpperBody");
                bool pendingUpper=upper>=0 && HasPendingState(upper);
                if(upper>=0 && !pendingUpper && !_animator.IsInTransition(upper))
                {
                    var state=_animator.GetCurrentAnimatorStateInfo(upper);
                    if(_owner.IsRetrievingHook && !state.IsName("HookRetrieve")) PlayHookRetrieval(_owner.HookRetrieveRemaining);
                    else if(_owner.KnifeReady && state.IsName("Empty")) Animate(upper,"KnifeReady",.1f);
                    else if(!_owner.KnifeReady && state.IsName("KnifeReady")) _animator.CrossFadeInFixedTime("Empty",.1f,upper);
                    else if(state.IsName("Empty")) _animator.SetLayerWeight(upper,0);
                    else _animator.SetLayerWeight(upper,1);
                }
                bool knifeHeld=_owner.KnifeReady || _owner.Now<_localKnifeHeldUntil;
                if(knifeHeld && _readyKnife==null && _animator.isHuman)
                {
                    var prefab=SkillPresentationCatalog.Instance?.Find((int)JobSkillKind.Knife)?.Prefab;
                    var hand=_animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if(prefab!=null && hand!=null) { _readyKnife=UnityEngine.Object.Instantiate(prefab,hand); FitKnifeGrip(_readyKnife.transform,hand,_animator); }
                }
                if(_readyKnife!=null) _readyKnife.SetActive(knifeHeld && (!_owner.IsStealthed || _owner.Owner));
                bool hookHeld=_owner.IsHoldingHook || _owner.Now<_localHookHeldUntil;
                if(hookHeld && _heldHook==null && _animator.isHuman)
                {
                    var prefab=SkillPresentationCatalog.Instance?.Find((int)JobSkillKind.Hook)?.Prefab;
                    var hand=_animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if(prefab!=null && hand!=null) { _heldHook=UnityEngine.Object.Instantiate(prefab,hand); _heldHook.transform.localPosition=Vector3.zero; _heldHook.transform.localRotation=Quaternion.Euler(0,90,0); }
                }
                if(_heldHook!=null) _heldHook.SetActive(hookHeld && (!_owner.IsStealthed || _owner.Owner));
                bool throwing=upper>=0 && (pendingUpper || _animator.GetCurrentAnimatorStateInfo(upper).IsName("Skill_AGI_Knife") || _animator.GetCurrentAnimatorStateInfo(upper).IsName("Skill_STR_Hook") || _animator.GetCurrentAnimatorStateInfo(upper).IsName("HookRetrieve"));
                HideHeldWeapons(knifeHeld || throwing || _owner.IsHookActive || _owner.IsPlacingTrap || (fortify && !_owner.GetComponent<PlayerCombat>().IsAttackActive));
                // Hand positions drive projectiles, and the kneeling hips drive the FPS camera.
                // These bones must keep evaluating even when a first-person renderer is culled.
                SetAnimationCulling(knifeHeld || _owner.IsHookActive || _owner.IsPlacingTrap || _owner.IsCharging || fortify ||
                    (layer>=0 && _animator.GetLayerWeight(layer)>0) || (upper>=0 && _animator.GetLayerWeight(upper)>0));
            }
            bool hide=_owner.IsStealthed && !_owner.Owner;
            _stealth.Apply(_owner.IsStealthed,_owner.Owner,ExpandedSkillController.Value(JobSkillKind.Stealth,"LocalAlpha",.5f));
            _defense.Tick(_owner,hide);
            _buffAura.Tick(_owner,hide);
            _trapPreview.Tick(_owner);
            bool dice=_owner.Active(JobSkillKind.Dice);
            if(dice && _diceAura==null)
            {
                var prefab=SkillPresentationCatalog.Instance?.Find((int)JobSkillKind.Dice)?.Prefab;
                if(prefab!=null)
                {
                    _diceAura=UnityEngine.Object.Instantiate(prefab,_owner.transform);
                    _auraRenderers=new Renderer[_diceAura.transform.childCount][];
                    for(int i=0;i<_auraRenderers.Length;i++) _auraRenderers[i]=_diceAura.transform.GetChild(i).GetComponentsInChildren<Renderer>();
                }
            }
            if(_diceAura!=null)
            {
                // Negative rolls use the common debuff arrows, avoiding a second overlapping group.
                _diceAura.SetActive(dice && _owner.DiceFace >= 4 && !hide);
                Color color=_owner.DiceFace<=3 ? new Color(.7f,.2f,1) : new Color(1,.12f,.08f);
                float sign=_owner.DiceFace<=3 ? -1 : 1;
                for(int i=0;i<_diceAura.transform.childCount;i++)
                {
                    var arrow=_diceAura.transform.GetChild(i);
                    float phase=Mathf.Repeat((float)(_owner.Now-_owner.DiceStarted)*.7f+i*.17f,1);
                    float angle=i*Mathf.PI*2/8;
                    arrow.localPosition=new Vector3(Mathf.Cos(angle)*.55f,sign>0 ? phase*2 : (1-phase)*2,Mathf.Sin(angle)*.55f);
                    arrow.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,sign>0 ? 0 : 180);
                    _auraColor.SetColor("_BaseColor",color); _auraColor.SetColor("_Color",color);
                    foreach(var renderer in _auraRenderers[i]) renderer.SetPropertyBlock(_auraColor);
                }
            }
            if(_owner.Owner && dice)
            {
                if(_diceCanvas==null) CreateDiceOverlay();
                _diceCanvas.SetActive(true);
                float elapsed=(float)(_owner.Now-_owner.DiceStarted);
                int face=elapsed<1 ? 1+((int)(elapsed*20)%6) : _owner.DiceFace;
                string sign=_owner.DiceFace<=3 ? "" : "+";
                float value=ExpandedSkillController.Value(JobSkillKind.Dice,"Face"+_owner.DiceFace)*100;
                _diceText.text=elapsed<1 ? face.ToString() : string.Format(SkillGameData.Text("UI_DiceResult","주사위 {0}  ·  모든 능력치 {1}{2}%"),face,sign,value);
                _diceText.color=_owner.DiceFace<=3 ? new Color(.8f,.45f,1) : new Color(1,.35f,.3f);
                _die.localRotation=Quaternion.Euler(0,0,elapsed<1 ? Mathf.Sin(elapsed*40)*24 : 0);
                for(int i=0;i<7;i++)
                {
                    bool visible=i==3 ? face%2==1 : (i==0 || i==6) ? face>=2 : (i==2 || i==4) ? face>=4 : face==6;
                    _pips[i].enabled=visible;
                    _pips[i].color=_diceText.color;
                }
            }
            else if(_diceCanvas!=null) _diceCanvas.SetActive(false);
        }
        private void CreateDiceOverlay()
        {
            _diceCanvas=new GameObject("Skill Dice Result",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
            var canvas=_diceCanvas.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=170;
            var scaler=_diceCanvas.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1600,900);
            var die=new GameObject("Die",typeof(RectTransform),typeof(UnityEngine.UI.Image)); die.transform.SetParent(_diceCanvas.transform,false);
            _die=die.GetComponent<RectTransform>(); _die.anchorMin=_die.anchorMax=new Vector2(.5f,1); _die.anchoredPosition=new Vector2(0,-72); _die.sizeDelta=new Vector2(58,58);
            var background=die.GetComponent<UnityEngine.UI.Image>(); background.color=new Color(.03f,.04f,.07f,.95f); background.raycastTarget=false;
            var positions=new[]{new Vector2(-17,17),new Vector2(-17,0),new Vector2(17,17),Vector2.zero,new Vector2(-17,-17),new Vector2(17,0),new Vector2(17,-17)};
            for(int i=0;i<positions.Length;i++)
            {
                var pip=new GameObject("Pip "+i,typeof(RectTransform),typeof(UnityEngine.UI.Image)); pip.transform.SetParent(_die,false);
                var r=pip.GetComponent<RectTransform>(); r.anchoredPosition=positions[i]; r.sizeDelta=new Vector2(8,8);
                var graphic=pip.GetComponent<UnityEngine.UI.Image>(); graphic.raycastTarget=false; _pips.Add(graphic);
            }
            var label=new GameObject("Result",typeof(RectTransform),typeof(TextMeshProUGUI)); label.transform.SetParent(_diceCanvas.transform,false);
            _diceText=label.GetComponent<TextMeshProUGUI>(); _diceText.font=SkillPresentationCatalog.Instance?.Font; _diceText.fontSize=26; _diceText.alignment=TextAlignmentOptions.Center; _diceText.raycastTarget=false;
            var rect=_diceText.rectTransform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,1); rect.anchoredPosition=new Vector2(0,-118); rect.sizeDelta=new Vector2(620,50);
        }
        internal static void FitKnifeGrip(Transform knife,Transform hand,Animator animator)
        {
            var middle=animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if(middle==null || index==null || little==null)
            { knife.localPosition=new Vector3(.06f,0,0); knife.localRotation=Quaternion.Euler(0,90,0); return; }
            // Grip the handle in the palm, with the blade leaving on the thumb side.
            Vector3 fingers=hand.InverseTransformPoint(middle.position);
            Vector3 blade=hand.InverseTransformDirection(index.position-little.position).normalized;
            knife.localPosition=fingers*.65f;
            knife.localRotation=Quaternion.LookRotation(blade,fingers.normalized);
        }
        private void HideHeldWeapons(bool hide)
        {
            if(hide && _heldWeapons.Count==0)
                foreach(var t in _owner.GetComponentsInChildren<Transform>(true))
                    if(t.name=="Sword" || t.name=="Bow_hand") foreach(var r in t.GetComponentsInChildren<Renderer>(true)) _heldWeapons[r]=r.forceRenderingOff;
            if(hide) { foreach(var pair in _heldWeapons) if(pair.Key!=null) pair.Key.forceRenderingOff=true; }
            else { foreach(var pair in _heldWeapons) if(pair.Key!=null) pair.Key.forceRenderingOff=pair.Value; _heldWeapons.Clear(); }
        }
        public void InterruptCharge()
        {
            // The owner predicts an attack before the server's charge cancellation arrives.
            // Release the full-body run immediately so it cannot cover the attack pose.
            _chargeInterrupted=true;
            if(_animator==null) return;
            int layer=_animator.GetLayerIndex("ExpandedSkills");
            if(layer<0) return;
            if(_animator.GetCurrentAnimatorStateInfo(layer).IsName(ChargeState) ||
                (_animator.IsInTransition(layer) && _animator.GetNextAnimatorStateInfo(layer).IsName(ChargeState)))
            { _pendingStates.Remove(layer); _animator.Play("Empty",layer,0); _animator.SetLayerWeight(layer,0); }
        }
        public void Dispose() => Dispose(false);
        public void ResetOwnerEffects() => Dispose(true);
        private void Dispose(bool preserveTraps)
        {
            if(!preserveTraps) { foreach(var pair in _traps) DestroyVisual(pair.Value); _traps.Clear(); }
            if(_disposed) return; _disposed=true; _pendingStates.Clear(); _stealth.Dispose(); _trapPreview.Dispose(); _defense.Dispose(); _buffAura.Dispose(); HideHeldWeapons(false);
            SetAnimationCulling(false);
            if(_animator!=null && _animator.GetCurrentAnimatorStateInfo(0).IsName("Skill_DEF_Fortify")) _animator.Play("Movement",0,0);
            if(_animator!=null) foreach(string name in new[]{"ExpandedSkills","ExpandedUpperBody"})
            { int layer=_animator.GetLayerIndex(name); if(layer>=0) { _animator.Play("Empty",layer,0); _animator.SetLayerWeight(layer,0); } }
            _localHookHeldUntil=_localKnifeHeldUntil=0;
            if(_hookProjectile!=null && _hookProjectile.IsOwnedBy(_owner)) _hookProjectile.Release();
            _hookProjectile=null;
            DestroyVisual(_diceAura); DestroyVisual(_diceCanvas); DestroyVisual(_readyKnife); DestroyVisual(_heldHook); DestroyVisual(_audio); _pips.Clear();
        }
        private static void DestroyVisual(UnityEngine.Object value)
        { if(value==null) return; if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
    public sealed class SkillProjectileVisual : MonoBehaviour
    {
        private ExpandedSkillController _owner; private JobSkillKind _kind; private Vector3 _start,_direction;
        private float _elapsed,_duration; private SkillHookChain _chain;
        private bool _attached, _returning, _observedHookActive;
        private float _createdAt;
        private float _returnDuration, _returnElapsed;
        private Transform _hitTarget; private Vector3 _hitOffset, _returnStart;
        public bool IsOwnedBy(ExpandedSkillController owner) => _owner!=null && _owner==owner;
        public void Release()
        {
            _owner=null; _hitTarget=null; _chain?.Hide();
            CombatVisualPool.Return(gameObject);
        }
        public void Configure(ExpandedSkillController owner,JobSkillKind kind,Vector3 start,Vector3 direction,float duration)
        {
            _owner=owner; _kind=kind; _start=start; _direction=direction; _duration=duration;
            _elapsed=_returnElapsed=_returnDuration=0;
            _attached=_returning=_observedHookActive=false; _hitTarget=null;
            _createdAt=Time.time;
            if(kind==JobSkillKind.Hook)
            {
                var renderer=GetComponentInChildren<MeshRenderer>(); _chain ??=new SkillHookChain(renderer!=null ? renderer.sharedMaterial : null,gameObject.scene);
            }
        }
        public void BeginRetrieval(Vector3 point,float seconds)
        {
            _returning=true; _attached=false; _returnStart=point; _returnDuration=Mathf.Max(.1f,seconds); _returnElapsed=0;
            transform.position=point;
        }
        private void Update()
        {
            if(_owner==null) { Release(); return; }
            if(_kind==JobSkillKind.Hook)
            {
                _observedHookActive|=_owner.IsHookActive;
                // The reliable projectile RPC can arrive before its owner's next SyncVar update.
                if((!_owner.IsHookActive && (_observedHookActive || Time.time-_createdAt>.5f)) || Time.time-_createdAt>3.5f)
                { Release(); return; }
            }
            if(_returning)
            {
                _returnElapsed+=Time.deltaTime;
                transform.position=Vector3.Lerp(_returnStart,_owner.ThrowHandPosition,Mathf.Clamp01(_returnElapsed/_returnDuration));
                return;
            }
            if(_attached)
            { if(_hitTarget!=null) transform.position=_hitTarget.TransformPoint(_hitOffset); return; }
            float step=ExpandedSkillController.Value(_kind,"ProjectileSpeed",16)*Mathf.Min(Time.deltaTime,Mathf.Max(0,_duration-_elapsed));
            if(SkillTargeting.Cast(_owner,transform.position,_direction,step,ExpandedSkillController.Value(_kind,"Radius",.06f),out var hit))
            {
                transform.position=hit.point;
                if(_kind==JobSkillKind.Hook) { _attached=true; _hitTarget=hit.collider.transform; _hitOffset=_hitTarget.InverseTransformPoint(hit.point); }
                else Release();
                return;
            }
            _elapsed+=Time.deltaTime;
            transform.position+=_direction*step;
            if(_elapsed>=_duration)
            {
                if(_kind!=JobSkillKind.Hook) { Release(); return; }
                // Wait for the server's retrieval cue instead of retracting before the hand pulls.
                transform.position=_start+_direction*ExpandedSkillController.Value(_kind,"Range",6);
            }
        }
        private void LateUpdate()
        { if(_owner!=null && UnityEngine.Rendering.OnDemandRendering.willCurrentFrameRender) _chain?.Update(_owner.ThrowHandPosition,transform.position); }
        private void OnDestroy() => _chain?.Dispose();
    }
}
