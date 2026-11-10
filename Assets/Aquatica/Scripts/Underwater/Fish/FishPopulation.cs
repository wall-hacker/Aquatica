using System.Collections.Generic;
using Aquatica.Navigation;
using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    /// <summary>
    /// Keeps the water around the submarine populated with fish: solo fish, pairs and schools drawn from a
    /// <see cref="FishCatalog"/>, each living out its own routine (cruising, hovering, feeding on the seafloor,
    /// inspecting the cockpit window, hiding by rocks, scattering from a fast submarine).
    /// <para>
    /// Fish never pop in or out of view: new groups only appear beyond the distance at which the fog fully hides
    /// them (biased towards where the submarine is heading, so there is always something to swim into), and
    /// groups are only removed once every member is even further away. Instances are pooled, so spawning
    /// doesn't allocate once the reef has warmed up.
    /// </para>
    /// </summary>
    public class FishPopulation : MonoBehaviour
    {
        [SerializeField] FishCatalog catalog;
        [Tooltip("Found in the world if left empty.")]
        [SerializeField] SubmarineMotor motor;

        [Header("Population")]
        [Tooltip("Fish kept alive around the submarine.")]
        [SerializeField] int targetCount = 100;
        [Tooltip("How often (s) the population is topped up / thinned out.")]
        [SerializeField] float maintenanceInterval = 0.4f;
        [Tooltip("Share of the population placed when the world is entered (the screen is still covered).")]
        [Range(0f, 1f)] [SerializeField] float initialFill = 0.75f;
        [Tooltip("Initial groups are kept at least this far (m) from the viewer.")]
        [SerializeField] float initialMinDistance = 10f;

        [Header("Visibility (fog)")]
        [Tooltip("Fog transmittance below which a fish counts as invisible.")]
        [Range(0.001f, 0.2f)] [SerializeField] float invisibleBelow = 0.03f;
        [Tooltip("Used when there is no fog.")]
        [SerializeField] float fallbackHiddenDistance = 60f;
        [Tooltip("Groups spawn this far (m) beyond the hidden distance, across a band of this width.")]
        [SerializeField] float spawnMargin = 4f;
        [SerializeField] float spawnBand = 14f;
        [Tooltip("Groups are removed once every member is this far (m) beyond the hidden distance.")]
        [SerializeField] float despawnMargin = 28f;
        [Tooltip("Share of spawns placed ahead of a moving submarine.")]
        [Range(0f, 1f)] [SerializeField] float spawnAheadBias = 0.75f;
        [Tooltip("New groups settle this far (m) from the submarine (or its path ahead) after swimming in out of the fog.")]
        [SerializeField] Vector2 homeDistance = new(7f, 22f);

        [Header("Limits")]
        [Tooltip("Fish stay this far (m) inside the navigable area.")]
        [SerializeField] float areaMargin = 12f;
        [Tooltip("Fish stay this far (m) below the water surface.")]
        [SerializeField] float surfaceMargin = 8f;
        [Tooltip("Gap (m) kept between fish and the hull.")]
        [SerializeField] float hullMargin = 0.6f;

        [Header("Curiosity")]
        [Tooltip("Seconds the submarine must be (almost) still before fish come to look.")]
        [SerializeField] float stillTimeForCuriosity = 2.5f;
        [SerializeField] float stillSpeed = 0.35f;
        [SerializeField] int maxCuriousGroups = 2;
        [Tooltip("Distance (m) from the hull's front face fish hover at to look in.")]
        [SerializeField] Vector2 windowDistance = new(0.5f, 1.6f);

        readonly List<FishSchool> schools = new();
        readonly Dictionary<GameObject, Stack<FishAgent>> pools = new();
        readonly Collider[] overlapBuffer = new Collider[16];
        UnderwaterWorld world;
        Transform viewer;
        Transform container;
        float maintenanceTimer;
        float stillTime;
        bool ready;

        public Transform Viewer => viewer;
        public Transform Sub => motor.transform;
        public Vector3 SubVelocity => motor.Velocity;
        public float SubSpeed => motor.Speed;
        public int TerrainMask { get; private set; }
        public float HiddenDistance { get; private set; }
        public int AliveCount { get; private set; }
        public IReadOnlyList<FishSchool> Schools => schools;

        /// <summary>Diagnostics: closest any fish has appeared / been removed (m from the viewer), after the initial fill.</summary>
        public float ClosestSpawn { get; private set; } = float.MaxValue;
        public float ClosestDespawn { get; private set; } = float.MaxValue;
        public int SpawnedGroups { get; private set; }
        public int DespawnedGroups { get; private set; }

        /// <summary>The submarine has been (almost) still long enough for curious fish to come over.</summary>
        public bool SubIsStill => stillTime >= stillTimeForCuriosity;

        void Update()
        {
            if (!ready && !TryInitialise())
                return;
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            stillTime = SubSpeed < stillSpeed ? stillTime + dt : 0f;
            for (int i = 0; i < schools.Count; i++)
                schools[i].Tick(dt);

            maintenanceTimer -= dt;
            if (maintenanceTimer <= 0f)
            {
                maintenanceTimer = maintenanceInterval;
                HiddenDistance = ComputeHiddenDistance();
                Despawn();
                Spawn(false);
            }
        }

        bool TryInitialise()
        {
            if (catalog == null)
                return false;
            if (world == null)
                world = GetComponentInParent<UnderwaterWorld>();
            if (world == null || world.Seafloor == null)
                return false;
            if (motor == null)
                motor = world.GetComponentInChildren<SubmarineMotor>();
            if (motor == null || !motor.IsReady)
                return false;
            var cam = Camera.main;
            if (cam == null)
                return false;

            viewer = cam.transform;
            TerrainMask = LayerMask.GetMask(UnderwaterWorld.TerrainLayerName);
            container = new GameObject("Fish").transform;
            container.SetParent(world.transform, false);
            HiddenDistance = ComputeHiddenDistance();
            ready = true;

            // The world was just entered behind the screen transition: fill the reef straight away
            int initial = Mathf.RoundToInt(targetCount * initialFill);
            for (int attempts = 0; AliveCount < initial && attempts < 60; attempts++)
                Spawn(true);
            return true;
        }

        /// <summary>Distance beyond which the fog hides a fish completely.</summary>
        float ComputeHiddenDistance()
        {
            float distance = fallbackHiddenDistance;
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
            var cam = viewer != null ? viewer.GetComponent<Camera>() : null;
            return cam != null ? Mathf.Min(distance, cam.farClipPlane) : distance;
        }

        // ---------------------------------------------------------------- spawning

        void Spawn(bool initial)
        {
            if (AliveCount >= targetCount)
                return;
            var species = PickSpecies();
            if (species == null)
                return;
            int size = Mathf.Min(species.RollGroupSize(), targetCount - AliveCount + 2);
            if (!FindSpawnCentre(species, initial, out var centre))
                return;

            // Fish that appear out in the fog swim in to a home near the submarine (or its path), so there is
            // always something to see, and a moving submarine runs into banks that scatter out of its way
            var home = initial ? centre : PickHome(species, centre);
            var school = new FishSchool(this, species, centre, home);
            for (int i = 0; i < size; i++)
            {
                var prefab = species.prefabs[Random.Range(0, species.prefabs.Length)];
                if (prefab == null)
                    continue;
                var offset = Random.insideUnitSphere * species.spread;
                offset.y *= 0.4f;
                var position = centre + offset;
                position.y = Mathf.Max(position.y, FloorHeight(position) + 0.3f);
                var agent = Acquire(prefab);
                float length = Random.Range(species.length.x, species.length.y);
                var heading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                agent.Begin(school, position, heading, length);
                school.Add(agent);
            }
            if (school.Count == 0)
                return;
            schools.Add(school);
            AliveCount += school.Count;
            if (!initial)
            {
                SpawnedGroups++;
                ClosestSpawn = Mathf.Min(ClosestSpawn, NearestMember(school));
            }
        }

        FishSpecies PickSpecies()
        {
            float total = 0f;
            foreach (var s in catalog.species)
                if (s.IsValid)
                    total += Weight(s);
            if (total <= 0f)
                return null;
            float roll = Random.value * total;
            foreach (var s in catalog.species)
            {
                if (!s.IsValid)
                    continue;
                roll -= Weight(s);
                if (roll <= 0f)
                    return s;
            }
            return null;
        }

        /// <summary>Spawn weight, lowered for species already present so the reef stays varied.</summary>
        float Weight(FishSpecies s)
        {
            int present = 0;
            foreach (var school in schools)
                if (school.Species == s)
                    present++;
            return s.spawnWeight / (1f + present * 0.75f);
        }

        bool FindSpawnCentre(FishSpecies species, bool initial, out Vector3 centre)
        {
            var origin = viewer.position;
            var flatVelocity = Vector3.ProjectOnPlane(SubVelocity, Vector3.up);
            bool ahead = !initial && flatVelocity.magnitude > 0.5f && Random.value < spawnAheadBias;
            float baseYaw = ahead ? Quaternion.LookRotation(flatVelocity).eulerAngles.y : 0f;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float yaw = ahead ? baseYaw + Random.Range(-75f, 75f) : Random.Range(0f, 360f);
                // Members scatter around the centre by up to the spread, so keep that much further out
                float minDistance = initial ? initialMinDistance + species.spread : HiddenDistance + spawnMargin + species.spread;
                float maxDistance = initial ? HiddenDistance + spawnMargin : minDistance + spawnBand;
                float distance = Random.Range(minDistance, maxDistance);
                var p = origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
                if (!InArea(p, areaMargin + species.spread))
                    continue;

                p.y = FloorHeight(p) + Random.Range(species.heightAboveFloor.x, species.heightAboveFloor.y);
                p.y = Mathf.Min(p.y, CeilingHeight);
                if (TerrainMask != 0 && Physics.CheckSphere(p, species.spread + 0.5f, TerrainMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (HullDistance(p, out _) < 6f)
                    continue;
                // Spawns must stay hidden (spawning above or below the viewer can be closer than the ring)
                if (!initial && Vector3.Distance(p, origin) < HiddenDistance + spawnMargin * 0.5f + species.spread)
                    continue;
                centre = p;
                return true;
            }
            centre = default;
            return false;
        }

        /// <summary>
        /// Where a newly spawned group settles: near the submarine's path a few seconds ahead when it is moving,
        /// otherwise around it, on the side the group came from.
        /// </summary>
        Vector3 PickHome(FishSpecies species, Vector3 spawn)
        {
            var origin = viewer.position;
            var flatVelocity = Vector3.ProjectOnPlane(SubVelocity, Vector3.up);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 p;
                if (flatVelocity.magnitude > 0.5f)
                {
                    var along = flatVelocity.normalized;
                    var side = Vector3.Cross(Vector3.up, along) * (Random.value < 0.5f ? -1f : 1f);
                    p = origin + along * Random.Range(homeDistance.y * 0.6f, homeDistance.y * 1.6f) + side * Random.Range(homeDistance.x * 0.5f, homeDistance.y);
                }
                else
                {
                    var toSpawn = Vector3.ProjectOnPlane(spawn - origin, Vector3.up).normalized;
                    p = origin + Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * toSpawn * Random.Range(homeDistance.x, homeDistance.y);
                }
                if (!InArea(p, areaMargin + species.spread))
                    continue;
                p.y = Mathf.Min(FloorHeight(p) + Random.Range(species.heightAboveFloor.x, species.heightAboveFloor.y), CeilingHeight);
                if (HullDistance(p, out _) < 6f)
                    continue;
                if (TerrainMask != 0 && Physics.CheckSphere(p, species.spread + 0.5f, TerrainMask, QueryTriggerInteraction.Ignore))
                    continue;
                return p;
            }
            return spawn;
        }

        void Despawn()
        {
            float limit = HiddenDistance + despawnMargin;
            var origin = viewer.position;
            for (int i = schools.Count - 1; i >= 0; i--)
            {
                var school = schools[i];
                bool far = true;
                foreach (var member in school.Members)
                {
                    var p = member.transform.position;
                    if (float.IsNaN(p.x))
                        continue;
                    if ((p - origin).sqrMagnitude < limit * limit)
                    {
                        far = false;
                        break;
                    }
                }
                if (!far)
                    continue;

                DespawnedGroups++;
                ClosestDespawn = Mathf.Min(ClosestDespawn, NearestMember(school));
                foreach (var member in school.Members)
                    Release(member);
                AliveCount -= school.Count;
                schools.RemoveAt(i);
            }
        }

        float NearestMember(FishSchool school)
        {
            float nearest = float.MaxValue;
            foreach (var member in school.Members)
                nearest = Mathf.Min(nearest, Vector3.Distance(member.transform.position, viewer.position));
            return nearest;
        }

        FishAgent Acquire(GameObject prefab)
        {
            if (!pools.TryGetValue(prefab, out var pool))
                pools[prefab] = pool = new Stack<FishAgent>();
            FishAgent agent;
            if (pool.Count > 0)
            {
                agent = pool.Pop();
                agent.gameObject.SetActive(true);
            }
            else
            {
                var go = Instantiate(prefab, container);
                if (go.GetComponent<FishModel>() == null)
                    go.AddComponent<FishModel>();
                agent = go.AddComponent<FishAgent>();
                agent.Prefab = prefab;
            }
            return agent;
        }

        void Release(FishAgent agent)
        {
            agent.gameObject.SetActive(false);
            if (pools.TryGetValue(agent.Prefab, out var pool))
                pool.Push(agent);
            else
                Destroy(agent.gameObject);
        }

        // ---------------------------------------------------------------- environment queries

        public float FloorHeight(Vector3 p) => world.SampleSeafloorHeight(p);

        /// <summary>Highest fish may swim (below the water surface).</summary>
        public float CeilingHeight => world.SurfaceHeight - surfaceMargin;

        public bool InArea(Vector3 p, float margin)
        {
            var c = world.transform.position;
            var offset = new Vector2(p.x - c.x, p.z - c.z);
            return offset.magnitude < motor.AreaRadius - margin;
        }

        /// <summary>Steering push back into the area near its edge (zero well inside it).</summary>
        public Vector3 AreaPush(Vector3 p)
        {
            var c = world.transform.position;
            var offset = new Vector3(p.x - c.x, 0f, p.z - c.z);
            float limit = motor.AreaRadius - areaMargin;
            float over = offset.magnitude - (limit - 10f);
            return over > 0f ? -offset.normalized * Mathf.Clamp01(over / 10f) : Vector3.zero;
        }

        /// <summary>
        /// Signed distance (m) from a point to the hull box inflated by the hull margin (negative = inside),
        /// and the outward normal of the nearest face.
        /// </summary>
        public float HullDistance(Vector3 p, out Vector3 normal) => motor.DistanceToHull(p, hullMargin, out normal);

        /// <summary>A spot just outside the cockpit's front window, at eye height (jitter in the hull's frame).</summary>
        public Vector3 WindowPoint(Vector3 jitter)
        {
            var sub = motor.transform;
            var bounds = motor.HullBounds;
            var eye = Quaternion.Inverse(sub.rotation) * (viewer.position - sub.position);
            float gap = Mathf.Lerp(windowDistance.x, windowDistance.y, Mathf.Clamp01(jitter.z));
            var local = new Vector3(eye.x + jitter.x, eye.y + jitter.y, bounds.max.z + hullMargin + gap);
            return sub.position + sub.rotation * local;
        }

        public bool CanBecomeCurious()
        {
            if (!SubIsStill)
                return false;
            int curious = 0;
            foreach (var s in schools)
                if (s.Plan == FishPlan.Curious)
                    curious++;
            return curious < maxCuriousGroups;
        }

        public bool PathClear(Vector3 from, Vector3 to, float radius)
        {
            if (TerrainMask == 0)
                return true;
            var delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-3f)
                return !Physics.CheckSphere(from, radius, TerrainMask, QueryTriggerInteraction.Ignore);
            return !Physics.SphereCast(from, radius, delta / distance, out _, distance, TerrainMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A hiding spot right next to the nearest rock within range, and the direction away from the rock.</summary>
        public bool TryFindShelter(Vector3 from, float range, out Vector3 spot, out Vector3 outward)
        {
            spot = outward = default;
            if (TerrainMask == 0)
                return false;
            int count = Physics.OverlapSphereNonAlloc(from, range, overlapBuffer, TerrainMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var collider = overlapBuffer[i];
                var closest = collider.ClosestPoint(from);
                float d = (closest - from).sqrMagnitude;
                if (d >= best)
                    continue;
                var away = from - closest;
                if (away.sqrMagnitude < 1e-6f)
                    away = closest - collider.bounds.center;
                away.y = Mathf.Max(away.y, 0f) * 0.3f;
                if (away.sqrMagnitude < 1e-6f)
                    continue;
                best = d;
                outward = away.normalized;
                spot = closest + outward * 0.7f;
            }
            if (best == float.MaxValue)
                return false;
            spot.y = Mathf.Max(spot.y, FloorHeight(spot) + 0.4f);
            return true;
        }

        void OnDestroy()
        {
            if (container != null)
                Destroy(container.gameObject);
        }

        void OnDrawGizmosSelected()
        {
            if (!ready)
                return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(viewer.position, HiddenDistance);
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(viewer.position, HiddenDistance + despawnMargin);
            foreach (var s in schools)
            {
                Gizmos.color = s.Plan switch
                {
                    FishPlan.Feed => Color.yellow, FishPlan.Curious => Color.magenta,
                    FishPlan.Shelter => Color.gray, FishPlan.Idle => Color.cyan, _ => Color.green,
                };
                Gizmos.DrawLine(s.LeaderPosition, s.Target);
            }
        }
    }
}
