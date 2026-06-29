using System;
using UnityEngine;

namespace BattlePvp.Audio
{
    [CreateAssetMenu(menuName = "Battle PVP/Audio/BGM Settings", fileName = "BgmSettings")]
    public sealed class BgmSettings : ScriptableObject
    {
        [Serializable]
        public struct SceneBgm
        {
            public string SceneName;
            public AudioClip Clip;
            public bool OverrideLoop;
            public bool Loop;
        }

        [Header("Playback")]
        [Range(0f, 1f)] public float MasterVolume = 0.6f;
        public float FadeSeconds = 0.6f;
        public bool LoopByDefault = true;

        [Header("Scene Clips")]
        public AudioClip DefaultClip;
        public SceneBgm[] SceneBgms;
    }
}
