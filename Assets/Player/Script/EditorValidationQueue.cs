#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace BattlePvp
{
    // OnValidate can run on Unity's loading thread. Only enqueue managed work there;
    // component/asset access belongs to the next editor update on the main thread.
    internal static class EditorValidationQueue
    {
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.update -= Drain;
            EditorApplication.update += Drain;
        }

        internal static void Enqueue(Action apply) => Pending.Enqueue(apply);

        private static void Drain()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            int count = Pending.Count;
            while (count-- > 0 && Pending.TryDequeue(out Action apply))
            {
                try { apply(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
#endif
