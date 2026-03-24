using System.IO;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTools
{
    /// <summary>
    /// Status_VFX Raw Image용 심리스 흑백 회로 노이즈 텍스처를 절차 생성합니다.
    /// </summary>
    public static class StatusVfxCircuitNoiseGenerator
    {
        private const int TextureSize = 512;
        private const int CircuitCells = 24;
        private const string OutputPath = "Assets/Status_VFX_CircuitNoise.png";

        /// <summary>
        /// 회로 노이즈 텍스처를 생성하고 Assets 경로에 저장합니다.
        /// </summary>
        [MenuItem("BattlePVP/VFX/Generate Status VFX Circuit Noise")]
        public static void Generate()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "Status_VFX_CircuitNoise"
            };

            var pixels = new Color32[TextureSize * TextureSize];

            for (int y = 0; y < TextureSize; y++)
            {
                float v = (float)y / TextureSize;
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = (float)x / TextureSize;
                    float value = SampleCircuitNoise(u, v);
                    byte c = (byte)Mathf.RoundToInt(value * 255f);
                    pixels[(y * TextureSize) + x] = new Color32(c, c, c, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            File.WriteAllBytes(OutputPath, png);
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(OutputPath);
            AssetDatabase.Refresh();

            Debug.Log($"[StatusVFX] Generated seamless circuit noise: {OutputPath}");
        }

        private static void ConfigureImporter(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
                return;

            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 타일 경계에서 끊김 없는(Periodic) 회로풍 노이즈를 샘플링합니다.
        /// </summary>
        private static float SampleCircuitNoise(float u, float v)
        {
            float baseNoise = 0.22f + 0.28f * FbmPeriodic(u, v, 4, 2.02f, 0.5f);
            float detailNoise = 0.14f * FbmPeriodic(u * 3.7f, v * 3.7f, 3, 2.1f, 0.5f);

            float circuitTracks = SampleCircuitTracks(u, v);
            float intersections = SampleIntersections(u, v);
            float scanPulse = 0.06f * Mathf.Sin((u + v) * Mathf.PI * 2f * 5f);

            float combined = baseNoise + detailNoise + (0.46f * circuitTracks) + (0.24f * intersections) + scanPulse;
            return Mathf.Clamp01(combined);
        }

        private static float SampleCircuitTracks(float u, float v)
        {
            float gx = u * CircuitCells;
            float gy = v * CircuitCells;

            int cellX = Mathf.FloorToInt(gx);
            int cellY = Mathf.FloorToInt(gy);
            float localX = Frac(gx) - 0.5f;
            float localY = Frac(gy) - 0.5f;

            float orientation = HashCell(cellX, cellY, 11);
            bool horizontal = orientation > 0.5f;

            float widthJitter = Mathf.Lerp(0.045f, 0.085f, HashCell(cellX, cellY, 19));
            float coreDistance = horizontal ? Mathf.Abs(localY) : Mathf.Abs(localX);

            float lengthMask = horizontal
                ? SmoothStepInv(0.54f, 0.30f, Mathf.Abs(localX))
                : SmoothStepInv(0.54f, 0.30f, Mathf.Abs(localY));

            float breakGate = HashCell(cellX, cellY, 37) > 0.18f ? 1f : 0f;
            float track = SmoothStepInv(widthJitter, widthJitter - 0.02f, coreDistance) * lengthMask * breakGate;

            // 약한 보조 라인을 추가해 "회로판 결"을 만든다.
            float aux = Mathf.Min(Mathf.Abs(localX), Mathf.Abs(localY));
            float auxLine = SmoothStepInv(0.04f, 0.018f, aux) * 0.22f;
            return Mathf.Clamp01(track + auxLine);
        }

        private static float SampleIntersections(float u, float v)
        {
            float gx = u * CircuitCells;
            float gy = v * CircuitCells;
            int cellX = Mathf.FloorToInt(gx);
            int cellY = Mathf.FloorToInt(gy);

            float maxNode = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int nx = cellX + ox;
                    int ny = cellY + oy;

                    float gate = HashCell(nx, ny, 73);
                    if (gate < 0.68f)
                        continue;

                    float nodeU = (Wrap(nx, CircuitCells) + 0.5f) / CircuitCells;
                    float nodeV = (Wrap(ny, CircuitCells) + 0.5f) / CircuitCells;

                    float dx = ToroidalDistance(u, nodeU);
                    float dy = ToroidalDistance(v, nodeV);
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));

                    float node = SmoothStepInv(0.032f, 0.008f, d);
                    if (node > maxNode)
                        maxNode = node;
                }
            }

            return maxNode;
        }

        private static float FbmPeriodic(float u, float v, int octaves, float lacunarity, float gain)
        {
            float amplitude = 1f;
            float frequency = 1f;
            float sum = 0f;
            float norm = 0f;

            for (int i = 0; i < octaves; i++)
            {
                float n = ValueNoisePeriodic(u * frequency, v * frequency);
                sum += n * amplitude;
                norm += amplitude;

                frequency *= lacunarity;
                amplitude *= gain;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        private static float ValueNoisePeriodic(float x, float y)
        {
            int xi0 = Mathf.FloorToInt(x);
            int yi0 = Mathf.FloorToInt(y);
            int xi1 = xi0 + 1;
            int yi1 = yi0 + 1;

            float tx = Frac(x);
            float ty = Frac(y);
            float sx = tx * tx * (3f - (2f * tx));
            float sy = ty * ty * (3f - (2f * ty));

            float p00 = HashGrid(xi0, yi0);
            float p10 = HashGrid(xi1, yi0);
            float p01 = HashGrid(xi0, yi1);
            float p11 = HashGrid(xi1, yi1);

            float ix0 = Mathf.Lerp(p00, p10, sx);
            float ix1 = Mathf.Lerp(p01, p11, sx);
            return Mathf.Lerp(ix0, ix1, sy);
        }

        private static float HashGrid(int x, int y)
        {
            int wx = Wrap(x, 1024);
            int wy = Wrap(y, 1024);
            uint h = (uint)(wx * 374761393) ^ (uint)(wy * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215f;
        }

        private static float HashCell(int x, int y, int salt)
        {
            int wx = Wrap(x, CircuitCells);
            int wy = Wrap(y, CircuitCells);
            uint h = (uint)(wx * 1597334677) ^ (uint)(wy * 3812015801u) ^ (uint)(salt * 1103515245);
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0x00FFFFFF) / 16777215f;
        }

        private static int Wrap(int value, int modulo)
        {
            int r = value % modulo;
            return r < 0 ? r + modulo : r;
        }

        private static float Frac(float v)
        {
            return v - Mathf.Floor(v);
        }

        private static float ToroidalDistance(float a, float b)
        {
            float d = Mathf.Abs(a - b);
            return d > 0.5f ? 1f - d : d;
        }

        private static float SmoothStepInv(float edge0, float edge1, float x)
        {
            float t = Mathf.InverseLerp(edge0, edge1, x);
            return t * t * (3f - (2f * t));
        }
    }
}

