using UnityEngine;

namespace BattlePvp.Logic
{
    /// <summary>
    /// [Legacy] This functionality has been moved to StatManager.ApplyStats() as requested by the user.
    /// Please remove this component from the Player object.
    /// </summary>
    public sealed class CharacterScaler : MonoBehaviour
    {
        private void Start()
        {
            Debug.LogWarning("[CharacterScaler] This component is legacy. Scaling is handled by StatManager.");
        }
    }
}
