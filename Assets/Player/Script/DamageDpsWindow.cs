using System;
using System.Collections.Generic;

namespace BattlePvp.Combat
{
    /// <summary>Actual HP damage received during the trailing one-second window.</summary>
    public sealed class DamageDpsWindow
    {
        private readonly Queue<(double time,float damage)> _hits=new();
        private float _total;
        public void Record(double time,float damage)
        {
            if(!double.IsFinite(time) || !float.IsFinite(damage) || damage<=0) return;
            Sample(time); _hits.Enqueue((time,damage)); _total+=damage;
        }
        public float Sample(double time)
        {
            while(_hits.Count>0 && _hits.Peek().time<=time-1) _total-=_hits.Dequeue().damage;
            if(_hits.Count==0) _total=0;
            return Math.Max(0,_total);
        }
        public void Clear() { _hits.Clear(); _total=0; }
    }
}
