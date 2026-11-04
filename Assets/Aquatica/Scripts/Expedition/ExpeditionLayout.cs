using System;
using System.Collections.Generic;
using Aquatica.Underwater;
using UnityEngine;

namespace Aquatica.Expedition
{
    [Serializable]
    public class ExpeditionLayoutSettings
    {
        [Tooltip("Crates to bury.")]
        public int crateCount = 10;
        [Tooltip("Crates per distance band from the start (near to far); the remainder goes to the last band.")]
        public int[] cratesPerBand = { 2, 4, 4 };
        [Tooltip("Distance bands (m from the start): x = min, y = max. Matches cratesPerBand.")]
        public Vector2[] bands = { new(28f, 55f), new(55f, 100f), new(95f, 150f) };
        [Tooltip("Gap (m) kept inside the navigable radius, clear of the edge currents.")]
        public float edgeMargin = 35f;
        [Tooltip("Minimum distance (m) between crates.")]
        public float minSpacing = 35f;
        [Tooltip("Placements tried for the gateway; the best valid one wins.")]
        public int candidates = 48;
        [Tooltip("Spacing (m) of the grid of possible crate spots scanned over the area.")]
        public float scanStep = 3f;

        [Header("Elevation")]
        [Tooltip("How many crates lie low (trenches, hollows), at middle height, and high up (ridges, plateaus).")]
        public Vector3Int elevationTiers = new(3, 4, 3);
        [Tooltip("Spots below this share of the possible spots' heights count as low, above the second as high. " +
                 "Where there are trench floors to lie on, only those count as low.")]
        public Vector2 tierQuantiles = new(0.1f, 0.9f);
        [Tooltip("Within its tier, how strongly a crate prefers the more extreme heights (deepest / highest).")]
        public float tierPull = 3f;

        [Header("Ground")]
        [Tooltip("Steepest ground (degrees) a crate is buried in.")]
        public float maxSlope = 22f;
        [Tooltip("Not on the barest rock (rock-mask value); trench floors and ridges are fairly rocky.")]
        [Range(0f, 1f)] public float maxRock = 0.7f;
        [Tooltip("Room for the magnet to swing down: no ground higher than the crate's base by more than y metres within x metres.")]
        public Vector2 magnetClearance = new(3.5f, 2f);
        [Tooltip("Room for the submarine to hover over it: no ground higher than the crate's base by more than y metres " +
                 "within x metres (trench walls may rise around it, as long as the hull fits between them).")]
        public Vector2 hoverClearance = new(8f, 12f);
        [Tooltip("Radius (m) and height (m) of the column above a crate that must be free of rock arches.")]
        public Vector2 archClearance = new(6f, 36f);

        [Header("Burial")]
        [Tooltip("Share of the crate's height sunk into the ground.")]
        public Vector2 burialRange = new(0.25f, 0.65f);
        [Tooltip("Random lean (degrees) on top of the ground's slope.")]
        public float tiltNoise = 7f;
        [Tooltip("Uniform scale range of the crate model.")]
        public Vector2 scaleRange = new(1f, 1.25f);
        [Tooltip("The lid's corners stay at least this far (m) above the ground.")]
        public float lidMargin = 0.1f;
        [Tooltip("The base's corners may float at most this far (m) above the ground.")]
        public float floatTolerance = 0.12f;

        [Header("Gateway")]
        [Tooltip("Distance band (m from the start) of the exit gateway.")]
        public Vector2 gateBand = new(115f, 150f);
        [Tooltip("Ground kept free of crates (m) around the gateway.")]
        public float gateClearRadius = 30f;
        [Tooltip("Flat-ish ground needed (m): x = radius, y = largest height difference within it.")]
        public Vector2 gateFlatness = new(10f, 4f);
    }

    /// <summary>A crate's pose in seafloor-local space.</summary>
    public struct CrateSite
    {
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
        /// <summary>Share of the crate's height below the ground.</summary>
        public float burial;
        public int band;
        /// <summary>0 = low, 1 = middle, 2 = high.</summary>
        public int tier;
        /// <summary>Height of the middle of the lid (seafloor-local).</summary>
        public float lidY;
    }

    /// <summary>
    /// Where the expedition's crates lie buried and where the exit gateway stands, the same way every time for a
    /// given world seed. Crates are spread around the start in distance bands (a couple close by, the rest further
    /// out, one per compass sector) and over the terrain's heights: some down in trenches and hollows, some on
    /// ridges and plateaus, the rest in between. Each lies half buried in fairly flat ground with room for the magnet
    /// to swing down and the submarine to hover, never under an arch, near another crate or out in the edge currents.
    /// All positions are in seafloor-local space.
    /// </summary>
    public class ExpeditionLayout
    {
        public readonly List<CrateSite> Crates = new();
        public Vector3 GatePosition;
        /// <summary>The gateway faces the start along this rotation's forward.</summary>
        public Quaternion GateRotation;
        public bool HasGate;

        Seafloor floor;
        Transform floorTransform;
        Vector3 worldCentre;
        float areaRadius;
        int terrainMask;
        ExpeditionLayoutSettings settings;
        Bounds crateBounds;
        float maxLidY;
        Vector2 lowRange, highRange; // heights spanned by the low and the high spots (deepest..shallowest, lowest..highest)

        /// <summary>
        /// crateBounds: the crate model's bounds in its own space (unscaled). start: the submarine's start in
        /// seafloor-local space. maxLidY: highest a lid may be (seafloor-local) so the submarine can still hover far
        /// enough above it for the magnet without leaving the navigable water.
        /// </summary>
        public static ExpeditionLayout Generate(UnderwaterWorld world, float navigableRadius, Vector3 start,
            Bounds crateBounds, ExpeditionLayoutSettings settings, int seed, float maxLidY = float.MaxValue)
        {
            Physics.SyncTransforms(); // the arch colliders were only just created
            var layout = new ExpeditionLayout
            {
                floor = world.Seafloor,
                floorTransform = world.SeafloorTransform,
                worldCentre = world.transform.position,
                areaRadius = navigableRadius - settings.edgeMargin,
                terrainMask = LayerMask.GetMask(UnderwaterWorld.TerrainLayerName),
                settings = settings,
                crateBounds = crateBounds,
                maxLidY = maxLidY,
            };
            var rng = new System.Random(seed ^ 0x3C7A7E);
            var start2 = new Vector2(start.x, start.z);
            layout.PlaceGate(rng, start2);
            layout.PlaceCrates(rng, start2);
            return layout;
        }

        void PlaceGate(System.Random rng, Vector2 start)
        {
            float best = float.MaxValue;
            for (int c = 0; c < settings.candidates * 2; c++)
            {
                var p = start + Polar(Range(rng, 0f, Mathf.PI * 2f), Range(rng, settings.gateBand));
                if (!Navigable(p, settings.gateFlatness.x + 6f) || ArchAbove(p, settings.gateFlatness.x + 4f, 30f))
                    continue;
                float ground = floor.SampleLocal(p.x, p.y);
                float spread = HeightSpread(p, settings.gateFlatness.x, ground, out _);
                if (spread > settings.gateFlatness.y)
                    continue;
                float score = spread + floor.RockAt(p.x, p.y) + (float)rng.NextDouble();
                if (score >= best)
                    continue;
                best = score;
                GatePosition = new Vector3(p.x, ground, p.y);
                var toStart = start - p;
                GateRotation = Quaternion.LookRotation(new Vector3(toStart.x, 0f, toStart.y));
                HasGate = true;
            }
            if (!HasGate)
                Debug.LogWarning("[Aquatica] Expedition: no site found for the gateway");
        }

        /// <summary>A spot a crate could lie in, from the scan.</summary>
        struct Spot
        {
            public Vector2 p;
            public float height, slope, rock;
            public int tier;
        }

        void PlaceCrates(System.Random rng, Vector2 start)
        {
            var spots = ScanSpots(start);
            if (spots.Count == 0)
            {
                Debug.LogWarning("[Aquatica] Expedition: no ground found for crates");
                return;
            }

            // Which band and which height tier each crate gets, then one compass sector each, all shuffled
            var bands = new List<int>();
            for (int b = 0; b < settings.bands.Length && bands.Count < settings.crateCount; b++)
                for (int k = 0; k < settings.cratesPerBand[Mathf.Min(b, settings.cratesPerBand.Length - 1)] && bands.Count < settings.crateCount; k++)
                    bands.Add(b);
            while (bands.Count < settings.crateCount)
                bands.Add(settings.bands.Length - 1);
            int count = bands.Count;

            var tiers = new List<int>();
            for (int t = 0; t < 3; t++)
                for (int k = 0; k < settings.elevationTiers[t] && tiers.Count < count; k++)
                    tiers.Add(t);
            while (tiers.Count < count)
                tiers.Add(1);
            Shuffle(rng, tiers);

            var sectors = new List<int>();
            for (int i = 0; i < count; i++)
                sectors.Add(i);
            Shuffle(rng, sectors);

            float sectorAngle = Mathf.PI * 2f / count;
            float sectorOffset = Range(rng, 0f, sectorAngle);
            var everywhere = new Vector2(settings.bands[0].x, areaRadius);
            const float all = Mathf.PI * 2f;

            // Trenches and ridges are scarce and only in some directions: low and high crates go first and search
            // their whole band for the deepest / highest ground, as far from the others as they can. The rest fill
            // in one compass sector each.
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                int tier = tiers[i];
                bool extreme = tier != 1;
                if (extreme != (pass == 0))
                    continue;
                var band = settings.bands[bands[i]];
                float a0 = sectorOffset + sectors[i] * sectorAngle;
                // Relax step by step rather than leave a crate out
                bool placed = extreme
                    ? TryPlaceCrate(rng, spots, start, 0f, all, band, tier, bands[i], true) ||
                      TryPlaceCrate(rng, spots, start, 0f, all, everywhere, tier, bands[i], true)
                    : TryPlaceCrate(rng, spots, start, a0, sectorAngle, band, tier, bands[i], false) ||
                      TryPlaceCrate(rng, spots, start, 0f, all, band, tier, bands[i], true) ||
                      TryPlaceCrate(rng, spots, start, 0f, all, everywhere, tier, bands[i], true);
                placed = placed ||
                         TryPlaceCrate(rng, spots, start, 0f, all, band, -1, bands[i], true) ||
                         TryPlaceCrate(rng, spots, start, 0f, all, everywhere, -1, bands[i], true);
                if (!placed)
                    Debug.LogWarning($"[Aquatica] Expedition: no site found for crate {i}");
            }
        }

        /// <summary>
        /// Every spot on a grid over the area where a crate could lie: fairly flat, not bare rock, with room around it
        /// for the magnet and the hull, each sorted into low / middle / high by its height among them.
        /// </summary>
        List<Spot> ScanSpots(Vector2 start)
        {
            var spots = new List<Spot>();
            float step = Mathf.Max(1f, settings.scanStep);
            float reach = areaRadius;
            for (float x = -reach; x <= reach; x += step)
            for (float z = -reach; z <= reach; z += step)
            {
                var p = start + new Vector2(x, z);
                if (new Vector2(x, z).magnitude < settings.bands[0].x || !Navigable(p, 0f))
                    continue;
                float rock = floor.RockAt(p.x, p.y);
                if (rock > settings.maxRock)
                    continue;
                float slope = Vector3.Angle(Vector3.up, Normal(p));
                if (slope > settings.maxSlope)
                    continue;
                float ground = floor.SampleLocal(p.x, p.y);
                HeightSpread(p, settings.magnetClearance.x, ground, out float near);
                if (near - ground > settings.magnetClearance.y)
                    continue;
                HeightSpread(p, settings.hoverClearance.x, ground, out float around);
                if (around - ground > settings.hoverClearance.y)
                    continue;
                spots.Add(new Spot { p = p, height = ground, slope = slope, rock = rock });
            }

            if (spots.Count > 0)
            {
                var heights = new List<float>(spots.Count);
                foreach (var spot in spots)
                    heights.Add(spot.height);
                heights.Sort();
                float lowHeight = heights[Mathf.Clamp(Mathf.FloorToInt(settings.tierQuantiles.x * heights.Count), 0, heights.Count - 1)];
                float highHeight = heights[Mathf.Clamp(Mathf.FloorToInt(settings.tierQuantiles.y * heights.Count), 0, heights.Count - 1)];
                bool anyTrench = false;
                foreach (var spot in spots)
                    anyTrench |= floor.PassageAt(spot.p.x, spot.p.y) > 0.5f;
                for (int i = 0; i < spots.Count; i++)
                {
                    var spot = spots[i];
                    bool low = anyTrench ? floor.PassageAt(spot.p.x, spot.p.y) > 0.5f : spot.height <= lowHeight;
                    spot.tier = low ? 0 : spot.height >= highHeight ? 2 : 1;
                    spots[i] = spot;
                }
                // Each extreme tier is pulled towards its own end of its own height range
                lowRange = new Vector2(float.MaxValue, float.MinValue);
                highRange = new Vector2(float.MaxValue, float.MinValue);
                foreach (var spot in spots)
                {
                    if (spot.tier == 0)
                        lowRange = new Vector2(Mathf.Min(lowRange.x, spot.height), Mathf.Max(lowRange.y, spot.height));
                    else if (spot.tier == 2)
                        highRange = new Vector2(Mathf.Min(highRange.x, spot.height), Mathf.Max(highRange.y, spot.height));
                }
            }
            return spots;
        }

        /// <summary>
        /// Picks the best spot within a band and an angle range (and of a height tier, unless -1) and buries a crate
        /// there. spread: prefer spots far from the crates placed so far (for fallbacks, which can land anywhere).
        /// </summary>
        bool TryPlaceCrate(System.Random rng, List<Spot> spots, Vector2 start, float angleMin, float angleSpan, Vector2 band,
            int tier, int bandIndex, bool spread)
        {
            var ranked = new List<(float score, int index)>();
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (tier >= 0 && spot.tier != tier)
                    continue;
                var offset = spot.p - start;
                float r = offset.magnitude;
                if (r < band.x || r > band.y)
                    continue;
                float angle = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) - angleMin, Mathf.PI * 2f);
                if (angle > angleSpan || Crowded(spot.p))
                    continue;

                float score = spot.slope / settings.maxSlope + spot.rock + (float)rng.NextDouble() * 1.5f;
                // Low crates go as deep as they can, high ones as high as they can
                if (spot.tier == 0)
                    score += (spot.height - lowRange.x) / Mathf.Max(1f, lowRange.y - lowRange.x) * settings.tierPull; // deeper is better
                else if (spot.tier == 2)
                    score += (highRange.y - spot.height) / Mathf.Max(1f, highRange.y - highRange.x) * settings.tierPull; // higher is better
                if (spread)
                    score -= Mathf.Min(NearestCrate(spot.p), 120f) / 20f;
                ranked.Add((score, i));
            }
            ranked.Sort((a, b) => a.score.CompareTo(b.score));

            // The best few that pass the remaining (costlier) checks
            int tries = Mathf.Min(ranked.Count, 40);
            for (int k = 0; k < tries; k++)
            {
                var spot = spots[ranked[k].index];
                var p = spot.p + new Vector2(Range(rng, -0.4f, 0.4f), Range(rng, -0.4f, 0.4f)) * settings.scanStep;
                if (!Navigable(p, 0f) || Crowded(p) || ArchAbove(p, settings.archClearance.x, settings.archClearance.y))
                    continue;
                if (!Bury(rng, p, Normal(p), out var site) || site.lidY > maxLidY)
                    continue;
                site.band = bandIndex;
                site.tier = spot.tier;
                Crates.Add(site);
                return true;
            }
            return false;
        }

        static void Shuffle<T>(System.Random rng, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Sinks the crate into the ground, leaning with the slope, by a random share of its height, then checks the
        /// lid stays above ground (less sunk if not) and the base doesn't float.
        /// </summary>
        bool Bury(System.Random rng, Vector2 p, Vector3 normal, out CrateSite site)
        {
            float scale = Range(rng, settings.scaleRange);
            var up = Quaternion.Euler(Range(rng, -settings.tiltNoise, settings.tiltNoise), 0f, Range(rng, -settings.tiltNoise, settings.tiltNoise))
                     * Vector3.Slerp(Vector3.up, normal, 0.85f);
            var rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
            var size = crateBounds.size * scale;
            var anchor = new Vector3(p.x, floor.SampleLocal(p.x, p.y), p.y);

            site = default;
            for (float burial = Range(rng, settings.burialRange); burial >= settings.burialRange.x * 0.5f; burial -= 0.05f)
            {
                // Bounds centre sits half a height minus the sunk share above the ground
                var centre = anchor + up * (size.y * (0.5f - burial));
                if (!LidClear(centre, rotation, size))
                    continue;
                if (!BaseGrounded(centre, rotation, size))
                    return false;
                site = new CrateSite
                {
                    position = centre - rotation * (crateBounds.center * scale),
                    rotation = rotation,
                    scale = scale,
                    burial = burial,
                    lidY = (centre + up * (size.y * 0.5f)).y,
                };
                return true;
            }
            return false;
        }

        bool LidClear(Vector3 centre, Quaternion rotation, Vector3 size)
        {
            foreach (var corner in Corners(centre, rotation, size, 0.5f))
                if (corner.y < floor.SampleLocal(corner.x, corner.z) + settings.lidMargin)
                    return false;
            return true;
        }

        bool BaseGrounded(Vector3 centre, Quaternion rotation, Vector3 size)
        {
            foreach (var corner in Corners(centre, rotation, size, -0.5f))
                if (corner.y > floor.SampleLocal(corner.x, corner.z) + settings.floatTolerance)
                    return false;
            return true;
        }

        static IEnumerable<Vector3> Corners(Vector3 centre, Quaternion rotation, Vector3 size, float side)
        {
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                yield return centre + rotation * new Vector3(sx * size.x * 0.5f, side * size.y, sz * size.z * 0.5f);
        }

        bool Navigable(Vector2 p, float margin)
        {
            var world = floorTransform.TransformPoint(new Vector3(p.x, 0f, p.y));
            return new Vector2(world.x - worldCentre.x, world.z - worldCentre.z).magnitude <= areaRadius - margin;
        }

        bool Crowded(Vector2 p)
        {
            foreach (var c in Crates)
                if (Vector2.Distance(p, new Vector2(c.position.x, c.position.z)) < settings.minSpacing)
                    return true;
            return HasGate && Vector2.Distance(p, new Vector2(GatePosition.x, GatePosition.z)) < settings.gateClearRadius;
        }

        float NearestCrate(Vector2 p)
        {
            float nearest = float.MaxValue;
            foreach (var c in Crates)
                nearest = Mathf.Min(nearest, Vector2.Distance(p, new Vector2(c.position.x, c.position.z)));
            return nearest;
        }

        /// <summary>Is any rock arch in a column of this radius and height above the ground at p?</summary>
        bool ArchAbove(Vector2 p, float radius, float height)
        {
            if (terrainMask == 0)
                return false;
            var ground = floorTransform.TransformPoint(new Vector3(p.x, floor.SampleLocal(p.x, p.y), p.y));
            return Physics.CheckCapsule(ground + Vector3.up * (radius * 0.5f), ground + Vector3.up * height, radius,
                terrainMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Largest height difference from the ground at p within a radius; highest = the tallest ground found.</summary>
        float HeightSpread(Vector2 p, float radius, float ground, out float highest)
        {
            highest = ground;
            float lowest = ground;
            for (int ring = 1; ring <= 2; ring++)
            {
                float r = radius * ring / 2f;
                int steps = 8 * ring;
                for (int s = 0; s < steps; s++)
                {
                    var q = p + Polar(s * Mathf.PI * 2f / steps, r);
                    float h = floor.SampleLocal(q.x, q.y);
                    highest = Mathf.Max(highest, h);
                    lowest = Mathf.Min(lowest, h);
                }
            }
            return highest - lowest;
        }

        Vector3 Normal(Vector2 p)
        {
            const float e = 1f;
            float dx = floor.SampleLocal(p.x + e, p.y) - floor.SampleLocal(p.x - e, p.y);
            float dz = floor.SampleLocal(p.x, p.y + e) - floor.SampleLocal(p.x, p.y - e);
            return new Vector3(-dx, 2f * e, -dz).normalized;
        }

        static Vector2 Polar(float angle, float radius) => new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        static float Range(System.Random rng, Vector2 range) => Range(rng, range.x, range.y);
        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
