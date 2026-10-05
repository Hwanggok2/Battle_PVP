using System;
using System.Collections.Generic;
using BattlePvp.UI;
using TMPro;
using UnityEngine;

namespace BattlePvp.Combat
{
    public sealed class SkillTrapPreview : IDisposable
    {
        private GameObject _ghost, _canvas;
        private TMP_Text _hint;
        private Renderer[] _renderers;
        private readonly List<Material> _materials = new();
        private readonly MaterialPropertyBlock _color = new();
        public void Tick(ExpandedSkillController owner)
        {
            bool active=owner.Owner && owner.TrapReady;
            if(active && _ghost==null) Create();
            if(_ghost==null) return;
            _ghost.SetActive(active); _canvas.SetActive(active);
            if(!active) return;
            bool valid=owner.TryGetTrapPlacement(out var point);
            _ghost.transform.position=point;
            Color tint=valid ? new Color(.15f,1,.8f,.38f) : new Color(1,.15f,.15f,.38f);
            _color.SetColor("_BaseColor",tint); _color.SetColor("_Color",tint);
            foreach(var renderer in _renderers) renderer.SetPropertyBlock(_color);
            _hint.color=valid ? new Color(.45f,1,.85f) : new Color(1,.4f,.4f);
            _hint.text=valid ? SkillGameData.Text("UI_TrapPreviewReady","좌클릭 설치 · 스킬 키 취소") : SkillGameData.Text("UI_TrapPreviewBlocked","설치할 수 없는 위치 · 스킬 키 취소");
        }
        private void Create()
        {
            var prefab=SkillPresentationCatalog.Instance?.Find((int)JobSkillKind.Trap)?.Prefab;
            if(prefab==null) return;
            _ghost=UnityEngine.Object.Instantiate(prefab); _ghost.name="Trap placement preview";
            foreach(var collider in _ghost.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            _renderers=_ghost.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in _renderers)
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++) if(materials[i]!=null)
                { materials[i]=SkillStealthPresentation.TransparentCopy(materials[i],.38f); _materials.Add(materials[i]); }
                renderer.sharedMaterials=materials;
            }
            _canvas=new GameObject("Trap placement hint",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
            var canvas=_canvas.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=120;
            var scaler=_canvas.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1600,900);
            var label=new GameObject("Hint",typeof(RectTransform),typeof(TextMeshProUGUI)); label.transform.SetParent(_canvas.transform,false);
            _hint=label.GetComponent<TextMeshProUGUI>(); _hint.font=SkillPresentationCatalog.Instance.Font; _hint.fontSize=20; _hint.alignment=TextAlignmentOptions.Center; _hint.raycastTarget=false;
            _hint.rectTransform.sizeDelta=new Vector2(660,40); _hint.rectTransform.anchoredPosition=new Vector2(0,-110);
        }
        public void Dispose()
        {
            Destroy(_ghost); Destroy(_canvas); foreach(var material in _materials) Destroy(material);
            _ghost=_canvas=null; _materials.Clear();
        }
        private static void Destroy(UnityEngine.Object value)
        { if(value==null) return; if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
