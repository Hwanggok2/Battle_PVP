using BattlePvp.Combat;
using TMPro;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>A shared, server-confirmed DPS readout above each training target.</summary>
    public sealed class TrainingDpsLabel : MonoBehaviour
    {
        private DummyHealth _dummy;
        private TMP_Text _label;
        private float _shown=-1;
        private string _format;
        public void Bind(DummyHealth dummy)
        {
            _dummy=dummy; _label=GetComponent<TMP_Text>();
            _format=SkillGameData.Text("UI_TrainingDps","DPS {0:0.0}");
        }
        private void LateUpdate()
        {
            if(_dummy==null || _label==null) return;
            float dps=_dummy.CurrentDps;
            if(!Mathf.Approximately(dps,_shown)) { _shown=dps; _label.text=string.Format(System.Globalization.CultureInfo.InvariantCulture,_format,dps); }
            var camera=Camera.main;
            if(camera!=null) transform.rotation=camera.transform.rotation;
        }
    }
}
