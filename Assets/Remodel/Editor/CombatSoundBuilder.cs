using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    /// <summary>Original, deterministic Foley-style noise layers; no third-party recordings.</summary>
    public static class CombatSoundBuilder
    {
        private const int Rate = 44100;
        public const string Folder = "Assets/Resources/CombatAudio/";
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);
            WriteSwings();
            for (int i = 0; i < 2; i++) Write("sword-cut-" + i, .24f + i * .03f, 1, 91 + i);
            Write("death-discharge", .82f, 2, 127);
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Folder, "*.wav"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }
        public static void GenerateSwings()
        {
            Directory.CreateDirectory(Folder);
            WriteSwings();
            AssetDatabase.Refresh();
        }
        private static void WriteSwings()
        {
            for (int i = 0; i < 3; i++) Write("sword-air-" + i, .19f + i * .015f, 0, 43 + i);
        }
        private static void Write(string name, float duration, int kind, int seed)
        {
            int count = (int)(Rate * duration);
            var samples = new float[count]; var random = new System.Random(seed);
            float low = 0, band = 0, peak = .001f, phase = 0;
            float airLow = 0, edgeLow = 0, bodyLow = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate, u = t / duration;
                float noise = (float)(random.NextDouble() * 2 - 1);
                low += .045f * (noise - low);
                float sweep = Mathf.Sin(Mathf.PI * u);
                band += (.08f + .42f * sweep) * (noise - band);
                float value;
                if (kind == 0)
                {
                    // A quick blade pass: broad low air movement, a tight bright edge and a short tail.
                    // No pitched oscillator, long hiss or synthetic rising whistle.
                    float pass = Mathf.Pow(Mathf.Clamp01(t / .034f), 2f) * Mathf.Exp(-Mathf.Max(0, t - .034f) * 31f);
                    float brightness = Mathf.Lerp(4200, 950, Mathf.Clamp01(t / .12f));
                    airLow += (1 - Mathf.Exp(-2 * Mathf.PI * 160 / Rate)) * (noise - airLow);
                    edgeLow += (1 - Mathf.Exp(-2 * Mathf.PI * brightness / Rate)) * (noise - edgeLow);
                    bodyLow += (1 - Mathf.Exp(-2 * Mathf.PI * 520 / Rate)) * (noise - bodyLow);
                    float edge = Mathf.Exp(-Mathf.Pow((t - .038f) / .013f, 2f));
                    value = (edgeLow - airLow) * pass + (bodyLow - airLow) * pass * .55f +
                        (noise - edgeLow) * edge * .07f;
                }
                else if (kind == 1)
                {
                    float attack = Mathf.Clamp01(t / .003f);
                    float cut = Mathf.Exp(-t * 27) * attack;
                    float metal = Mathf.Sin(t * (3150 + seed) * 2 * Mathf.PI) * Mathf.Exp(-t * 40);
                    float body = Mathf.Sin(2 * Mathf.PI * (175 * t - 170 * t * t)) * Mathf.Exp(-t * 22);
                    value = ((noise - low) * .5f + band * .65f + body * .48f + metal * .08f) * cut;
                }
                else
                {
                    phase += 2 * Mathf.PI * Mathf.Lerp(170, 42, Mathf.Sqrt(u)) / Rate;
                    float body = Mathf.Sin(phase) * .42f + Mathf.Sin(phase * 2.07f) * .08f;
                    value = (body + low * .65f + (band - low) * Mathf.Exp(-t * 16) * .4f) *
                        Mathf.Clamp01(t / .006f) * Mathf.Exp(-u * 4.5f);
                }
                // Taper both boundaries to avoid digital clicks.
                value *= Mathf.Clamp01((duration - t) / .025f);
                samples[i] = value; peak = Mathf.Max(peak, Mathf.Abs(value));
            }
            using var stream = File.Create(Folder + name + ".wav");
            using var writer = new BinaryWriter(stream);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(Rate); writer.Write(Rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            foreach (float value in samples) writer.Write((short)(value / peak * (kind == 0 ? .65f : .8f) * short.MaxValue));
        }
    }
}
