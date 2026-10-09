using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>References the same baked map images used by the waiting-room terminal.</summary>
    public sealed class BattleMapPreviews : ScriptableObject
    {
        public Texture2D[] Images;
        private static BattleMapPreviews _instance;
        public static Texture2D Get(byte map)
        {
            if (_instance == null) _instance = Resources.Load<BattleMapPreviews>("BattleMapPreviews");
            return _instance != null && _instance.Images != null && map < _instance.Images.Length ? _instance.Images[map] : null;
        }
    }
}
