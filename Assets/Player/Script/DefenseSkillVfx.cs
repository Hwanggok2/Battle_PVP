using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Reusable visuals driven by replicated skill expiry/consumption, including late joiners.</summary>
    public sealed class DefenseSkillVfx : IDisposable
    {
        private GameObject _shield;
        private Mesh _shieldMesh;
        private Material _shieldMaterial, _weaponMaterial;
        private GameObject _presetShield;
        private Mesh _presetShieldMesh;
        private Material _presetShieldMaterial;
        private HealthSystem _health;
        private PlayerCombat _combat;
        private double _shieldHitAt = double.NegativeInfinity;
        private Vector3 _shieldHitPoint;
        private readonly MaterialPropertyBlock _impactProperties = new();
        private MeshRenderer _presetShieldRenderer;
        private readonly List<(MeshRenderer source, MeshRenderer glow)> _weapons = new();
        public void Tick(ExpandedSkillController owner,bool concealed)
        {
            if (_health == null)
            {
                _health = owner.GetComponent<HealthSystem>();
                if (_health != null) _health.ShieldHit += OnShieldHit;
            }
            float impactAge = (float)(Time.unscaledTimeAsDouble - _shieldHitAt);
            bool shield = !concealed && _health != null && !_health.IsDead && (_health.CurrentShield > 0 || impactAge < .35f);
            if (shield && _presetShield == null)
            {
                _presetShieldMaterial = Material("Preset yellow shield", new Color(1.8f, 1.45f, .08f, .19f));
                _presetShieldMesh = ShieldMesh(false);
                _presetShield = new GameObject("Preset yellow shield") { layer = owner.gameObject.layer };
                _presetShield.transform.SetParent(owner.transform, false);
                _presetShieldRenderer = Renderer(_presetShield, _presetShieldMesh, _presetShieldMaterial);
            }
            if (_presetShield != null)
            {
                _presetShield.SetActive(shield);
                if (shield)
                {
                    _presetShieldMaterial.SetColor("_BaseColor", new Color(1.8f, 1.45f, .08f, _health.CurrentShield<=0 ? 0 : owner.IsStealthed ? .095f : .19f));
                    _impactProperties.SetVector("_ImpactPoint",_shieldHitPoint);
                    _impactProperties.SetFloat("_ImpactAge",impactAge<.35f ? impactAge : -1);
                    _presetShieldRenderer.SetPropertyBlock(_impactProperties);
                }
            }
            bool thorns=owner.Active(JobSkillKind.Thorns) && !concealed;
            bool bash=owner.Active(JobSkillKind.Bash) && !concealed;
            if(_combat==null) _combat=owner.GetComponent<PlayerCombat>();
            bool taunt=_combat!=null && _combat.IsTauntReady && !concealed;
            bool weaponReady=(bash || taunt) && (_health==null || !_health.IsDead);
            if(thorns && _shield==null) CreateShield(owner.transform);
            if(_shield!=null) _shield.SetActive(thorns);
            if(weaponReady && _weaponMaterial==null) CreateWeaponGlow(owner.transform);
            if(_weaponMaterial!=null && weaponReady)
                _weaponMaterial.SetColor("_BaseColor", bash && taunt ? new Color(1.3f,.2f,2.4f,.6f) :
                    taunt ? new Color(2.4f,.65f,.06f,.6f) : new Color(.08f,.45f,2.4f,.6f));
            foreach(var pair in _weapons)
                if(pair.glow!=null) pair.glow.enabled=weaponReady && pair.source!=null && pair.source.enabled && !pair.source.forceRenderingOff;
        }
        private void OnShieldHit(Vector3 localPoint)
        {
            Vector3 center=Vector3.up*.94f;
            Vector3 direction=localPoint-center;
            if(direction.sqrMagnitude<.0001f) direction=Vector3.forward;
            Vector3 unit=new Vector3(direction.x/.68f,direction.y/1.04f,direction.z/.68f).normalized;
            _shieldHitPoint=center+Vector3.Scale(unit,new Vector3(.68f,1.04f,.68f));
            _shieldHitAt=Time.unscaledTimeAsDouble;
        }
        private static Material Material(string name,Color color,float expand=0,bool vertexAlpha=false)
        {
            var material=new Material(Resources.Load<Shader>("CombatVfx/DefenseAura")) { name=name };
            material.SetColor("_BaseColor",color); material.SetFloat("_Expand",expand); material.SetFloat("_VertexAlpha",vertexAlpha ? 1 : 0);
            return material;
        }
        private static MeshRenderer Renderer(GameObject go,Mesh mesh,Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return renderer;
        }
        private void CreateWeaponGlow(Transform root)
        {
            _weaponMaterial=Material("Bash blue weapon glow",new Color(.08f,.45f,2.4f,.6f),.012f);
            foreach(var weapon in root.GetComponentsInChildren<Transform>(true))
            {
                if(weapon.name!="Sword" && weapon.name!="Bow_hand") continue;
                // Snapshot the source filters before adding children; never clone our own overlay.
                foreach(var filter in weapon.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter.name=="Bash weapon glow") continue;
                    var source=filter.GetComponent<MeshRenderer>(); if(source==null || filter.sharedMesh==null) continue;
                    var glow=new GameObject("Bash weapon glow"); glow.layer=filter.gameObject.layer; glow.transform.SetParent(filter.transform,false);
                    _weapons.Add((source,Renderer(glow,filter.sharedMesh,_weaponMaterial)));
                }
            }
        }
        private void CreateShield(Transform root)
        {
            _shieldMaterial=Material("Thorns translucent shield",new Color(.1f,1.1f,1.35f,.275f),0,true);
            _shieldMesh=ShieldMesh();
            _shield=new GameObject("Thorns spiked shield"); _shield.transform.SetParent(root,false);
            Renderer(_shield,_shieldMesh,_shieldMaterial);
        }
        private static Mesh ShieldMesh(bool spikes = true)
        {
            var vertices=new List<Vector3>(); var colors=new List<Color>(); var indices=new List<int>();
            Vector3 Ellipsoid(Vector3 v) => Vector3.Scale(v,new Vector3(.68f,1.04f,.68f))+Vector3.up*.94f;
            void Triangle(Vector3 a,Vector3 b,Vector3 c,float alpha)
            {
                int index=vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                for(int i=0;i<3;i++) { indices.Add(index+i); colors.Add(new Color(1,1,1,alpha)); }
            }
            Vector3 Unit(float ring,float slice)
            {
                float pitch=Mathf.PI*ring/10,angle=Mathf.PI*2*slice/20;
                return new Vector3(Mathf.Sin(pitch)*Mathf.Cos(angle),Mathf.Cos(pitch),Mathf.Sin(pitch)*Mathf.Sin(angle));
            }
            for(int ring=0;ring<10;ring++) for(int slice=0;slice<20;slice++)
            {
                var a=Ellipsoid(Unit(ring,slice)); var b=Ellipsoid(Unit(ring+1,slice));
                var c=Ellipsoid(Unit(ring+1,slice+1)); var d=Ellipsoid(Unit(ring,slice+1));
                Triangle(a,c,b,.15f); Triangle(a,d,c,.15f);
            }
            // Four staggered rings of faceted spikes, combined into the shell's single draw call.
            for(int ring=0;ring<(spikes ? 4 : 0);ring++) for(int i=0;i<10;i++)
            {
                Vector3 normal=Unit(2+ring*2,(i+.5f*(ring%2))*2);
                Vector3 center=Ellipsoid(normal),tip=center+normal*.23f;
                Vector3 tangent=Vector3.Cross(Vector3.up,normal).normalized*.075f;
                Vector3 up=Vector3.Cross(normal,tangent).normalized*.075f;
                Triangle(center-tangent-up,center+tangent-up,tip,.8f);
                Triangle(center+tangent-up,center+tangent+up,tip,.8f);
                Triangle(center+tangent+up,center-tangent+up,tip,.8f);
                Triangle(center-tangent+up,center-tangent-up,tip,.8f);
            }
            var mesh=new Mesh { name=spikes ? "Thorns shell and spikes" : "Preset shield shell" }; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        public void Dispose()
        {
            if (_health != null) _health.ShieldHit -= OnShieldHit;
            _health=null; _shieldHitAt=double.NegativeInfinity; _presetShieldRenderer=null;
            Destroy(_shield); Destroy(_shieldMesh); Destroy(_shieldMaterial); Destroy(_weaponMaterial);
            Destroy(_presetShield); Destroy(_presetShieldMesh); Destroy(_presetShieldMaterial);
            _presetShield = null; _presetShieldMesh = null; _presetShieldMaterial = null;
            foreach(var pair in _weapons) if(pair.glow!=null) Destroy(pair.glow.gameObject);
            _weapons.Clear(); _shield=null; _shieldMesh=null; _shieldMaterial=_weaponMaterial=null;
        }
        private static void Destroy(UnityEngine.Object value)
        { if(value==null) return; if(value is GameObject go) go.SetActive(false); if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
