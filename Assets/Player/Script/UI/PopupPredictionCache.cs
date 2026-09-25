using System.Collections.Generic;

namespace BattlePvp.UI
{
    /// <summary>동일 예측 적중의 중복 표시를 막고 만료된 항목만 앞에서 제거한다.</summary>
    public sealed class PopupPredictionCache
    {
        private readonly HashSet<(uint, uint, uint)> _keys = new HashSet<(uint, uint, uint)>();
        private readonly Queue<((uint, uint, uint) key, float expiresAt)> _expiry =
            new Queue<((uint, uint, uint), float)>();
        private float _lastTime;
        public int Count => _keys.Count;

        public bool TryClaim(uint attacker, uint victim, uint prediction, float now)
        {
            if (!float.IsFinite(now)) return false;
            if (now < _lastTime) Clear();
            _lastTime = now;
            while (_expiry.Count > 0 && _expiry.Peek().expiresAt <= now)
                _keys.Remove(_expiry.Dequeue().key);
            var key = (attacker, victim, prediction);
            if (!_keys.Add(key)) return false;
            _expiry.Enqueue((key, now + 30f));
            return true;
        }

        public void Clear()
        {
            _keys.Clear();
            _expiry.Clear();
            _lastTime = 0f;
        }
    }
}
