using System;
using System.IO;
using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    /// <summary>Original short mechanical/noise cues. No external recordings or runtime synthesis.</summary>
    public static class SkillSoundBuilder
    {
        private const int Rate = 44100;
        public static string CueName(JobSkillKind kind) => kind switch
        {
            JobSkillKind.MonostatStrLifesteal => "skill-blood-charge",
            JobSkillKind.MonostatAgiPoison => "skill-venom",
            JobSkillKind.MonostatConKick => "skill-kick",
            JobSkillKind.MonostatDefTaunt => "skill-taunt",
            JobSkillKind.StrategistRoll => "skill-dash",
            JobSkillKind.PolymathRoll => "skill-roll",
            JobSkillKind.StrategistPresetChange or JobSkillKind.PolymathPresetChange => "skill-reconfigure",
            _ => "skill-weapon-swap"
        };
        [MenuItem("Battle PvP/Audio/Generate Skill And Death Cues")]
        public static void GenerateAndBind()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before generating audio.");
            Directory.CreateDirectory(CombatSoundBuilder.Folder);
            string[] names = { "skill-blood-charge", "skill-venom", "skill-kick", "skill-taunt", "skill-dash", "skill-roll", "skill-reconfigure", "skill-weapon-swap", "death-discharge" };
            float[] lengths = { .65f, .52f, .30f, .72f, .30f, .42f, .56f, .36f, 1.05f };
            for (int i = 0; i < names.Length; i++) Write(names[i], lengths[i], i);
            AssetDatabase.Refresh();
            foreach (string name in names)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(CombatSoundBuilder.Folder + name + ".wav");
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            }
            foreach (string guid in AssetDatabase.FindAssets("t:JobSkillData", new[] { "Assets/Player/skill" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<JobSkillData>(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(data);
                so.FindProperty("_useSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(CombatSoundBuilder.Folder + CueName(data.SkillKind) + ".wav");
                so.FindProperty("_sfxVolume").floatValue = .7f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }
        private static float Envelope(float t, float start, float attack, float decay)
        {
            float age = t - start;
            return age < 0 ? 0 : Mathf.Min(1, age / attack) * Mathf.Exp(-age * decay);
        }
        private static void Write(string name, float length, int kind)
        {
            var samples = new float[(int)(length * Rate)]; var random = new System.Random(671 + kind);
            float low = 0, mid = 0, high = 0, phase = 0, peak = .001f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate, u = t / length, noise = (float)(random.NextDouble() * 2 - 1);
                low += .028f * (noise - low); mid += .18f * (noise - mid); high += .65f * (noise - high);
                phase += 2 * Mathf.PI * Mathf.Lerp(kind == 3 ? 145 : 110, 42, u) / Rate;
                float thud = Mathf.Sin(phase), air = mid - low, edge = high - mid;
                float value = kind switch
                {
                    0 => (thud * .32f + low * 1.5f + air * .16f) * Envelope(t, 0, .045f, 6) + edge * .14f * Envelope(t, .16f, .01f, 24),
                    1 => air * Envelope(t, 0, .035f, 9) + edge * .45f * (Envelope(t, .10f, .006f, 36) + Envelope(t, .23f, .008f, 42)),
                    2 => air * .65f * Envelope(t, 0, .026f, 27) + (thud * .32f + low) * Envelope(t, .065f, .004f, 25),
                    3 => (thud * .35f + Mathf.Sin(phase * 1.51f) * .1f + low) * Envelope(t, 0, .009f, 6) + edge * .3f * Envelope(t, 0, .002f, 45),
                    4 => air * 1.2f * Envelope(t, 0, .022f, 24) + edge * .12f * Envelope(t, .04f, .01f, 35),
                    5 => (air * .7f + low) * Envelope(t, 0, .02f, 16) + (low + thud * .16f) * Envelope(t, .15f, .008f, 24),
                    6 => (edge * .23f + mid * .5f) * (Envelope(t, 0, .004f, 34) + Envelope(t, .1f, .004f, 34) + Envelope(t, .21f, .004f, 24)) + thud * .15f * Envelope(t, .20f, .012f, 12),
                    7 => air * .5f * Envelope(t, 0, .022f, 22) + (edge * .8f + Mathf.Sin(2 * Mathf.PI * t * 1730) * .08f) * Envelope(t, .09f, .002f, 45),
                    _ => (thud * .42f + low * 1.3f) * Envelope(t, 0, .006f, 5) + (edge * .24f + air * .7f) * Envelope(t, 0, .003f, 16) + low * .7f * Envelope(t, .2f, .015f, 12)
                };
                value *= Mathf.Clamp01(t / .002f) * Mathf.Clamp01((length - t) / .04f);
                samples[i] = value; peak = Mathf.Max(peak, Mathf.Abs(value));
            }
            using var writer = new BinaryWriter(File.Create(CombatSoundBuilder.Folder + name + ".wav"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float value in samples) writer.Write((short)(value / peak * .72f * short.MaxValue));
        }
    }
}
