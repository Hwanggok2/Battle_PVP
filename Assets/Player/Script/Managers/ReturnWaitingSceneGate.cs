using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BattlePvp.Networking
{
    [DefaultExecutionOrder(-32000)]
    public sealed class ReturnWaitingSceneGate : MonoBehaviour
    {
        private readonly List<GameObject> _presentation = new();
        private readonly List<Renderer> _renderers = new();
        private readonly List<Behaviour> _lighting = new();
        public bool IsReturnScene { get; private set; }
        private void Awake()
        {
            if (!(NetworkManager.singleton is BattleNetworkManager manager) || !manager.IsPreparingReturnWaiting) return;
            IsReturnScene = true;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root == gameObject) continue;
                root.transform.position += BattleNetworkManager.ReturnWaitingOffset;
                bool presentation = root.GetComponent<Canvas>() != null || root.GetComponent<Camera>() != null ||
                    root.GetComponent<UnityEngine.EventSystems.EventSystem>() != null ||
                    root.GetComponent<BattlePvp.Logic.GameInputController>() != null ||
                    root.GetComponent<BattlePvp.UI.WaitingRoomTerminal>() != null;
                if (presentation)
                {
                    if (root.activeSelf || root.GetComponent<NetworkIdentity>() != null) _presentation.Add(root);
                    if (root.TryGetComponent<NetworkIdentity>(out var identity)) identity.gameObject.hideFlags = HideFlags.NotEditable;
                    root.SetActive(false);
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.GetComponentInParent<NetworkIdentity>() == null && !renderer.forceRenderingOff)
                    { _renderers.Add(renderer); renderer.forceRenderingOff = true; }
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    if (light.enabled) { _lighting.Add(light); light.enabled = false; }
                foreach (var volume in root.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true))
                    if (volume.enabled) { _lighting.Add(volume); volume.enabled = false; }
            }
        }
        public void Present()
        {
            foreach (var root in _presentation) if (root != null) root.SetActive(true);
            foreach (var renderer in _renderers) if (renderer != null) renderer.forceRenderingOff = false;
            foreach (var light in _lighting) if (light != null) light.enabled = true;
        }
    }
}
