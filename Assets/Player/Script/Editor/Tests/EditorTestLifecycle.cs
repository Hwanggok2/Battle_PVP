using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    // EditMode does not invoke ordinary MonoBehaviour Awake/OnEnable/OnDisable.
    // These fixtures explicitly enter the lifecycle stage they exercise.
    internal static class EditorTestLifecycle
    {
        internal static void Invoke(object target, string method)
        {
            MethodInfo callback = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(callback, Is.Not.Null, method);
            callback.Invoke(target, null);
        }

        internal static T AddNetwork<T>(GameObject owner) where T : Component
        {
            if (owner.GetComponent<NetworkIdentity>() == null) owner.AddComponent<NetworkIdentity>();
            T component = owner.AddComponent<T>();
            BindNetwork(owner);
            return component;
        }

        internal static void BindNetwork(GameObject owner)
        {
            Invoke(owner.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
        }

        internal static void SetActive(MonoBehaviour component, bool active)
        {
            component.gameObject.SetActive(active);
            Invoke(component, active ? "OnEnable" : "OnDisable");
        }
    }
}
