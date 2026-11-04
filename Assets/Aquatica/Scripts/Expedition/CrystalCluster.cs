using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Builds a cluster of crystal shards growing out of a wall: hexagonal prisms with pointed tips, their bases
    /// buried behind the surface (local z below 0), fanning out along +z and leaning upwards. Flat-shaded so every
    /// facet catches the light on its own. About one unit across.
    /// </summary>
    public static class CrystalCluster
    {
        const int Sides = 6;

        public static Mesh Build(int seed, int shards = 9)
        {
            var rng = new System.Random(seed);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int s = 0; s < shards; s++)
            {
                // The first shard is the big one in the middle, the others smaller around it
                bool main = s == 0;
                float length = main ? Range(rng, 0.85f, 1f) : Range(rng, 0.35f, 0.75f);
                float radius = length * Range(rng, 0.17f, 0.24f);
                var spread = main ? new Vector2(Range(rng, -0.15f, 0.15f), Range(rng, 0.1f, 0.3f))
                                  : new Vector2(Range(rng, -0.75f, 0.75f), Range(rng, -0.35f, 0.75f));
                var axis = new Vector3(spread.x, spread.y, 1f).normalized;
                var basePoint = main ? Vector3.zero : new Vector3(Range(rng, -0.18f, 0.18f), Range(rng, -0.14f, 0.14f), 0f);
                basePoint -= axis * 0.2f; // buried in the stone
                AddShard(vertices, triangles, basePoint, axis, length, radius, Range(rng, 0f, 60f));
            }

            var mesh = new Mesh { name = $"CrystalCluster_{seed}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); // no shared vertices: flat facets
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddShard(List<Vector3> v, List<int> t, Vector3 basePoint, Vector3 axis, float length, float radius, float twist)
        {
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, axis);
            float shoulder = length * 0.72f;
            var ring0 = new Vector3[Sides];
            var ring1 = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = (i * 360f / Sides + twist) * Mathf.Deg2Rad;
                var offset = (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
                ring0[i] = basePoint + offset;
                ring1[i] = basePoint + axis * shoulder + offset * 0.92f;
            }
            var tip = basePoint + axis * length;

            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                // Side facet (outward winding)
                Quad(v, t, ring0[i], ring0[j], ring1[j], ring1[i]);
                // Point facet
                int k = v.Count;
                v.Add(ring1[i]); v.Add(ring1[j]); v.Add(tip);
                t.Add(k); t.Add(k + 2); t.Add(k + 1);
            }
        }

        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int k = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(k); t.Add(k + 2); t.Add(k + 1);
            t.Add(k); t.Add(k + 3); t.Add(k + 2);
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
