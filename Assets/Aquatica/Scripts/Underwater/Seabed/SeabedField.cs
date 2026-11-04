using System.Collections.Generic;
using Aquatica.Expedition;
using Aquatica.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquatica.Underwater.Seabed
{
    /// <summary>
    /// Grows seaweed fields and scatters shells across the seafloor, the same way every time for a given world seed.
    /// <para>
    /// Seaweed grows in fields of one type: the seafloor is divided into irregular regions (a jittered cell pattern)
    /// and each region belongs to one seaweed type (e.g. green seagrass, brown kelp), so the same type grows next to
    /// itself in lush stands. Density varies within a field (thick patches, thinner stretches), neighbouring fields
    /// blend over a narrow border and only a few off-type plants are mixed in. Shells and starfish are scattered
    /// randomly on sand (starfish on rock too).
    /// </para>
    /// <para>
    /// Everything is drawn GPU-instanced in chunks culled by the fog distance and the view. Seaweed sways in the
    /// shader; near the submarine each plant gets a damped spring that bends it out of the hull's way and lets it
    /// wobble back. Large shells near the submarine (giant clams) get colliders on the terrain layer.
    /// </para>
    /// </summary>
    public class SeabedField : MonoBehaviour
    {
        [SerializeField] SeabedCatalog catalog;
        [Tooltip("Found in the world if left empty.")]
        [SerializeField] SubmarineMotor motor;

        [Header("Generation")]
        [Tooltip("Spacing (m) of the placement grid.")]
        [SerializeField] float cellSize = 2f;
        [Tooltip("Grow this far (m) beyond the navigable area, so its edge isn't bare.")]
        [SerializeField] float extraRadius = 25f;
        [Tooltip("Only short seaweed and no large shells within this distance (m) of the submarine's start.")]
        [SerializeField] float startClearRadius = 14f;
        [Tooltip("No seaweed on trench / crevasse floors beyond this passage value.")]
        [Range(0f, 1f)] [SerializeField] float trenchThreshold = 0.55f;
        [Tooltip("Rock from this rock-mask value (steep ground counts as rock too).")]
        [Range(0f, 1f)] [SerializeField] float rockThreshold = 0.45f;

        [Header("Seaweed fields")]
        [Tooltip("Typical size (m) of a field of one seaweed type.")]
        [SerializeField] float fieldSize = 30f;
        [Tooltip("Width of the blend between neighbouring fields, as a fraction of the field size.")]
        [Range(0f, 0.5f)] [SerializeField] float fieldBorder = 0.12f;
        [Tooltip("Share of plants of a random other type mixed into a field.")]
        [Range(0f, 0.5f)] [SerializeField] float offTypeShare = 0.04f;
        [Tooltip("Size (m) of the thick and thin patches inside fields, and the noise range from thin to thick.")]
        [SerializeField] float patchScale = 18f;
        [SerializeField] Vector2 patchRange = new(0.3f, 0.65f);
        [Tooltip("Seaweed clumps per grid cell, from the thinnest to the thickest part of a field.")]
        [SerializeField] Vector2 seaweedPerCell = new(0.35f, 3f);
        [Tooltip("Density on rock relative to sand.")]
        [Range(0f, 1f)] [SerializeField] float rockSeaweed = 0.2f;

        [Header("Shells")]
        [SerializeField] float sandShells = 0.05f;
        [SerializeField] float rockShells = 0.015f;

        [Header("Around the expedition's crates")]
        [Tooltip("Nothing grows or lies within this distance (m) of a crate.")]
        [SerializeField] float crateClearRadius = 1.8f;
        [Tooltip("Only seaweed up to crateGrassHeight (m) grows within this distance (m), so crates stay visible.")]
        [SerializeField] float crateShortRadius = 4.5f;
        [SerializeField] float crateGrassHeight = 0.6f;
        [Tooltip("Only short seaweed grows within this distance (m) of the exit gateway, so the way through stays open.")]
        [SerializeField] float gateShortRadius = 22f;

        [Header("Rendering")]
        [SerializeField] float chunkSize = 16f;
        [Tooltip("Fog transmittance below which a piece counts as invisible (it isn't drawn beyond that).")]
        [Range(0.001f, 0.2f)] [SerializeField] float invisibleBelow = 0.02f;
        [SerializeField] float fallbackDrawDistance = 70f;

        [Header("Seaweed pushed by the submarine")]
        [Tooltip("Seaweed within this distance (m) of the hull is simulated.")]
        [SerializeField] float pushRange = 10f;
        [Tooltip("Gap (m) seaweed keeps from the hull.")]
        [SerializeField] float pushMargin = 0.5f;
        [SerializeField] float springStiffness = 22f;
        [SerializeField] float springDamping = 3.2f;
        [Tooltip("Furthest a tip bends, as a fraction of the plant's height.")]
        [Range(0f, 1f)] [SerializeField] float maxBend = 0.85f;

        [Header("Collisions")]
        [Tooltip("Blocking pieces (giant clams) within this distance (m) of the submarine get colliders.")]
        [SerializeField] float colliderRange = 24f;
        [Tooltip("Collider box size relative to the piece's bounds.")]
        [Range(0.3f, 1f)] [SerializeField] float colliderShrink = 0.8f;

        const int Batch = 1023;
        static readonly int BendId = Shader.PropertyToID("_Bend");

        class Proto
        {
            public SeabedPiece piece;
            public Mesh mesh;
            public Material material;
            public Bounds bounds;
            public float weight, maxExtent;
            public int fieldType = -1;
            public readonly List<int> visible = new();
        }

        class FieldType
        {
            public string name;
            public float weight;
            public readonly List<int> protos = new();
        }

        struct Instance
        {
            public Matrix4x4 matrix;
            public Vector3 position;
            public Vector3 up;
            public float height;
            public float radius;
            public int proto;
        }

        struct Spring
        {
            public Vector3 offset, velocity;
        }

        readonly List<Proto> protos = new();
        readonly List<FieldType> fieldTypes = new();
        readonly List<int> shellProtos = new();
        readonly List<Instance> instances = new();
        readonly Dictionary<Vector2Int, List<int>> chunks = new();
        readonly List<Vector2Int> visibleChunks = new();
        readonly Dictionary<int, Spring> springs = new();
        readonly List<int> springKeys = new();
        readonly Dictionary<int, BoxCollider> colliders = new();
        readonly Stack<BoxCollider> colliderPool = new();
        readonly HashSet<int> wantedColliders = new();
        readonly List<int> releaseList = new();
        readonly Matrix4x4[] matrices = new Matrix4x4[Batch];
        readonly Vector4[] bends = new Vector4[Batch];
        readonly Plane[] frustum = new Plane[6];
        MaterialPropertyBlock props;
        UnderwaterWorld world;
        Camera viewer;
        Transform colliderRoot;
        Vector3[] instanceBend;
        int terrainLayer;
        int seed;
        readonly List<Vector2> crateSpots = new();
        Vector2? gateSpot;
        float colliderTimer;
        bool ready;

        /// <summary>Camera the field is culled around (the main camera by default; set it for a spectator camera).</summary>
        public Camera CullingCamera
        {
            get => viewer;
            set => viewer = value != null ? value : Camera.main;
        }

        public int InstanceCount => instances.Count;
        public int VisibleCount { get; private set; }
        public int SimulatedSeaweed => springs.Count;
        public int ActiveColliders => colliders.Count;

        void Update()
        {
            if (!ready && !TryInitialise())
                return;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                UpdateSeaweed(Mathf.Min(dt, 0.05f));
                colliderTimer -= dt;
                if (colliderTimer <= 0f)
                {
                    colliderTimer = 0.25f;
                    UpdateColliders();
                }
            }
            Draw();
        }

        bool TryInitialise()
        {
            if (catalog == null)
                return false;
            if (world == null)
                world = GetComponentInParent<UnderwaterWorld>();
            if (world == null || world.Seafloor == null || world.SeafloorTransform == null)
                return false;
            if (motor == null)
                motor = world.GetComponentInChildren<SubmarineMotor>();
            if (motor == null || !motor.IsReady)
                return false;
            var crateField = world.GetComponentInChildren<CrateField>();
            ExpeditionLayout expedition = null;
            if (crateField != null && !crateField.TryGetLayout(out expedition))
                return false;
            if (viewer == null)
                viewer = Camera.main;
            if (viewer == null)
                return false;

            props = new MaterialPropertyBlock();
            terrainLayer = LayerMask.NameToLayer(UnderwaterWorld.TerrainLayerName);
            colliderRoot = new GameObject("SeabedColliders").transform;
            colliderRoot.SetParent(world.transform, false);
            seed = world.Seed;
            if (expedition != null)
            {
                foreach (var c in expedition.Crates)
                    crateSpots.Add(new Vector2(c.position.x, c.position.z));
                if (expedition.HasGate)
                    gateSpot = new Vector2(expedition.GatePosition.x, expedition.GatePosition.z);
            }
            BuildProtos();
            Generate();
            ready = true;
            Debug.Log($"[Aquatica] Seabed: {instances.Count} pieces, {fieldTypes.Count} seaweed types (seed {seed})");
            return true;
        }

        void BuildProtos()
        {
            var byName = new Dictionary<string, FieldType>();
            foreach (var entry in catalog.entries)
            {
                if (entry.prefab == null || entry.weight <= 0f)
                    continue;
                var filter = entry.prefab.GetComponent<MeshFilter>();
                var renderer = entry.prefab.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null)
                    continue;
                var mesh = filter.sharedMesh;
                var p = new Proto
                {
                    piece = entry.prefab,
                    mesh = mesh,
                    material = renderer.sharedMaterial,
                    bounds = mesh.bounds,
                    weight = entry.weight,
                    maxExtent = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z)),
                };
                p.material.enableInstancing = true;
                int index = protos.Count;
                protos.Add(p);

                if (p.piece.Kind == SeabedKind.Shell)
                {
                    shellProtos.Add(index);
                    continue;
                }
                string key = string.IsNullOrEmpty(p.piece.FieldType) ? p.piece.name : p.piece.FieldType;
                if (!byName.TryGetValue(key, out var type))
                {
                    type = new FieldType { name = key };
                    byName[key] = type;
                    fieldTypes.Add(type);
                }
                p.fieldType = fieldTypes.IndexOf(type);
                type.protos.Add(index);
                type.weight += p.weight;
            }
        }

        // ---------------------------------------------------------------- generation

        /// <summary>Small deterministic generator, seeded per grid cell so the layout never depends on order.</summary>
        struct CellRandom
        {
            uint state;
            public CellRandom(int seed, int x, int z, uint salt)
            {
                state = (uint)seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)z * 0xC2B2AE3Du ^ salt * 0x27D4EB2Fu;
                Next();
                Next();
            }
            uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }
            public float Value => (Next() & 0xFFFFFF) / 16777216f;
            public float Range(float a, float b) => a + (b - a) * Value;
        }

        void Generate()
        {
            var floor = world.Seafloor;
            var floorTransform = world.SeafloorTransform;
            var offsets = new System.Random(seed ^ 0x5EA);
            var patchOffset = new Vector2((float)offsets.NextDouble() * 1000f, (float)offsets.NextDouble() * 1000f);

            int terrainMask = LayerMask.GetMask(UnderwaterWorld.TerrainLayerName);
            var centre = world.transform.position;
            float reach = motor.AreaRadius + extraRadius;
            float half = Mathf.Min(floor.Size * 0.5f - cellSize, reach + Vector3.Distance(floorTransform.position, centre));
            int cells = Mathf.CeilToInt(half * 2f / cellSize);
            var startLocal = floorTransform.InverseTransformPoint(motor.transform.position);

            for (int iz = 0; iz < cells; iz++)
            for (int ix = 0; ix < cells; ix++)
            {
                float cx = -half + (ix + 0.5f) * cellSize;
                float cz = -half + (iz + 0.5f) * cellSize;
                var world0 = floorTransform.TransformPoint(new Vector3(cx, 0f, cz));
                if (new Vector2(world0.x - centre.x, world0.z - centre.z).magnitude > reach)
                    continue;

                var rng = new CellRandom(seed, ix, iz, 1);
                bool nearStart = new Vector2(cx - startLocal.x, cz - startLocal.z).magnitude < startClearRadius;

                // Seaweed: thick and thin patches, all of the field's type
                if (fieldTypes.Count > 0)
                {
                    float patch = Smooth(patchRange.x, patchRange.y, Mathf.PerlinNoise(cx / patchScale + patchOffset.x, cz / patchScale + patchOffset.y));
                    float expected = Mathf.Lerp(seaweedPerCell.x, seaweedPerCell.y, patch);
                    int count = Mathf.FloorToInt(expected + rng.Value);
                    for (int k = 0; k < count; k++)
                    {
                        var p = Jitter(cx, cz, ref rng);
                        var ground = Classify(floor, p.x, p.y, out var normal, out bool trench);
                        if (trench || (ground == SeabedGround.Rock && rng.Value > rockSeaweed))
                            continue;
                        int type = FieldTypeAt(p, ref rng);
                        int proto = Pick(fieldTypes[type].protos, ground, ref rng, nearStart ? 2.6f : float.MaxValue);
                        if (proto >= 0)
                            Place(proto, p, normal, ref rng, terrainMask);
                    }
                }

                // Shells: scattered at random
                {
                    var p = Jitter(cx, cz, ref rng);
                    var ground = Classify(floor, p.x, p.y, out var normal, out bool trench);
                    float chance = trench ? 0f : ground == SeabedGround.Sand ? sandShells : rockShells;
                    if (rng.Value < chance)
                    {
                        int proto = Pick(shellProtos, ground, ref rng, nearStart ? 1f : float.MaxValue);
                        if (proto >= 0)
                            Place(proto, p, normal, ref rng, terrainMask);
                    }
                }
            }

            instanceBend = new Vector3[instances.Count];
        }

        /// <summary>
        /// The seaweed type at a seafloor-local position: the type of the nearest field centre (a jittered grid, so
        /// fields are irregular), blending into the neighbouring field near the border, with a few random accents.
        /// </summary>
        int FieldTypeAt(Vector2 p, ref CellRandom rng)
        {
            if (rng.Value < offTypeShare)
                return WeightedType(rng.Value);

            int gx = Mathf.FloorToInt(p.x / fieldSize), gz = Mathf.FloorToInt(p.y / fieldSize);
            float best = float.MaxValue, second = float.MaxValue;
            int bestType = 0, secondType = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var cellRng = new CellRandom(seed, gx + dx, gz + dz, 7);
                var site = new Vector2((gx + dx + cellRng.Range(0.1f, 0.9f)) * fieldSize, (gz + dz + cellRng.Range(0.1f, 0.9f)) * fieldSize);
                int type = WeightedType(cellRng.Value);
                float d = (site - p).magnitude;
                if (d < best)
                {
                    second = best; secondType = bestType;
                    best = d; bestType = type;
                }
                else if (d < second)
                {
                    second = d; secondType = type;
                }
            }

            // Near the border between two fields, mix the two (more of the neighbour the closer it gets)
            float border = (second - best) / Mathf.Max(0.01f, fieldSize * fieldBorder);
            if (border < 1f && rng.Value > 0.5f + 0.5f * border)
                return secondType;
            return bestType;
        }

        int WeightedType(float roll)
        {
            float total = 0f;
            foreach (var t in fieldTypes)
                total += t.weight;
            roll *= total;
            for (int i = 0; i < fieldTypes.Count; i++)
            {
                roll -= fieldTypes[i].weight;
                if (roll <= 0f)
                    return i;
            }
            return fieldTypes.Count - 1;
        }

        int Pick(List<int> candidates, SeabedGround ground, ref CellRandom rng, float maxSize)
        {
            float total = 0f;
            foreach (int i in candidates)
                if (Fits(protos[i], ground, maxSize))
                    total += protos[i].weight;
            if (total <= 0f)
                return -1;
            float roll = rng.Value * total;
            foreach (int i in candidates)
            {
                if (!Fits(protos[i], ground, maxSize))
                    continue;
                roll -= protos[i].weight;
                if (roll <= 0f)
                    return i;
            }
            return -1;
        }

        static bool Fits(Proto p, SeabedGround ground, float maxSize) =>
            (p.piece.Ground & ground) != 0 && p.piece.Size.x <= maxSize;

        Vector2 Jitter(float cx, float cz, ref CellRandom rng) =>
            new(cx + rng.Range(-0.5f, 0.5f) * cellSize, cz + rng.Range(-0.5f, 0.5f) * cellSize);

        /// <summary>Ground type at a seafloor-local position and its normal (seafloor space).</summary>
        SeabedGround Classify(Seafloor floor, float x, float z, out Vector3 normal, out bool trench)
        {
            const float d = 0.8f;
            float hx = floor.SampleLocal(x + d, z) - floor.SampleLocal(x - d, z);
            float hz = floor.SampleLocal(x, z + d) - floor.SampleLocal(x, z - d);
            normal = new Vector3(-hx, 2f * d, -hz).normalized;
            trench = floor.PassageAt(x, z) > trenchThreshold;
            return floor.MaskAt(x, z).r > rockThreshold || normal.y < 0.6f ? SeabedGround.Rock : SeabedGround.Sand;
        }

        void Place(int protoIndex, Vector2 at, Vector3 normalLocal, ref CellRandom rng, int terrainMask)
        {
            var proto = protos[protoIndex];
            var piece = proto.piece;
            float size = rng.Range(piece.Size.x, piece.Size.y);
            float scale = piece.Sways ? size : size / Mathf.Max(0.01f, proto.maxExtent);
            float height = proto.bounds.size.y * scale;
            float radius = Mathf.Max(proto.bounds.size.x, proto.bounds.size.z) * scale * 0.5f;
            if (HidesCrate(at, height, radius))
                return;

            // Shells lie along the ground; seaweed stands nearly upright
            var floorTransform = world.SeafloorTransform;
            var normal = floorTransform.TransformDirection(normalLocal).normalized;
            var up = piece.Sways
                ? Quaternion.Euler(rng.Range(-6f, 6f), 0f, rng.Range(-6f, 6f)) * Vector3.Slerp(Vector3.up, normal, 0.15f)
                : normal;
            var rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);

            float ground = world.Seafloor.SampleLocal(at.x, at.y);
            var position = floorTransform.TransformPoint(new Vector3(at.x, ground, at.y));
            // Sink the base in a little (more on slopes) so nothing floats on uneven ground
            position -= up * (height * 0.04f + (1f - normal.y) * radius * 0.6f);

            if (!piece.Sways && terrainMask != 0 &&
                Physics.CheckSphere(position + up * (height * 0.5f), Mathf.Max(0.15f, radius * 0.5f), terrainMask, QueryTriggerInteraction.Ignore))
                return; // inside a rock arch

            int index = instances.Count;
            instances.Add(new Instance
            {
                matrix = Matrix4x4.TRS(position, rotation, Vector3.one * scale),
                position = position,
                up = up,
                height = height,
                radius = radius,
                proto = protoIndex,
            });
            var chunk = ChunkOf(position);
            if (!chunks.TryGetValue(chunk, out var members))
                chunks[chunk] = members = new List<int>();
            members.Add(index);
        }

        bool HidesCrate(Vector2 at, float height, float radius)
        {
            if (gateSpot.HasValue && height > crateGrassHeight && Vector2.Distance(at, gateSpot.Value) < gateShortRadius)
                return true;
            foreach (var spot in crateSpots)
            {
                float d = Vector2.Distance(at, spot) - radius;
                if (d < crateClearRadius || (d < crateShortRadius && height > crateGrassHeight))
                    return true;
            }
            return false;
        }

        Vector2Int ChunkOf(Vector3 position) =>
            new(Mathf.FloorToInt(position.x / chunkSize), Mathf.FloorToInt(position.z / chunkSize));

        // ---------------------------------------------------------------- seaweed pushed by the hull

        void UpdateSeaweed(float dt)
        {
            var sub = motor.transform.position;
            float range = motor.HullBounds.extents.magnitude + pushRange;

            // Wake up seaweed near the hull
            var min = ChunkOf(sub - Vector3.one * range);
            var max = ChunkOf(sub + Vector3.one * range);
            for (int cz = min.y; cz <= max.y; cz++)
            for (int cx = min.x; cx <= max.x; cx++)
            {
                if (!chunks.TryGetValue(new Vector2Int(cx, cz), out var members))
                    continue;
                foreach (int i in members)
                {
                    var inst = instances[i];
                    if (!protos[inst.proto].piece.Sways || springs.ContainsKey(i))
                        continue;
                    if ((inst.position - sub).sqrMagnitude < range * range && Target(inst).sqrMagnitude > 1e-6f)
                        springs[i] = default;
                }
            }

            // Damped springs: pushed clear of the hull, wobbling back once it has passed
            springKeys.Clear();
            springKeys.AddRange(springs.Keys);
            foreach (int i in springKeys)
            {
                var inst = instances[i];
                var s = springs[i];
                var target = Target(inst);
                var accel = (target - s.offset) * springStiffness - s.velocity * springDamping;
                s.velocity += accel * dt;
                s.offset += s.velocity * dt;
                s.offset = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(s.offset, inst.up), inst.height * maxBend);
                if (target.sqrMagnitude < 1e-6f && s.offset.sqrMagnitude < 1e-5f && s.velocity.sqrMagnitude < 1e-4f)
                {
                    springs.Remove(i);
                    instanceBend[i] = Vector3.zero;
                    continue;
                }
                springs[i] = s;
                instanceBend[i] = s.offset;
            }
        }

        /// <summary>
        /// Tip offset that keeps a plant clear of the hull: the furthest any point along it must move, scaled up
        /// for lower points (a bend of b moves the point at height fraction t by b * t^2).
        /// </summary>
        Vector3 Target(in Instance inst)
        {
            var best = Vector3.zero;
            float bestLength = 0f;
            for (int k = 1; k <= 3; k++)
            {
                float t = k / 3f;
                var point = inst.position + inst.up * (inst.height * t);
                float d = motor.DistanceToHull(point, pushMargin, out var normal);
                if (d >= 0f)
                    continue;
                var away = Vector3.ProjectOnPlane(normal, inst.up);
                if (away.sqrMagnitude < 1e-4f)
                    away = Vector3.ProjectOnPlane(point - motor.transform.position, inst.up);
                float need = Mathf.Min(-d / (t * t), inst.height * maxBend);
                if (need > bestLength)
                {
                    bestLength = need;
                    best = away.normalized * need;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- collisions

        void UpdateColliders()
        {
            if (terrainLayer < 0)
                return;
            var sub = motor.transform.position;
            wantedColliders.Clear();
            var min = ChunkOf(sub - Vector3.one * colliderRange);
            var max = ChunkOf(sub + Vector3.one * colliderRange);
            for (int cz = min.y; cz <= max.y; cz++)
            for (int cx = min.x; cx <= max.x; cx++)
            {
                if (!chunks.TryGetValue(new Vector2Int(cx, cz), out var members))
                    continue;
                foreach (int i in members)
                {
                    var inst = instances[i];
                    if (protos[inst.proto].piece.BlocksSubmarine && (inst.position - sub).sqrMagnitude < colliderRange * colliderRange)
                        wantedColliders.Add(i);
                }
            }

            releaseList.Clear();
            foreach (var kv in colliders)
                if (!wantedColliders.Contains(kv.Key))
                    releaseList.Add(kv.Key);
            foreach (int i in releaseList)
            {
                var c = colliders[i];
                c.gameObject.SetActive(false);
                colliderPool.Push(c);
                colliders.Remove(i);
            }

            foreach (int i in wantedColliders)
            {
                if (colliders.ContainsKey(i))
                    continue;
                BoxCollider c;
                if (colliderPool.Count > 0)
                {
                    c = colliderPool.Pop();
                    c.gameObject.SetActive(true);
                }
                else
                {
                    var go = new GameObject("SeabedCollider") { layer = terrainLayer };
                    go.transform.SetParent(colliderRoot, false);
                    c = go.AddComponent<BoxCollider>();
                }
                var inst = instances[i];
                var bounds = protos[inst.proto].bounds;
                var m = inst.matrix;
                c.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
                c.transform.localScale = m.lossyScale;
                c.center = bounds.center;
                c.size = bounds.size * colliderShrink;
                colliders[i] = c;
            }
        }

        // ---------------------------------------------------------------- drawing

        void Draw()
        {
            float drawDistance = ComputeDrawDistance();
            var camPos = viewer.transform.position;
            GeometryUtility.CalculateFrustumPlanes(viewer, frustum);

            visibleChunks.Clear();
            int r = Mathf.CeilToInt(drawDistance / chunkSize) + 1;
            var c0 = ChunkOf(camPos);
            float reach = drawDistance + chunkSize * 0.75f;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var key = new Vector2Int(c0.x + dx, c0.y + dz);
                if (!chunks.ContainsKey(key))
                    continue;
                var centre = new Vector3((key.x + 0.5f) * chunkSize, camPos.y, (key.y + 0.5f) * chunkSize);
                if ((centre - camPos).sqrMagnitude > reach * reach)
                    continue;
                var bounds = new Bounds(centre, new Vector3(chunkSize + 8f, 400f, chunkSize + 8f));
                if (GeometryUtility.TestPlanesAABB(frustum, bounds))
                    visibleChunks.Add(key);
            }

            foreach (var p in protos)
                p.visible.Clear();
            foreach (var key in visibleChunks)
                foreach (int i in chunks[key])
                {
                    var inst = instances[i];
                    float limit = drawDistance + inst.radius + inst.height;
                    if ((inst.position - camPos).sqrMagnitude < limit * limit)
                        protos[inst.proto].visible.Add(i);
                }

            int drawn = 0;
            foreach (var p in protos)
            {
                if (p.visible.Count == 0)
                    continue;
                drawn += p.visible.Count;
                int count = 0;
                bool sways = p.piece.Sways;
                foreach (int i in p.visible)
                {
                    matrices[count] = instances[i].matrix;
                    if (sways)
                        bends[count] = instanceBend[i];
                    if (++count == Batch)
                    {
                        Flush(p, count, sways);
                        count = 0;
                    }
                }
                if (count > 0)
                    Flush(p, count, sways);
            }
            VisibleCount = drawn;
        }

        void Flush(Proto p, int count, bool sways)
        {
            props.Clear();
            if (sways)
                props.SetVectorArray(BendId, bends);
            // All cameras (also the editor's scene view); culling is done around the viewer
            Graphics.DrawMeshInstanced(p.mesh, 0, p.material, matrices, count, props, ShadowCastingMode.Off, true, 0);
        }

        float ComputeDrawDistance()
        {
            float distance = fallbackDrawDistance;
            if (RenderSettings.fog)
            {
                float density = Mathf.Max(1e-4f, RenderSettings.fogDensity);
                float k = -Mathf.Log(invisibleBelow);
                distance = RenderSettings.fogMode switch
                {
                    FogMode.ExponentialSquared => Mathf.Sqrt(k) / density,
                    FogMode.Exponential => k / density,
                    _ => RenderSettings.fogEndDistance,
                };
            }
            return Mathf.Min(distance, viewer.farClipPlane);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        void OnDestroy()
        {
            if (colliderRoot != null)
                Destroy(colliderRoot.gameObject);
        }
    }
}
