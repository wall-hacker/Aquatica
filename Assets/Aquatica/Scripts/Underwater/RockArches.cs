using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Underwater
{
    [Serializable]
    public class RockArchSettings
    {
        public int count = 6;
        [Tooltip("Distance between the two feet (m).")]
        public Vector2 spanRange = new(22f, 32f);
        [Tooltip("Free height under the apex, above the highest ground beneath the arch (m).")]
        public Vector2 clearanceRange = new(11f, 15f);
        [Tooltip("Radius of the rock band at the apex (m); the feet are about 1.4x thicker.")]
        public Vector2 thicknessRange = new(2.2f, 3.6f);
        [Tooltip("Keep this far from the start (m).")]
        public float minDistanceFromStart = 55f;
        [Tooltip("Minimum distance between arches (m).")]
        public float minSpacing = 55f;
        [Tooltip("How far the feet are buried into the ground (m).")]
        public float footSink = 6f;
        [Tooltip("Height difference under a foot (m) above which a placement is penalised (e.g. a foot on a crevasse edge).")]
        public float maxFootStep = 4f;
        [Tooltip("Share of placement candidates seeded inside a trench or crevasse, sized to span it from rim to rim.")]
        [Range(0f, 1f)] public float ravineBias = 0.8f;
        [Tooltip("Widest ravine crossing (foot to foot, m); arches over ravines may be longer than spanRange.")]
        public float maxRavineSpan = 70f;
        [Tooltip("How many placements to try per arch; the one bridging the deepest dip wins.")]
        public int candidates = 40;
        [Tooltip("Rubble mound heaped around each foot so it grows out of the ground (height relative to the foot radius).")]
        public float moundHeight = 0.9f;
        public float moundRadius = 2.6f;
    }

    /// <summary>
    /// Free-standing rock arches. Each is a lumpy band swept along an arc between two buried feet, picked from
    /// several candidate spots so arches tend to bridge gullies, crevasses and trenches between rocky ground.
    /// Each foot grows out of a rubble mound heaped into the seafloor, and the upper surfaces carry a dusting of
    /// sediment, so they blend in with the terrain (same Aquatica/Seafloor material and masks). The apex is
    /// raised so the submarine fits underneath. Colliders are convex chunks along the arc.
    /// Generate before building the seafloor mesh, since it reshapes the ground. Coordinates are local to the
    /// seafloor object.
    /// </summary>
    public static class RockArches
    {
        const int Rings = 64;
        const int Sides = 20;
        const int ChunkRings = 6;
        const float LongSpanThickening = 1.2f;

        public struct Arch
        {
            public Mesh Mesh;
            public Mesh[] ColliderChunks;
        }

        public static List<Arch> Generate(Seafloor floor, RockArchSettings settings, int seed)
        {
            var rng = new System.Random(seed ^ 0x5A3C);
            var arches = new List<Arch>();
            var placed = new List<Vector2>();
            float limit = floor.Size * 0.5f - settings.spanRange.y * 0.5f - 10f;

            for (int a = 0; a < settings.count; a++)
            {
                float bestScore = float.MinValue;
                Vector2 bestA = default, bestB = default;
                bool found = false;
                float span = Range(rng, settings.spanRange);

                for (int c = 0; c < settings.candidates; c++)
                {
                    // Most candidates start on a ravine floor (trench or crevasse) and are sized to cross it; the
                    // rest are anywhere, so arches can still stand over gentler ground when no ravine fits
                    bool ravine = rng.NextDouble() < settings.ravineBias;
                    var centre = ravine ? RandomRavinePoint(floor, rng, limit) : RandomPoint(rng, limit);
                    float angle0 = Range(rng, 0f, Mathf.PI);
                    // A ravine spot tries several headings so one can cross it rather than run along it
                    int headings = ravine ? 6 : 1;
                    for (int h = 0; h < headings; h++)
                    {
                        float angle = angle0 + h * Mathf.PI / headings;
                        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Vector2 footA, footB;
                        if (ravine)
                        {
                            if (!CrossRavine(floor, settings, centre, dir, out footA, out footB))
                                continue;
                        }
                        else
                        {
                            footA = centre - dir * (span * 0.5f);
                            footB = centre + dir * (span * 0.5f);
                        }

                        var mid = (footA + footB) * 0.5f;
                        if (mid.magnitude < settings.minDistanceFromStart + Vector2.Distance(footA, footB) * 0.5f)
                            continue;
                        if (Mathf.Max(Mathf.Abs(footA.x), Mathf.Abs(footA.y), Mathf.Abs(footB.x), Mathf.Abs(footB.y)) > floor.Size * 0.5f - 10f)
                            continue;
                        bool crowded = false;
                        foreach (var p in placed)
                            crowded |= Vector2.Distance(p, mid) < settings.minSpacing;
                        if (crowded)
                            continue;

                        float score = Score(floor, settings, footA, footB) + (float)rng.NextDouble() * 3f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestA = footA;
                            bestB = footB;
                            found = true;
                        }
                    }
                }

                if (!found)
                    continue;
                placed.Add((bestA + bestB) * 0.5f);
                arches.Add(Build(floor, settings, rng, bestA, bestB));
            }
            return arches;
        }

        /// <summary>
        /// How good a pair of feet is: the deeper the gap under the span (especially a trench or crevasse), the
        /// better; feet should be on solid, fairly even ground outside the ravine, at similar heights.
        /// </summary>
        static float Score(Seafloor floor, RockArchSettings settings, Vector2 footA, Vector2 footB)
        {
            float ha = floor.SampleLocal(footA.x, footA.y), hb = floor.SampleLocal(footB.x, footB.y);
            float lowest = float.MaxValue, passage = 0f;
            for (int i = 3; i <= 13; i++)
            {
                var p = Vector2.Lerp(footA, footB, i / 16f);
                lowest = Mathf.Min(lowest, floor.SampleLocal(p.x, p.y));
                passage = Mathf.Max(passage, floor.PassageAt(p.x, p.y));
            }
            float dip = Mathf.Clamp(Mathf.Min(ha, hb) - lowest, -10f, 25f);
            float feetInRavine = floor.PassageAt(footA.x, footA.y) + floor.PassageAt(footB.x, footB.y);
            float rocky = floor.RockAt(footA.x, footA.y) + floor.RockAt(footB.x, footB.y);

            // A foot right on a ravine edge is fine (it is sunk below its lowest ground), but a lopsided one is not
            float footSize = settings.thicknessRange.y * 1.4f;
            var axis = (footB - footA).normalized;
            FootprintRange(floor, footA, axis, footSize, out float minA, out float maxA);
            FootprintRange(floor, footB, axis, footSize, out float minB, out float maxB);
            float uneven = Mathf.Max(0f, maxA - minA - settings.maxFootStep) + Mathf.Max(0f, maxB - minB - settings.maxFootStep);

            return dip * 1.5f + passage * 25f - feetInRavine * 12f - Mathf.Abs(ha - hb) * 0.5f + rocky * 4f - uneven * 1.2f;
        }

        /// <summary>
        /// From a point inside a ravine, walks both ways along <paramref name="dir"/> to its rims and puts a foot
        /// just beyond each, so the foot stands on the rim rather than in the ravine. Fails if the crossing is
        /// wider than <see cref="RockArchSettings.maxRavineSpan"/>.
        /// </summary>
        static bool CrossRavine(Seafloor floor, RockArchSettings settings, Vector2 centre, Vector2 dir, out Vector2 footA, out Vector2 footB)
        {
            footA = footB = default;
            // The foot's whole footprint (see FootprintRange) stays on the rim
            float margin = settings.thicknessRange.y * 1.4f * LongSpanThickening * 1.6f;
            float reach = settings.maxRavineSpan - margin * 2f;
            float toA = RimDistance(floor, centre, -dir, reach), toB = RimDistance(floor, centre, dir, reach);
            if (toA < 0f || toB < 0f || toA + toB > reach)
                return false;
            // Narrow cracks still get a full-sized arch, centred on the crack
            float extra = Mathf.Max(0f, settings.spanRange.x - (toA + toB + margin * 2f)) * 0.5f;
            footA = centre - dir * (toA + margin + extra);
            footB = centre + dir * (toB + margin + extra);
            return true;
        }

        /// <summary>Distance to where the ravine ends along a direction, or -1 if it does not within <paramref name="reach"/>.</summary>
        static float RimDistance(Seafloor floor, Vector2 from, Vector2 dir, float reach)
        {
            for (float d = 0f; d <= reach; d += 1f)
            {
                var p = from + dir * d;
                if (floor.PassageAt(p.x, p.y) < 0.1f)
                    return d;
            }
            return -1f;
        }

        static Vector2 RandomPoint(System.Random rng, float limit) => new(Range(rng, -limit, limit), Range(rng, -limit, limit));

        /// <summary>A random point inside a trench or crevasse, or anywhere if none turns up.</summary>
        static Vector2 RandomRavinePoint(Seafloor floor, System.Random rng, float limit)
        {
            var p = RandomPoint(rng, limit);
            for (int i = 0; i < 40 && floor.PassageAt(p.x, p.y) < 0.5f; i++)
                p = RandomPoint(rng, limit);
            return p;
        }

        static Arch Build(Seafloor floor, RockArchSettings settings, System.Random rng, Vector2 footA, Vector2 footB)
        {
            // Long ravine crossings get a sturdier band
            float thickness = Range(rng, settings.thicknessRange) * Mathf.Clamp(Vector2.Distance(footA, footB) / 36f, 1f, LongSpanThickening);
            float clearance = Range(rng, settings.clearanceRange);
            float footRadius = thickness * 1.4f;
            float noiseSeed = (float)rng.NextDouble() * 500f;

            // The feet grow out of rubble mounds
            int moundSeed = rng.Next(1000);
            floor.RaiseMound(footA, footRadius * settings.moundRadius, footRadius * settings.moundHeight, moundSeed);
            floor.RaiseMound(footB, footRadius * settings.moundRadius, footRadius * settings.moundHeight, moundSeed + 17);

            // Sink each foot below the lowest ground under its whole footprint, not just its centre, so no part of
            // it is left hanging where the ground falls away beside it
            var spanAxis = (footB - footA).normalized;
            FootprintRange(floor, footA, spanAxis, footRadius, out float lowA, out _);
            FootprintRange(floor, footB, spanAxis, footRadius, out float lowB, out _);
            float ha = Mathf.Min(floor.SampleLocal(footA.x, footA.y) - settings.footSink, lowA - 1f);
            float hb = Mathf.Min(floor.SampleLocal(footB.x, footB.y) - settings.footSink, lowB - 1f);
            float highest = float.MinValue;
            for (int i = 1; i < 16; i++)
            {
                var p = Vector2.Lerp(footA, footB, i / 16f);
                highest = Mathf.Max(highest, floor.SampleLocal(p.x, p.y));
            }
            float apex = highest + clearance + thickness;
            float baseMid = (ha + hb) * 0.5f;

            var spanDir = (footB - footA).normalized;
            var side = new Vector3(-spanDir.y, 0f, spanDir.x); // normal of the arch's plane

            Vector3 PathPoint(float t)
            {
                var xz = Vector2.Lerp(footA, footB, t);
                float lift = Lift(t);
                float y = Mathf.Lerp(ha, hb, t) + (apex - baseMid) * lift;
                return new Vector3(xz.x, y, xz.y);
            }

            var vertices = new Vector3[Rings * Sides];
            var colors = new Color[Rings * Sides];
            for (int r = 0; r < Rings; r++)
            {
                float t = r / (Rings - 1f);
                var p = PathPoint(t);
                var tangent = (PathPoint(Mathf.Min(1f, t + 0.01f)) - PathPoint(Mathf.Max(0f, t - 0.01f))).normalized;
                // In the arch plane, perpendicular to the path: up at the apex, inwards along the legs
                var up = Vector3.Cross(side, tangent).normalized;

                float lift = Lift(t);
                float radius = Mathf.Lerp(footRadius, thickness, lift);
                // A little sag in thickness along the span keeps it from looking like a pipe
                radius *= 1f + 0.18f * Mathf.Sin(t * 9.3f + noiseSeed);

                for (int s = 0; s < Sides; s++)
                {
                    float phi = s * Mathf.PI * 2f / Sides;
                    // Wider across the arch's plane than within it: a band, not a round pipe
                    var dir = side * Mathf.Cos(phi) * 1.35f + up * Mathf.Sin(phi);
                    var surface = p + dir * radius;
                    var n = dir.normalized;

                    float lumps = ValueNoise3(surface * 0.18f + Vector3.one * noiseSeed) - 0.5f;
                    float detail = ValueNoise3(surface * 0.6f + Vector3.one * (noiseSeed + 31f)) - 0.5f;
                    float strata = Mathf.Sin(surface.y * 1.7f + lumps * 3f) * 0.25f;
                    surface += n * (lumps * radius * 0.55f + detail * 0.7f + strata);

                    int i = r * Sides + s;
                    vertices[i] = surface;
                    // Rock with darker undersides; sediment settles on the upper surfaces
                    float under = Mathf.Clamp01(n.y * 0.5f + 0.5f);
                    float sediment = Mathf.Clamp01((n.y - 0.55f) / 0.35f) * (0.6f + 0.4f * detail * 2f);
                    sediment = Mathf.Clamp01(sediment);
                    colors[i] = new Color(1f - sediment * 0.75f, sediment * 0.6f, 0.15f + sediment * 0.3f, Mathf.Lerp(0.72f, 1f, under));
                }
            }

            var triangles = new List<int>((Rings - 1) * Sides * 6);
            AddBand(triangles, 0, Rings - 1);

            var mesh = new Mesh { name = "RockArch" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Convex chunks for collision: a few rings each, with every other side, so hulls stay small
            var chunks = new List<Mesh>();
            for (int r0 = 0; r0 < Rings - 1; r0 += ChunkRings)
            {
                int r1 = Mathf.Min(Rings - 1, r0 + ChunkRings);
                var chunkVertices = new List<Vector3>();
                foreach (int r in new[] { r0, (r0 + r1) / 2, r1 })
                    for (int s = 0; s < Sides; s += 2)
                        chunkVertices.Add(vertices[r * Sides + s]);
                var chunk = new Mesh { name = "RockArchCollider" };
                chunk.SetVertices(chunkVertices);
                // Convex cooking only needs the points; a single dummy triangle keeps the mesh valid
                chunk.SetTriangles(new[] { 0, 1, 2 }, 0);
                chunks.Add(chunk);
            }

            return new Arch { Mesh = mesh, ColliderChunks = chunks.ToArray() };
        }

        /// <summary>
        /// Lowest and highest ground under a foot: an ellipse covering its band (wider across the arch's plane than
        /// along the span), including the lumps added to the surface.
        /// </summary>
        static void FootprintRange(Seafloor floor, Vector2 foot, Vector2 spanAxis, float footRadius, out float min, out float max)
        {
            var across = new Vector2(-spanAxis.y, spanAxis.x);
            float acrossRadius = footRadius * 1.35f * 1.6f, alongRadius = footRadius * 1.6f;
            min = max = floor.SampleLocal(foot.x, foot.y);
            for (int ring = 1; ring <= 2; ring++)
            for (int k = 0; k < 12; k++)
            {
                float phi = k * Mathf.PI * 2f / 12f, f = ring / 2f;
                var p = foot + across * (Mathf.Cos(phi) * acrossRadius * f) + spanAxis * (Mathf.Sin(phi) * alongRadius * f);
                float h = floor.SampleLocal(p.x, p.y);
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
        }

        /// <summary>Quads between consecutive rings, outward-facing (rings wrap around, so there is no seam).</summary>
        static void AddBand(List<int> triangles, int firstRing, int lastRing)
        {
            for (int r = firstRing; r < lastRing; r++)
            for (int s = 0; s < Sides; s++)
            {
                int s1 = (s + 1) % Sides;
                int a = r * Sides + s, b = r * Sides + s1, c = (r + 1) * Sides + s, d = (r + 1) * Sides + s1;
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        /// <summary>Arc profile: 0 at the feet, 1 at the apex, with steep legs and a rounded top.</summary>
        static float Lift(float t) => Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t))), 0.7f);

        static float Range(System.Random rng, Vector2 range) => Range(rng, range.x, range.y);

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Smooth 3D value noise in [0, 1].</summary>
        static float ValueNoise3(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float Corner(int dx, int dy, int dz) => Hash(x0 + dx, y0 + dy, z0 + dz);
            float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx);
            float x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
            float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx);
            float x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
