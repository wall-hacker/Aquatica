using Aquatica.Cockpit;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Water in the cabin. While any cockpit device is faulty the cabin floods at <see cref="FaultDirector.FloodRate"/>
    /// (more faults and later phases flood faster; it pauses while the magnet minigame has the screen). Once every fault
    /// is repaired the cabin repressurises and the water drains away through the floor: the level falls steadily (the
    /// reverse of the flooding, easing in as the valves open and out over the last centimetres), to the sound of a valve
    /// opening, a pump humming under a hiss of air for as long as it drains, and a valve closing with a pressure sigh.
    /// If the water reaches the player's eyes they drown and the dive fails.
    /// The surface is a horizontal mesh fitted to the cabin at the current level: rays cast out from the cabin centre
    /// find the walls and the cockpit glass (nothing else), so the water fills the cabin's shape (it widens as the walls
    /// flare out) and simply covers seats and consoles. Its highlights come from the cabin's own lights (the ceiling
    /// fixtures, red during an alarm), not the sun.
    /// </summary>
    [RequireComponent(typeof(ExpeditionDirector), typeof(FaultDirector))]
    public class CabinFlood : MonoBehaviour
    {
        static readonly int AgitationId = Shader.PropertyToID("_Agitation");
        static readonly int LightPosId = Shader.PropertyToID("_CabinLightPos");
        static readonly int LightColorId = Shader.PropertyToID("_CabinLightColor");
        static readonly int LightCountId = Shader.PropertyToID("_CabinLightCount");
        const int MaxLights = 4;

        [Tooltip("A point on the cabin floor near its centre; the water level is measured up from it.")]
        [SerializeField] Transform floorOrigin;
        [Tooltip("The only surfaces that shape the water: the cabin walls (hull) and the cockpit glass. Seats, consoles and devices are ignored; the water just covers them.")]
        [SerializeField] Collider[] walls;
        [SerializeField] Material waterMaterial;

        [Header("Flooding")]
        [Tooltip("How fast the water rises (m/s) with one fault, before the phase multiplier (FaultDirector.FloodRate).")]
        [SerializeField] float riseRate = 0.022f;
        [Tooltip("The player drowns when the water is this close to their eyes (m).")]
        [SerializeField] float drownMargin = 0.04f;
        [Tooltip("Sound starts to muffle when the water is this far below the eyes (m).")]
        [SerializeField] float muffleRange = 0.35f;

        [Header("Draining")]
        [Tooltip("How fast the water drains (m/s) once every fault is repaired. 0.1 empties 1 m in about 11 s.")]
        [SerializeField] float drainRate = 0.1f;
        [Tooltip("Seconds for the valves to open and the drain to reach full speed.")]
        [SerializeField] float spinUp = 1f;
        [Tooltip("Over the last this many metres the drain slows down (to a third of its speed).")]
        [SerializeField] float easeOut = 0.08f;
        [SerializeField] float pumpVolume = 0.25f;
        [SerializeField] float valveVolume = 0.35f;
        [Tooltip("Volume of the water pouring in while the cabin floods.")]
        [SerializeField] float inflowVolume = 0.5f;

        [Header("Surface")]
        [SerializeField] int rays = 72;
        [SerializeField] int rings = 10;
        [SerializeField] float maxReach = 8f;
        [Tooltip("The surface mesh is refitted when the level has moved this much (m).")]
        [SerializeField] float refitStep = 0.01f;

        /// <summary>Water depth above the floor origin (m).</summary>
        public float Level { get => level; set => level = Mathf.Max(0f, value); }
        public bool Draining => draining;
        public bool Drowned => drowned;

        ExpeditionDirector expedition;
        FaultDirector faults;
        float level;
        bool draining, drowned;
        float drainTime;
        float fittedLevel = -1f;
        GameObject water;
        Mesh mesh;
        Material material;
        AudioSource valveSource, pumpSource, inflowSource;
        AudioLowPassFilter muffle;
        readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3[] vertices;
        int[] triangles;
        Light[] cabinLights;
        readonly Vector4[] lightPos = new Vector4[MaxLights];
        readonly Vector4[] lightColor = new Vector4[MaxLights];

        void Awake()
        {
            expedition = GetComponent<ExpeditionDirector>();
            faults = GetComponent<FaultDirector>();
            expedition.DiveEnded += Calm;
        }

        void OnDestroy()
        {
            expedition.DiveEnded -= Calm;
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
            if (muffle != null)
                Destroy(muffle);
        }

        void Start()
        {
            mesh = new Mesh { name = "CabinWater" };
            mesh.MarkDynamic();
            water = new GameObject("CabinWater", typeof(MeshFilter), typeof(MeshRenderer));
            water.transform.SetParent(transform, false);
            water.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = water.GetComponent<MeshRenderer>();
            material = new Material(waterMaterial);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            water.SetActive(false);

            // The cabin's own lights (ceiling fixtures) light the water; the sun doesn't reach in here
            var cabin = floorOrigin != null ? floorOrigin.parent : transform;
            cabinLights = System.Array.FindAll(cabin.GetComponentsInChildren<Light>(true), l => l.type != LightType.Directional);

            var floor = floorOrigin != null ? floorOrigin : transform;
            valveSource = NewSource(floor, 0.4f);
            pumpSource = NewSource(floor, 0.4f);
            pumpSource.clip = Tones.PumpLoop;
            pumpSource.loop = true;
            pumpSource.volume = 0f;
            inflowSource = NewSource(floorOrigin != null ? floorOrigin : transform, 0.5f);
            inflowSource.clip = Tones.Inflow;
            inflowSource.loop = true;
            inflowSource.volume = 0f;
        }

        AudioSource NewSource(Transform at, float spatial)
        {
            var go = new GameObject("FloodAudio");
            go.transform.SetParent(at, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial;
            source.minDistance = 1.5f;
            source.maxDistance = 12f;
            return source;
        }

        void Update()
        {
            if (drowned || !expedition.Started || expedition.Ended || faults.Systems == null)
            {
                SetInflow(0f);
                SetPump(0f);
                return;
            }

            float dt = Time.deltaTime;
            int active = faults.ActiveFaults;
            if (active > 0)
            {
                if (draining)
                    StopDraining();
                // The magnet minigame takes over the screen: the player can't act, so neither does the sea
                if (!expedition.MagnetBusy)
                    level += faults.FloodRate * riseRate * dt;
                SetInflow(expedition.MagnetBusy ? 0f : inflowVolume * Mathf.Clamp01(0.6f + 0.2f * active));
            }
            else
            {
                SetInflow(0f);
                if (level > 0f)
                    Drain(dt);
            }

            SetPump(draining ? pumpVolume * Mathf.SmoothStep(0f, 1f, drainTime / Mathf.Max(0.01f, spinUp)) : 0f);
            CheckEyes();
        }

        /// <summary>The water goes down through the floor: steady, easing in and out.</summary>
        void Drain(float dt)
        {
            if (!draining)
            {
                draining = true;
                drainTime = 0f;
                valveSource.PlayOneShot(Tones.ValveOpen, valveVolume);
            }
            drainTime += dt;
            float open = Mathf.SmoothStep(0f, 1f, drainTime / Mathf.Max(0.01f, spinUp));
            float tail = Mathf.Lerp(0.33f, 1f, Mathf.Clamp01(level / Mathf.Max(0.001f, easeOut)));
            level -= drainRate * open * tail * dt;
            if (level <= 0f)
            {
                level = 0f;
                StopDraining();
            }
        }

        void StopDraining()
        {
            draining = false;
            valveSource.PlayOneShot(Tones.ValveClose, valveVolume);
        }

        void SetPump(float volume)
        {
            if (pumpSource == null)
                return;
            pumpSource.volume = Mathf.MoveTowards(pumpSource.volume, volume, Time.deltaTime * 0.8f);
            if (pumpSource.volume > 0f && !pumpSource.isPlaying)
                pumpSource.Play();
            else if (pumpSource.volume <= 0f && pumpSource.isPlaying)
                pumpSource.Stop();
        }

        void SetInflow(float volume)
        {
            if (inflowSource == null)
                return;
            inflowSource.volume = Mathf.MoveTowards(inflowSource.volume, volume, Time.deltaTime * 0.5f);
            if (inflowSource.volume > 0f && !inflowSource.isPlaying)
                inflowSource.Play();
            else if (inflowSource.volume <= 0f && inflowSource.isPlaying)
                inflowSource.Stop();
        }

        /// <summary>Muffles the world as the water nears the player's eyes; at the eyes they drown.</summary>
        void CheckEyes()
        {
            var cam = Camera.main;
            if (cam == null || floorOrigin == null)
                return;
            if (muffle == null && cam.GetComponent<AudioListener>() != null)
            {
                muffle = cam.gameObject.AddComponent<AudioLowPassFilter>();
                muffle.cutoffFrequency = 22000f;
            }
            float eye = cam.transform.position.y - floorOrigin.position.y;
            float clearance = eye - level;
            if (muffle != null)
                muffle.cutoffFrequency = Mathf.Lerp(700f, 22000f, Mathf.Clamp01(clearance / Mathf.Max(0.01f, muffleRange)));
            if (level > 0.05f && clearance <= drownMargin)
                Drown();
        }

        void Drown()
        {
            drowned = true;
            SetInflow(0f);
            var listener = Camera.main;
            if (listener != null)
                AudioSource.PlayClipAtPoint(Tones.Bubbles, listener.transform.position, 1f);
            expedition.Fail();
        }

        /// <summary>The dive ended well: the water goes quietly.</summary>
        void Calm()
        {
            level = 0f;
            draining = false;
        }

        void LateUpdate()
        {
            bool visible = level > 0.003f && floorOrigin != null;
            if (water.activeSelf != visible)
                water.SetActive(visible);
            if (!visible)
            {
                fittedLevel = -1f;
                return;
            }

            // Level water, turning with the submarine
            var yaw = Quaternion.Euler(0f, floorOrigin.eulerAngles.y, 0f);
            water.transform.SetPositionAndRotation(floorOrigin.position + Vector3.up * level, yaw);
            if (Mathf.Abs(level - fittedLevel) >= refitStep)
                Fit(yaw);

            material.SetFloat(AgitationId, faults.ActiveFaults > 0 ? 0.6f : draining ? 0.25f : 0f);
            UpdateLights();
        }

        /// <summary>Hands the cabin lights (position and range, colour times intensity) to the water shader.</summary>
        void UpdateLights()
        {
            int count = 0;
            foreach (var l in cabinLights)
            {
                if (count >= MaxLights)
                    break;
                if (l == null || !l.isActiveAndEnabled)
                    continue;
                var p = l.transform.position;
                lightPos[count] = new Vector4(p.x, p.y, p.z, l.range);
                var c = l.color * l.intensity;
                lightColor[count] = new Vector4(c.r, c.g, c.b, 0f);
                count++;
            }
            material.SetVectorArray(LightPosId, lightPos);
            material.SetVectorArray(LightColorId, lightColor);
            material.SetInt(LightCountId, count);
        }

        /// <summary>Refits the surface mesh to the inside of the hull at the current level.</summary>
        void Fit(Quaternion yaw)
        {
            fittedLevel = level;
            int n = Mathf.Max(8, rays), m = Mathf.Max(1, rings);
            if (vertices == null || vertices.Length != 1 + n * m)
            {
                vertices = new Vector3[1 + n * m];
                triangles = new int[n * 3 + n * (m - 1) * 6];
                int k = 0;
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    triangles[k++] = 0;
                    triangles[k++] = 1 + next;
                    triangles[k++] = 1 + i;
                    for (int r = 0; r < m - 1; r++)
                    {
                        int a = 1 + r * n + i, b = 1 + r * n + next, c = 1 + (r + 1) * n + i, d = 1 + (r + 1) * n + next;
                        triangles[k++] = a; triangles[k++] = b; triangles[k++] = c;
                        triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
                    }
                }
            }

            // A couple of centimetres up when shallow, so the rays clear the floor
            var origin = floorOrigin.position + Vector3.up * Mathf.Max(level, 0.02f);
            vertices[0] = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float angle = i * Mathf.PI * 2f / n;
                var local = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float reach = Reach(origin, yaw * local);
                for (int r = 0; r < m; r++)
                    vertices[1 + r * n + i] = local * (reach * (r + 1) / m);
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        /// <summary>Distance to the inside of the hull along a horizontal direction.</summary>
        float Reach(Vector3 origin, Vector3 direction)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, maxReach, ~0, QueryTriggerInteraction.Ignore);
            float best = maxReach;
            for (int i = 0; i < count; i++)
                if (hits[i].distance < best && IsWall(hits[i].collider))
                    best = hits[i].distance;
            // Tuck just inside the wall
            return Mathf.Max(0f, best - 0.01f);
        }

        bool IsWall(Collider c)
        {
            if (walls == null || walls.Length == 0)
                return true;
            foreach (var w in walls)
                if (w == c)
                    return true;
            return false;
        }

        // ---------------------------------------------------------------- testing

        [ContextMenu("Debug: flood to 0.6 m")]
        void DebugFlood() => level = 0.6f;
    }
}
