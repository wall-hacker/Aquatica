using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquatica.Underwater
{
    [Serializable]
    public class SeafloorSettings
    {
        [Tooltip("Side length of the generated square (m). Fog hides the edges.")]
        public float size = 160f;
        [Tooltip("Distance between height samples (m).")]
        public float cellSize = 0.8f;

        [Header("Large undulations")]
        public float baseScale = 85f;
        public float baseAmplitude = 6f;
        [Tooltip("Domain warping makes the shapes flow instead of looking like plain noise blobs.")]
        public float warpScale = 60f;
        public float warpStrength = 18f;

        [Header("Canyons")]
        public float canyonScale = 150f;
        public float canyonDepth = 4f;
        [Range(0.01f, 0.3f)] public float canyonWidth = 0.1f;

        [Header("Rock outcrops")]
        [Range(0f, 1f)] public float rockCoverage = 0.35f;
        public float rockScale = 30f;
        public float rockHeight = 5f;
        [Tooltip("Layered (sedimentary) steps on the outcrops; 0 disables.")]
        public int rockTerraces = 5;

        [Header("Sand")]
        public float duneWavelength = 7f;
        public float duneHeight = 0.35f;
        public float detailScale = 4f;
        public float detailHeight = 0.18f;
        [Tooltip("Thermal erosion passes: loose sediment slumps until it rests at its angle of repose.")]
        public int erosionIterations = 12;
        [Range(10f, 60f)] public float sandReposeAngle = 30f;
    }

    /// <summary>
    /// Procedural seafloor heightfield. Heights come from layered noise (domain-warped fBm undulations,
    /// meandering canyons, terraced ridged-multifractal rock outcrops and asymmetric sand dunes), then a
    /// thermal erosion pass lets loose sand slump while rock stays steep. Per-vertex colours carry the
    /// material masks for the Aquatica/Seafloor shader: R = rock, G = silt, B = gravel, A = occlusion.
    /// Coordinates are local to the seafloor object, centred on (0, 0).
    /// </summary>
    public class Seafloor
    {
        const int ChWarpX = 0, ChWarpZ = 1, ChBase = 2, ChCanyon = 3, ChRockMask = 4, ChRidge = 5,
            ChDune = 6, ChDuneAmp = 7, ChDetail = 8, ChGravel = 9;

        readonly SeafloorSettings settings;
        readonly Noise noise;
        readonly Vector2 duneDirection;

        public int Resolution { get; }
        public float Size => settings.size;
        public float CellSize { get; }
        public float[] Heights { get; }
        readonly Color32[] masks;

        public Seafloor(SeafloorSettings settings, int seed, float flattenRadius, float centreHeight)
        {
            this.settings = settings;
            var rng = new System.Random(seed);
            noise = new Noise(rng);
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            duneDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            Resolution = Mathf.Max(2, Mathf.RoundToInt(settings.size / settings.cellSize) + 1);
            CellSize = settings.size / (Resolution - 1);
            Heights = new float[Resolution * Resolution];
            masks = new Color32[Resolution * Resolution];

            var rock = new float[Heights.Length];
            var canyon = new float[Heights.Length];
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = z * Resolution + x;
                Heights[i] = RawHeight(LocalX(x), LocalX(z), out rock[i], out canyon[i]);
            }

            Erode(rock);
            Flatten(flattenRadius, centreHeight);
            BuildMasks(rock, canyon);
        }

        float LocalX(int index) => index * CellSize - settings.size * 0.5f;

        float RawHeight(float x, float z, out float rockAmount, out float canyonAmount)
        {
            var s = settings;

            float qx = x + noise.Fbm(x / s.warpScale, z / s.warpScale, ChWarpX, 3) * s.warpStrength;
            float qz = z + noise.Fbm(x / s.warpScale, z / s.warpScale, ChWarpZ, 3) * s.warpStrength;

            float h = noise.Fbm(qx / s.baseScale, qz / s.baseScale, ChBase, 5) * s.baseAmplitude;

            // Meandering canyons follow the zero crossings of a low-frequency noise
            float c = Mathf.Abs(noise.Fbm(qx / s.canyonScale, qz / s.canyonScale, ChCanyon, 3));
            canyonAmount = 1f - Smooth(0f, s.canyonWidth, c);
            h -= s.canyonDepth * canyonAmount * canyonAmount * (3f - 2f * canyonAmount);

            // Rock outcrops: ridged multifractal, limited to patches and stepped like layered rock
            float patch = noise.Fbm(x / (s.rockScale * 3.5f), z / (s.rockScale * 3.5f), ChRockMask, 3) * 0.5f + 0.5f;
            float threshold = Mathf.Lerp(0.85f, 0.2f, s.rockCoverage);
            float rockMask = Smooth(threshold - 0.08f, threshold + 0.08f, patch);
            float ridge = noise.Ridged(qx / s.rockScale, qz / s.rockScale, ChRidge, 4);
            float r = ridge * ridge;
            if (s.rockTerraces > 0)
            {
                float steps = r * s.rockTerraces;
                float floor = Mathf.Floor(steps);
                float stepped = (floor + Smooth(0.55f, 1f, steps - floor)) / s.rockTerraces;
                r = Mathf.Lerp(r, stepped, 0.6f);
            }
            h += r * s.rockHeight * rockMask;
            rockAmount = rockMask * Smooth(0.12f, 0.45f, ridge);

            // Asymmetric dunes (gentle stoss side, steep lee side) perpendicular to the current
            float phase = (qx * duneDirection.x + qz * duneDirection.y) / s.duneWavelength
                          + noise.Fbm(qx / 25f, qz / 25f, ChDune, 2) * 1.5f;
            float f = phase - Mathf.Floor(phase);
            float dune = f < 0.75f ? f / 0.75f : (1f - f) / 0.25f;
            dune = dune * dune * (3f - 2f * dune);
            float duneAmp = 0.5f + 0.5f * noise.Fbm(qx / 40f, qz / 40f, ChDuneAmp, 2);
            h += dune * s.duneHeight * duneAmp * (1f - rockMask);

            h += noise.Fbm(x / s.detailScale, z / s.detailScale, ChDetail, 3) * s.detailHeight;
            return h;
        }

        /// <summary>Thermal erosion: material above the repose slope slides to lower neighbours (rock barely moves).</summary>
        void Erode(float[] rock)
        {
            int n = Resolution;
            float sandTalus = Mathf.Tan(settings.sandReposeAngle * Mathf.Deg2Rad) * CellSize;
            float rockTalus = Mathf.Tan(80f * Mathf.Deg2Rad) * CellSize;
            var delta = new float[Heights.Length];

            for (int iteration = 0; iteration < settings.erosionIterations; iteration++)
            {
                Array.Clear(delta, 0, delta.Length);
                for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    float h = Heights[i];
                    float talus = Mathf.Lerp(sandTalus, rockTalus, rock[i]);
                    Move(i, i - 1, h, talus, delta);
                    Move(i, i + 1, h, talus, delta);
                    Move(i, i - n, h, talus, delta);
                    Move(i, i + n, h, talus, delta);
                }
                for (int i = 0; i < Heights.Length; i++)
                    Heights[i] += delta[i];
            }
        }

        void Move(int from, int to, float h, float talus, float[] delta)
        {
            float diff = h - Heights[to] - talus;
            if (diff <= 0f)
                return;
            float amount = diff * 0.12f;
            delta[from] -= amount;
            delta[to] += amount;
        }

        /// <summary>Levels the ground under the submarine and shifts everything so the centre sits at centreHeight.</summary>
        void Flatten(float radius, float centreHeight)
        {
            float h0 = SampleLocal(0f, 0f);
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = z * Resolution + x;
                float d = new Vector2(LocalX(x), LocalX(z)).magnitude;
                float keep = Smooth(radius * 0.35f, radius, d);
                Heights[i] = Mathf.Lerp(h0, Heights[i], keep) - h0 + centreHeight;
            }
        }

        void BuildMasks(float[] rock, float[] canyon)
        {
            int n = Resolution;
            const int k = 3; // curvature measured over a few cells so it follows landforms, not noise
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float h = Heights[i];
                float around = (H(x - k, z) + H(x + k, z) + H(x, z - k) + H(x, z + k)) * 0.25f;
                float concavity = (around - h) / (k * CellSize); // > 0 in hollows and valleys

                float r = rock[i];
                float silt = Mathf.Clamp01(Smooth(0.02f, 0.25f, concavity) + canyon[i] * 0.8f) * (1f - r);

                float patches = Smooth(0.15f, 0.5f, noise.Fbm(LocalX(x) / 14f, LocalX(z) / 14f, ChGravel, 3));
                float fringe = Smooth(0.0f, 0.25f, r) * (1f - Smooth(0.5f, 0.9f, r));
                float gravel = Mathf.Max(patches * 0.85f, fringe) * (1f - r) * (1f - silt * 0.7f);

                float occlusion = 1f - Smooth(0.0f, 0.5f, concavity) * 0.45f;

                masks[i] = new Color(r, silt, gravel, occlusion);
            }
        }

        float H(int x, int z) => Heights[Mathf.Clamp(z, 0, Resolution - 1) * Resolution + Mathf.Clamp(x, 0, Resolution - 1)];

        /// <summary>Bilinear height at a local (x, z) position.</summary>
        public float SampleLocal(float x, float z)
        {
            float fx = Mathf.Clamp((x + settings.size * 0.5f) / CellSize, 0f, Resolution - 1.001f);
            float fz = Mathf.Clamp((z + settings.size * 0.5f) / CellSize, 0f, Resolution - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(H(ix, iz), H(ix + 1, iz), tx);
            float b = Mathf.Lerp(H(ix, iz + 1), H(ix + 1, iz + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }

        public Mesh BuildMesh()
        {
            int n = Resolution;
            var vertices = new Vector3[n * n];
            var normals = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                vertices[i] = new Vector3(LocalX(x), Heights[i], LocalX(z));
                float dx = H(x + 1, z) - H(x - 1, z);
                float dz = H(x, z + 1) - H(x, z - 1);
                normals[i] = new Vector3(-dx, 2f * CellSize, -dz).normalized;
            }

            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int i = z * n + x;
                triangles[t++] = i;
                triangles[t++] = i + n;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + n;
                triangles[t++] = i + n + 1;
            }

            var mesh = new Mesh { name = "Seafloor" };
            mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = masks;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Seeded fBm / ridged noise on top of Mathf.PerlinNoise, with independent offsets per channel and octave.</summary>
        class Noise
        {
            const int MaxOctaves = 8;
            readonly Vector2[] offsets = new Vector2[16 * MaxOctaves];

            public Noise(System.Random rng)
            {
                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = new Vector2((float)rng.NextDouble() * 2000f - 1000f, (float)rng.NextDouble() * 2000f - 1000f);
            }

            float Perlin(float x, float z, int channel, int octave)
            {
                var o = offsets[channel * MaxOctaves + octave];
                return Mathf.PerlinNoise(x + o.x, z + o.y) * 2f - 1f;
            }

            /// <summary>Fractal Brownian motion, roughly in [-1, 1].</summary>
            public float Fbm(float x, float z, int channel, int octaves)
            {
                float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Perlin(x * frequency, z * frequency, channel, o) * amplitude;
                    norm += amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2.03f;
                }
                return sum / norm;
            }

            /// <summary>Ridged multifractal in [0, 1]: sharp crests whose detail is weighted by the previous octave.</summary>
            public float Ridged(float x, float z, int channel, int octaves)
            {
                float sum = 0f, amplitude = 0.5f, frequency = 1f, weight = 1f, norm = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    float signal = 1f - Mathf.Abs(Perlin(x * frequency, z * frequency, channel, o));
                    signal *= signal * weight;
                    weight = Mathf.Clamp01(signal * 2f);
                    sum += signal * amplitude;
                    norm += amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2.1f;
                }
                return Mathf.Clamp01(sum / norm);
            }
        }
    }
}
