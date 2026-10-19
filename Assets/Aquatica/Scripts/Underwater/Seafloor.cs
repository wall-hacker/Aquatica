using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquatica.Underwater
{
    [Serializable]
    public class SeafloorSettings
    {
        [Tooltip("Side length of the generated square (m). Fog hides the edges.")]
        public float size = 420f;
        [Tooltip("Distance between height samples (m).")]
        public float cellSize = 1f;
        [Tooltip("The mesh is split into square tiles of this many cells so only what the camera sees is drawn.")]
        public int tileCells = 60;
        [Tooltip("Dramatic features (trenches, crevasses, tall rock) fade out within this distance of the start.")]
        public float calmRadius = 70f;

        [Header("Large undulations")]
        public float baseScale = 220f;
        public float baseAmplitude = 14f;
        [Tooltip("Domain warping makes the shapes flow instead of looking like plain noise blobs.")]
        public float warpScale = 120f;
        public float warpStrength = 40f;

        [Header("Canyons")]
        public float canyonScale = 200f;
        public float canyonDepth = 6f;
        [Range(0.01f, 0.3f)] public float canyonWidth = 0.1f;

        [Header("Trenches (deep, steep-walled, wide enough to fly into)")]
        public float trenchScale = 340f;
        public float trenchDepth = 32f;
        [Range(0.01f, 0.2f)] public float trenchWidth = 0.08f;
        [Tooltip("How much the trench floor depth varies along its length (0-1).")]
        [Range(0f, 1f)] public float trenchFloorVariation = 0.45f;

        [Header("Crevasses (narrow jagged cracks)")]
        public float crevasseScale = 90f;
        public float crevasseDepth = 10f;
        [Range(0.005f, 0.1f)] public float crevasseWidth = 0.05f;
        [Range(0f, 1f)] public float crevasseCoverage = 0.45f;
        [Tooltip("Trenches and crevasses narrower than this (m) are filled up to a shallow crack, so whatever is deep is wide enough to fly into.")]
        public float minPassageWidth = 13f;
        [Tooltip("Depth (m) left in the parts that are too narrow.")]
        public float narrowCrackDepth = 2.5f;

        [Header("Rock outcrops")]
        [Range(0f, 1f)] public float rockCoverage = 0.45f;
        public float rockScale = 45f;
        public float rockHeight = 18f;
        [Tooltip("Layered (sedimentary) steps on the outcrops; 0 disables.")]
        public int rockTerraces = 6;

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
    /// meandering canyons, deep steep-walled trenches, narrow jagged crevasses, terraced ridged-multifractal rock
    /// outcrops and asymmetric sand dunes), then a thermal erosion pass lets loose sand slump while rock (and
    /// trench/crevasse walls) stays steep. Per-vertex colours carry the material masks for the Aquatica/Seafloor
    /// shader: R = rock, G = silt, B = gravel, A = occlusion. Rows are generated in parallel and the mesh is
    /// built as tiles. Coordinates are local to the seafloor object, centred on (0, 0).
    /// </summary>
    public class Seafloor
    {
        const int ChWarpX = 0, ChWarpZ = 1, ChBase = 2, ChCanyon = 3, ChRockMask = 4, ChRidge = 5,
            ChDune = 6, ChDuneAmp = 7, ChDetail = 8, ChGravel = 9, ChTrench = 10, ChTrenchFloor = 11,
            ChCrevasse = 12, ChCrevasseMask = 13, ChCrevasseWarp = 14;

        readonly SeafloorSettings settings;
        readonly Noise noise;
        readonly Vector2 duneDirection;

        public int Resolution { get; }
        public float Size => settings.size;
        public float CellSize { get; }
        public float[] Heights { get; }
        readonly Color32[] masks;
        readonly float[] passages;

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
            passages = new float[Resolution * Resolution];
            masks = new Color32[Resolution * Resolution];

            var rock = new float[Heights.Length];
            var canyon = new float[Heights.Length];
            var trench = new float[Heights.Length];
            var trenchDepth = new float[Heights.Length];
            var crack = new float[Heights.Length];
            var crackDepth = new float[Heights.Length];
            int n = Resolution;
            Parallel.For(0, n, z =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    Heights[i] = RawHeight(LocalX(x), LocalX(z), out rock[i], out canyon[i],
                        out trench[i], out trenchDepth[i], out crack[i], out crackDepth[i]);
                }
            });

            CarvePassages(trench, trenchDepth, crack, crackDepth, rock, canyon);
            Erode(rock);
            Flatten(flattenRadius, centreHeight);
            BuildMasks(rock, canyon);
        }

        float LocalX(int index) => index * CellSize - settings.size * 0.5f;

        float RawHeight(float x, float z, out float rockAmount, out float canyonAmount,
            out float trench, out float trenchDepth, out float crack, out float crackDepth)
        {
            var s = settings;
            // Dramatic features stay away from the start so the submarine begins in open water
            float wild = Smooth(s.calmRadius * 0.4f, s.calmRadius, Mathf.Sqrt(x * x + z * z));

            float qx = x + noise.Fbm(x / s.warpScale, z / s.warpScale, ChWarpX, 3) * s.warpStrength;
            float qz = z + noise.Fbm(x / s.warpScale, z / s.warpScale, ChWarpZ, 3) * s.warpStrength;

            float h = noise.Fbm(qx / s.baseScale, qz / s.baseScale, ChBase, 5) * s.baseAmplitude;

            // Meandering canyons follow the zero crossings of a low-frequency noise
            float c = Mathf.Abs(noise.Fbm(qx / s.canyonScale, qz / s.canyonScale, ChCanyon, 3));
            canyonAmount = 1f - Smooth(0f, s.canyonWidth, c);
            h -= s.canyonDepth * canyonAmount * canyonAmount * (3f - 2f * canyonAmount);

            // Trenches: the same idea at a larger scale, with a flat floor and near-vertical walls.
            // Carved later (CarvePassages), once the parts too narrow for the submarine are known.
            float tn = Mathf.Abs(noise.Fbm(qx / s.trenchScale, qz / s.trenchScale, ChTrench, 4));
            trench = Smooth(s.trenchWidth, s.trenchWidth * 0.45f, tn) * wild;
            trenchDepth = s.trenchDepth * (1f - s.trenchFloorVariation * (0.5f + 0.5f * noise.Fbm(x / 90f, z / 90f, ChTrenchFloor, 3)));

            // Crevasses: narrow cracks, warped at a small scale so their edges are jagged, in patches
            float jx = x + noise.Fbm(x / 9f, z / 9f, ChCrevasseWarp, 2) * 4f;
            float jz = z + noise.Fbm(z / 9f, x / 9f, ChCrevasseWarp, 2) * 4f;
            float cn = Mathf.Abs(noise.Fbm(jx / s.crevasseScale, jz / s.crevasseScale, ChCrevasse, 4));
            float crevassePatch = Smooth(1f - s.crevasseCoverage - 0.1f, 1f - s.crevasseCoverage + 0.1f,
                noise.Fbm(x / 160f, z / 160f, ChCrevasseMask, 2) * 0.5f + 0.5f);
            crack = Smooth(s.crevasseWidth, s.crevasseWidth * 0.3f, cn) * crevassePatch * wild;
            crackDepth = s.crevasseDepth * (0.6f + 0.4f * noise.Fbm(x / 20f, z / 20f, ChTrenchFloor, 2));

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
            h += r * s.rockHeight * rockMask * Mathf.Lerp(0.25f, 1f, wild);
            rockAmount = rockMask * Smooth(0.12f, 0.45f, ridge);

            // Asymmetric dunes (gentle stoss side, steep lee side) perpendicular to the current
            float phase = (qx * duneDirection.x + qz * duneDirection.y) / s.duneWavelength
                          + noise.Fbm(qx / 25f, qz / 25f, ChDune, 2) * 1.5f;
            float f = phase - Mathf.Floor(phase);
            float dune = f < 0.75f ? f / 0.75f : (1f - f) / 0.25f;
            dune = dune * dune * (3f - 2f * dune);
            float duneAmp = 0.5f + 0.5f * noise.Fbm(qx / 40f, qz / 40f, ChDuneAmp, 2);
            h += dune * s.duneHeight * duneAmp * (1f - rockMask) * (1f - Mathf.Max(trench, crack));

            h += noise.Fbm(x / s.detailScale, z / s.detailScale, ChDetail, 3) * s.detailHeight;
            return h;
        }

        /// <summary>
        /// Cuts trenches and crevasses. A morphological opening (erode, then dilate) of each mask keeps only the
        /// parts at least <see cref="SeafloorSettings.minPassageWidth"/> wide at full depth; narrower parts stay as
        /// shallow cracks. Walls become bare rock and floors collect silt.
        /// </summary>
        void CarvePassages(float[] trench, float[] trenchDepth, float[] crack, float[] crackDepth, float[] rock, float[] canyon)
        {
            int radius = Mathf.Max(1, Mathf.RoundToInt(settings.minPassageWidth * 0.5f / CellSize));
            var openTrench = Open(trench, radius);
            var openCrack = Open(crack, radius);
            for (int i = 0; i < Heights.Length; i++)
            {
                float deep = Mathf.Max(openTrench[i] * trenchDepth[i], openCrack[i] * crackDepth[i]);
                float shallow = Mathf.Max(trench[i], crack[i]) * settings.narrowCrackDepth;
                Heights[i] -= Mathf.Max(deep, shallow);

                float open = Mathf.Max(openTrench[i], openCrack[i]);
                float wall = Smooth(0.02f, 0.2f, open) * (1f - Smooth(0.85f, 0.99f, open));
                float narrow = Smooth(0.05f, 0.3f, Mathf.Max(trench[i], crack[i]) - open);
                rock[i] = Mathf.Max(rock[i], Mathf.Max(wall, narrow));
                canyon[i] = Mathf.Max(canyon[i], open);
                passages[i] = open;
            }
        }

        /// <summary>Grayscale opening with a square of the given radius (separable min, then max filters).</summary>
        float[] Open(float[] mask, int radius)
        {
            var temp = new float[mask.Length];
            var result = new float[mask.Length];
            Filter(mask, temp, radius, true, true);
            Filter(temp, result, radius, true, false);
            Filter(result, temp, radius, false, true);
            Filter(temp, result, radius, false, false);
            return result;
        }

        /// <summary>1D min (erode) or max (dilate) filter along rows or columns.</summary>
        void Filter(float[] source, float[] target, int radius, bool erode, bool alongX)
        {
            int n = Resolution;
            Parallel.For(0, n, line =>
            {
                for (int k = 0; k < n; k++)
                {
                    float v = erode ? float.MaxValue : float.MinValue;
                    int from = Mathf.Max(0, k - radius), to = Mathf.Min(n - 1, k + radius);
                    for (int j = from; j <= to; j++)
                    {
                        float sample = alongX ? source[line * n + j] : source[j * n + line];
                        v = erode ? Mathf.Min(v, sample) : Mathf.Max(v, sample);
                    }
                    if (alongX)
                        target[line * n + k] = v;
                    else
                        target[k * n + line] = v;
                }
            });
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
            Parallel.For(0, n, z =>
            {
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
            });
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

        /// <summary>How rocky the ground is at a local (x, z) position (0 = sediment, 1 = bare rock).</summary>
        public float RockAt(float x, float z)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt((x + settings.size * 0.5f) / CellSize), 0, Resolution - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt((z + settings.size * 0.5f) / CellSize), 0, Resolution - 1);
            return masks[iz * Resolution + ix].r / 255f;
        }

        /// <summary>Material masks at a local (x, z) position: r = rock, g = silt, b = gravel, a = occlusion.</summary>
        public Color MaskAt(float x, float z) => masks[NearestCell(x, z)];

        /// <summary>How far into a trench or crevasse a local (x, z) position is (0 = outside, 1 = on its floor).</summary>
        public float PassageAt(float x, float z) => passages[NearestCell(x, z)];

        int NearestCell(float x, float z)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt((x + settings.size * 0.5f) / CellSize), 0, Resolution - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt((z + settings.size * 0.5f) / CellSize), 0, Resolution - 1);
            return iz * Resolution + ix;
        }

        /// <summary>
        /// Heaps a lumpy rock mound (e.g. rubble around the foot of an arch) at a local (x, z) position and paints
        /// it rock, fading into the surrounding ground. Call before <see cref="BuildTiles"/>.
        /// </summary>
        public void RaiseMound(Vector2 centre, float radius, float height, int seed)
        {
            int r = Mathf.CeilToInt(radius / CellSize);
            int cx = Mathf.RoundToInt((centre.x + settings.size * 0.5f) / CellSize);
            int cz = Mathf.RoundToInt((centre.y + settings.size * 0.5f) / CellSize);
            for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(Resolution - 1, cz + r); z++)
            for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(Resolution - 1, cx + r); x++)
            {
                float lx = LocalX(x), lz = LocalX(z);
                // Irregular outline and lumpy surface
                float wobble = 1f + 0.3f * noise.Fbm(lx / 6f + seed, lz / 6f, ChRockMask, 2);
                float d = Vector2.Distance(new Vector2(lx, lz), centre) / (radius * wobble);
                if (d >= 1f)
                    continue;
                float falloff = 1f - Smooth(0f, 1f, d);
                float lumps = 0.75f + 0.5f * (noise.Ridged(lx / 4f + seed, lz / 4f, ChRidge, 3));
                int i = z * Resolution + x;
                Heights[i] += height * falloff * falloff * lumps;

                var m = (Color)masks[i];
                float rock = Mathf.Max(m.r, Smooth(0f, 0.6f, falloff));
                float gravelFringe = Smooth(0f, 0.35f, falloff) * (1f - rock);
                masks[i] = new Color(rock, m.g * (1f - rock), Mathf.Max(m.b, gravelFringe), Mathf.Min(m.a, 1f - falloff * 0.15f));
            }
        }

        /// <summary>The seafloor as square tiles (seafloor-local coordinates) so off-screen parts are culled.</summary>
        public Mesh[] BuildTiles()
        {
            int cells = Resolution - 1;
            int tileCells = Mathf.Clamp(settings.tileCells, 8, 250);
            int tilesPerSide = Mathf.CeilToInt(cells / (float)tileCells);
            var tiles = new Mesh[tilesPerSide * tilesPerSide];
            for (int tz = 0; tz < tilesPerSide; tz++)
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                int x0 = tx * tileCells, z0 = tz * tileCells;
                tiles[tz * tilesPerSide + tx] = BuildTile(x0, z0, Mathf.Min(x0 + tileCells, cells), Mathf.Min(z0 + tileCells, cells));
            }
            return tiles;
        }

        Mesh BuildTile(int x0, int z0, int x1, int z1)
        {
            int w = x1 - x0 + 1, d = z1 - z0 + 1;
            var vertices = new Vector3[w * d];
            var normals = new Vector3[w * d];
            var colors = new Color32[w * d];
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * Resolution + x;
                int j = (z - z0) * w + (x - x0);
                vertices[j] = new Vector3(LocalX(x), Heights[i], LocalX(z));
                float dx = H(x + 1, z) - H(x - 1, z);
                float dz = H(x, z + 1) - H(x, z - 1);
                normals[j] = new Vector3(-dx, 2f * CellSize, -dz).normalized;
                colors[j] = masks[i];
            }

            var triangles = new int[(w - 1) * (d - 1) * 6];
            int t = 0;
            for (int z = 0; z < d - 1; z++)
            for (int x = 0; x < w - 1; x++)
            {
                int i = z * w + x;
                triangles[t++] = i;
                triangles[t++] = i + w;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + w;
                triangles[t++] = i + w + 1;
            }

            var mesh = new Mesh { name = $"Seafloor_{x0}_{z0}" };
            mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Seeded fBm / ridged noise on top of a managed 2D gradient (Perlin) noise, with independent offsets per
        /// channel and octave. Managed (no engine calls) so rows can be generated on worker threads.
        /// </summary>
        class Noise
        {
            const int MaxOctaves = 8;
            const int Channels = 16;
            readonly Vector2[] offsets = new Vector2[Channels * MaxOctaves];
            readonly int[] perm = new int[512];

            public Noise(System.Random rng)
            {
                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = new Vector2((float)rng.NextDouble() * 2000f - 1000f, (float)rng.NextDouble() * 2000f - 1000f);
                for (int i = 0; i < 256; i++)
                    perm[i] = i;
                for (int i = 255; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (perm[i], perm[j]) = (perm[j], perm[i]);
                }
                for (int i = 0; i < 256; i++)
                    perm[256 + i] = perm[i];
            }

            float Perlin(float x, float z, int channel, int octave)
            {
                var o = offsets[channel * MaxOctaves + octave];
                return Gradient(x + o.x, z + o.y);
            }

            /// <summary>Improved Perlin noise in 2D, roughly in [-1, 1].</summary>
            float Gradient(float x, float y)
            {
                float fx = Mathf.Floor(x), fy = Mathf.Floor(y);
                int xi = (int)fx & 255, yi = (int)fy & 255;
                x -= fx;
                y -= fy;
                float u = x * x * x * (x * (x * 6f - 15f) + 10f);
                float v = y * y * y * (y * (y * 6f - 15f) + 10f);
                int aa = perm[perm[xi] + yi], ab = perm[perm[xi] + yi + 1];
                int ba = perm[perm[xi + 1] + yi], bb = perm[perm[xi + 1] + yi + 1];
                float x1 = Lerp(Grad(aa, x, y), Grad(ba, x - 1f, y), u);
                float x2 = Lerp(Grad(ab, x, y - 1f), Grad(bb, x - 1f, y - 1f), u);
                return Lerp(x1, x2, v);
            }

            static float Lerp(float a, float b, float t) => a + (b - a) * t;

            static float Grad(int hash, float x, float y)
            {
                switch (hash & 7)
                {
                    case 0: return x + y;
                    case 1: return -x + y;
                    case 2: return x - y;
                    case 3: return -x - y;
                    case 4: return x;
                    case 5: return -x;
                    case 6: return y;
                    default: return -y;
                }
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
