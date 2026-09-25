using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>Applies one-time visibility defaults from an active parent without intercepting later opens.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class UiInitialVisibility : MonoBehaviour
    {
        [SerializeField] private GameObject[] _initiallyHidden;

        private void Awake()
        {
            if (_initiallyHidden == null) return;
            foreach (GameObject target in _initiallyHidden)
                if (target != null) target.SetActive(false);
        }
    }
}
