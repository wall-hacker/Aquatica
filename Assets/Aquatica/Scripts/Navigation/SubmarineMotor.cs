using System;
using Aquatica.Player;
using Aquatica.Underwater;
using UnityEngine;

namespace Aquatica.Navigation
{
    /// <summary>
    /// Moves the submarine through the water, taking the player (the AR camera rig) along with it.
    /// <para>
    /// While <see cref="Throttle"/> is held the engine spools up and pushes the hull along
    /// <see cref="Heading"/> (the direction the player looks). The hull slowly yaws its nose towards the heading,
    /// and because the player turns with the hull, looking off to one side keeps it turning that way until they
    /// look ahead again. Looking up or down climbs or dives; levelling the view holds the depth. Everything is
    /// deliberately sluggish: thrust spools, velocity eases in and coasts out, turns build up and settle, and the
    /// hull pitches a little into climbs/dives and banks into turns, with a gentle idle sway.
    /// </para>
    /// <para>
    /// Collisions: only the front half of the hull (the part the player can picture from the cockpit) touches the
    /// seafloor and rock arches. Hitting them stops the motion into the surface with a short shake and lets the
    /// hull glide along it (over slopes, under overhangs) with light friction, so it never gets stuck. Turns that
    /// would swing the front into something are stopped. Towards the edge of the navigable area or the surface, a current pushes it back
    /// (<see cref="InCurrents"/>, <see cref="CurrentPush"/>).
    /// </para>
    /// Rotation pivots around the viewer, so turning feels like turning on the spot rather than being swung around.
    /// </summary>
    [DefaultExecutionOrder(900)] // before CameraWallBlocker (1000), after tracking has updated the camera
    public class SubmarineMotor : MonoBehaviour
    {
        [Header("Engine")]
        [SerializeField] float maxSpeed = 3.6f;
        [Tooltip("Climb/dive speed relative to maxSpeed.")]
        [SerializeField] float verticalSpeedScale = 0.9f;
        [Tooltip("Seconds for the engine to spool up to full thrust.")]
        [SerializeField] float spoolUpTime = 1.6f;
        [SerializeField] float spoolDownTime = 0.8f;
        [Tooltip("Time constant (s) of the hull catching up with the thrust.")]
        [SerializeField] float accelerationTime = 1.8f;
        [Tooltip("Time constant (s) of the hull coasting to a stop without thrust.")]
        [SerializeField] float coastTime = 2.8f;
        [Tooltip("Time to brake to a stop while anchored (s).")]
        [SerializeField] float anchorBrakeTime = 0.7f;
        [Tooltip("Time constant (s) of climbing/diving catching up with the look direction (and settling when level).")]
        [SerializeField] float verticalResponseTime = 0.9f;

        [Header("Steering")]
        [SerializeField] float maxTurnRate = 18f;
        [Tooltip("Look this far (degrees) off the bow for the full turn rate.")]
        [SerializeField] float fullTurnAngle = 50f;
        [SerializeField] float turnResponseTime = 1.2f;
        [Tooltip("How far down (degrees) the phone is normally held: looking this much down counts as looking level.")]
        [SerializeField] float neutralPitch = 12f;
        [Tooltip("Look pitch (degrees) around the neutral one that is ignored, so a slightly tilted phone doesn't drift up or down.")]
        [SerializeField] float pitchDeadZone = 7f;
        [Tooltip("Look pitch (degrees) for the full climb/dive rate.")]
        [SerializeField] float fullPitchAngle = 30f;

        [Header("Hull motion")]
        [SerializeField] float maxPitchTilt = 8f;
        [SerializeField] float maxBank = 3f;
        [SerializeField] float tiltResponseTime = 1.5f;
        [SerializeField] float swayHeight = 0.03f;
        [SerializeField] float swayRoll = 0.4f;
        [SerializeField] float swayPeriod = 7f;

        [Header("Collisions (seafloor and rock arches)")]
        [Tooltip("Gap kept between the bottom of the hull and the seafloor.")]
        [SerializeField] float floorClearance = 0.8f;
        [Tooltip("How far (m) the hull may dip into the floor clearance before ground counts as in the way, so ripples in the sand don't stop it.")]
        [SerializeField] float floorTolerance = 0.5f;
        [Tooltip("Gap kept between the hull spheres and rock.")]
        [SerializeField] float rockClearance = 0.3f;
        [Tooltip("Seconds to stop once something is in the way: short, so contact feels like a firm bump.")]
        [SerializeField] float contactBrakeTime = 0.35f;
        [Tooltip("Friction while sliding along something (0 = glides freely, 1 = sticks); stronger the more head-on the hit.")]
        [Range(0f, 1f)] [SerializeField] float slideFriction = 0.15f;
        [Tooltip("How freely a collision may turn the motion up or down along a surface: 1 = glides up and over slopes and under overhangs (forgiving), 0 = only ever reduces the climb/dive asked for (strict, can get stuck).")]
        [Range(0f, 1f)] [SerializeField] float collisionLift = 1f;
        [Tooltip("Time constant (s) of the friction taking effect.")]
        [SerializeField] float frictionTime = 0.25f;
        [Tooltip("Only the hull in front of this point (this transform's space, along the bow) collides: the part the player can picture from the cockpit.")]
        [SerializeField] float collisionBackZ = -5f;
        [Tooltip("Step (m) used when checking the ground along the path.")]
        [SerializeField] float floorProbeStep = 0.4f;

        [Header("Impact shake")]
        [Tooltip("Speed (m/s) below which touching something doesn't shake the cabin.")]
        [SerializeField] float minImpactSpeed = 0.4f;
        [Tooltip("Shake amplitude (m) per m/s of impact speed.")]
        [SerializeField] float shakePerSpeed = 0.035f;
        [SerializeField] float maxShake = 0.09f;
        [SerializeField] float shakeDuration = 0.6f;
        [SerializeField] float shakeFrequency = 11f;

        [Header("Navigable area (currents push back beyond it)")]
        [Tooltip("Radius around the world centre the submarine may roam.")]
        [SerializeField] float areaRadius = 180f;
        [Tooltip("Gap kept between the top of the hull and the water surface.")]
        [SerializeField] float surfaceClearance = 35f;
        [Tooltip("Width of the band inside the limits where the current builds up.")]
        [SerializeField] float sideCurrentZone = 20f;
        [SerializeField] float topCurrentZone = 8f;
        [Tooltip("Strength of the current at the limit, relative to maxSpeed (above 1 always wins).")]
        [SerializeField] float currentStrength = 1.4f;
        [SerializeField] float currentResponseTime = 0.8f;

        [Header("Rock (arches on the terrain layer)")]
        [Tooltip("Spheres approximating the front half of the hull in this transform's space (they scale with it): xyz = centre, w = radius.")]
        [SerializeField] Vector4[] hullSpheres =
        {
            new(0f, 1.5f, 2.3f, 2.8f), new(0f, 1.5f, -1f, 3f), new(0f, 1.5f, -3.2f, 2.6f),
            new(0.9f, 4.4f, -2.4f, 1.9f), new(0f, -0.8f, -1.5f, 2.2f),
        };

        /// <summary>Thrust lever held.</summary>
        public bool Throttle { get; set; }

        /// <summary>Holds station (e.g. while the magnet is down): the engine is cut and the hull brakes to a stop.</summary>
        public bool Anchored { get; set; }

        /// <summary>World direction to travel in while the throttle is held.</summary>
        public Vector3 Heading { get; set; }

        public Vector3 Velocity => velocity + current;
        public float Speed => Velocity.magnitude;
        public float EngineLevel => spool;
        public bool InCurrents => currentZone > 0.01f;

        /// <summary>The current pushing the hull back (world space, m/s).</summary>
        public Vector3 CurrentPush => current;

        /// <summary>How deep into the current zone the hull is (0 = outside, 1 = at the limit).</summary>
        public float CurrentZone => currentZone;

        /// <summary>Hull extents around this transform (rotation-aligned, world units).</summary>
        public Bounds HullBounds => hullBounds;

        /// <summary>The motor has found its world and measured the hull.</summary>
        public bool IsReady => ready;

        /// <summary>Radius (m) around the world centre the submarine may roam.</summary>
        public float AreaRadius => areaRadius;

        /// <summary>Highest the submarine's origin goes (world y) before the surface current starts pushing it down.</summary>
        public float CruiseCeiling
        {
            get
            {
                // Also answers before the motor has started (e.g. for editor previews)
                var w = world != null ? world : GetComponentInParent<UnderwaterWorld>();
                if (w == null)
                    return float.MaxValue;
                var hull = ready ? hullBounds : MeasureHull();
                return w.SurfaceHeight - surfaceClearance - hull.max.y - topCurrentZone;
            }
        }

        /// <summary>
        /// Signed distance (m) from a point to the hull's box inflated by a margin (negative = inside), and the
        /// outward normal of the nearest face. Used by things that must keep out of the way (fish, plants).
        /// </summary>
        public float DistanceToHull(Vector3 point, float margin, out Vector3 normal)
        {
            var local = Quaternion.Inverse(transform.rotation) * (point - transform.position) - hullBounds.center;
            var extents = hullBounds.extents + Vector3.one * margin;
            var q = new Vector3(Mathf.Abs(local.x) - extents.x, Mathf.Abs(local.y) - extents.y, Mathf.Abs(local.z) - extents.z);
            var outside = Vector3.Max(q, Vector3.zero);
            float distance;
            Vector3 localNormal;
            if (outside.sqrMagnitude > 0f)
            {
                distance = outside.magnitude;
                localNormal = new Vector3(Mathf.Sign(local.x) * outside.x, Mathf.Sign(local.y) * outside.y, Mathf.Sign(local.z) * outside.z) / distance;
            }
            else
            {
                // Inside: leave through the nearest face
                distance = Mathf.Max(q.x, Mathf.Max(q.y, q.z));
                localNormal = q.x >= q.y && q.x >= q.z ? new Vector3(Mathf.Sign(local.x), 0f, 0f)
                    : q.y >= q.z ? new Vector3(0f, Mathf.Sign(local.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(local.z));
            }
            normal = transform.rotation * localNormal;
            return distance;
        }

        /// <summary>Raised when the submarine enters or leaves the currents at the edge of the area.</summary>
        public event Action<bool> CurrentsChanged;

        /// <summary>A real hit on ground or rock: impact speed (m/s) and the world direction from the hull towards what was hit.</summary>
        public event Action<float, Vector3> Bumped;

        /// <summary>The hull is touching ground or rock while moving: sliding speed (m/s), 0 when clear.</summary>
        public float ScrapeSpeed { get; private set; }

        /// <summary>World direction from the hull towards what it is scraping along (valid while <see cref="ScrapeSpeed"/> &gt; 0).</summary>
        public Vector3 ScrapeDirection { get; private set; }

        /// <summary>
        /// Knocks the cabin along a world direction (e.g. away from a fault), easing back with a decaying wobble.
        /// Stronger jolts replace weaker ones still running.
        /// </summary>
        public void Jolt(Vector3 worldDirection, float amplitude, float duration = 0.8f)
        {
            if (joltTime > 0f && amplitude < joltAmplitude * (joltTime / joltDuration))
                return;
            joltDirection = worldDirection.sqrMagnitude > 1e-6f ? worldDirection.normalized : Vector3.up;
            joltAmplitude = Mathf.Min(amplitude, 0.12f);
            joltDuration = Mathf.Max(0.05f, duration);
            joltTime = joltDuration;
        }

        UnderwaterWorld world;
        Transform viewer;
        Transform rig;
        CameraWallBlocker blocker;
        Bounds hullBounds;
        Bounds collisionBounds;
        int rockMask;
        readonly RaycastHit[] rockHits = new RaycastHit[8];
        readonly Collider[] rockOverlaps = new Collider[8];

        float yaw, pitch, roll;
        Vector3 velocity, current;
        float yawRate;
        float spool;
        float currentZone;
        bool ready;
        bool wasInCurrents;
        Vector3 lastSway;
        bool braking;
        float shakeTime;
        float shakeAmplitude;
        float shakeSeed;
        Vector3 joltDirection;
        float joltAmplitude, joltDuration = 1f, joltTime;

        /// <summary>World-space depth of the hull's reference point below the water surface.</summary>
        public float DepthBelowSurface(Vector3 worldPoint) => world != null ? world.SurfaceHeight - worldPoint.y : 0f;

        /// <summary>Compass heading of the bow in degrees (0-360, relative to the world's start orientation).</summary>
        public float Bearing => Mathf.Repeat(yaw - (world != null ? world.transform.eulerAngles.y : 0f), 360f);

        void LateUpdate()
        {
            if (!ready && !TryInitialise())
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            UpdateEngine(dt);
            UpdateSteering(dt);
            UpdateVelocity(dt);
            UpdateCurrents(dt);
            ApplyCollisions(dt);
            Move(dt);

            bool inCurrents = InCurrents;
            if (inCurrents != wasInCurrents)
            {
                wasInCurrents = inCurrents;
                CurrentsChanged?.Invoke(inCurrents);
            }
        }

        bool TryInitialise()
        {
            if (world == null)
                world = GetComponentInParent<UnderwaterWorld>();
            if (world == null || world.Seafloor == null)
                return false;

            var cam = Camera.main;
            if (cam == null)
                return false;
            viewer = cam.transform;
            rig = viewer.root;
            blocker = FindAnyObjectByType<CameraWallBlocker>();

            hullBounds = MeasureHull();
            collisionBounds = hullBounds;
            collisionBounds.min = new Vector3(hullBounds.min.x, hullBounds.min.y, Mathf.Max(hullBounds.min.z, collisionBackZ * transform.lossyScale.z));
            rockMask = LayerMask.GetMask(UnderwaterWorld.TerrainLayerName);
            lastSway = Vector3.zero;
            var euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = roll = 0f;
            ready = true;
            return true;
        }

        void UpdateEngine(float dt)
        {
            bool thrust = Throttle && !Anchored;
            float rate = thrust ? 1f / Mathf.Max(0.01f, spoolUpTime) : 1f / Mathf.Max(0.01f, spoolDownTime);
            spool = Mathf.MoveTowards(spool, thrust ? 1f : 0f, rate * dt);
        }

        void UpdateSteering(float dt)
        {
            float targetRate = 0f;
            var flat = Vector3.ProjectOnPlane(Heading, Vector3.up);
            if (spool > 0f && !Anchored && flat.sqrMagnitude > 1e-4f)
            {
                var bow = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float error = Vector3.SignedAngle(bow, flat, Vector3.up);
                targetRate = Mathf.Clamp(error / fullTurnAngle, -1f, 1f) * maxTurnRate * Smooth01(spool);
            }
            yawRate = Mathf.Lerp(yawRate, targetRate, Response(dt, turnResponseTime));
        }

        void UpdateVelocity(float dt)
        {
            var target = Vector3.zero;
            if (spool > 0f && !Anchored && Heading.sqrMagnitude > 1e-4f)
            {
                var dir = Heading.normalized;
                var flat = Vector3.ProjectOnPlane(dir, Vector3.up);
                // Relative to how the phone is normally held (slightly down)
                float lookPitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg + neutralPitch;
                float climb = Mathf.Sign(lookPitch) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pitchDeadZone, fullPitchAngle, Mathf.Abs(lookPitch)));

                float horizontal = flat.sqrMagnitude > 1e-6f ? Mathf.Cos(lookPitch * Mathf.Deg2Rad) : 0f;
                target = (flat.sqrMagnitude > 1e-6f ? flat.normalized * horizontal : Vector3.zero) * maxSpeed
                       + Vector3.up * (climb * maxSpeed * verticalSpeedScale);
                target *= Smooth01(spool);
            }

            // Horizontal: heavy, slow to build up and to coast out. Vertical (ballast and planes): quicker.
            var flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            var flatTarget = new Vector3(target.x, 0f, target.z);
            float time = Anchored ? anchorBrakeTime : flatTarget.sqrMagnitude > flatVelocity.sqrMagnitude ? accelerationTime : coastTime;
            flatVelocity = Vector3.Lerp(flatVelocity, flatTarget, Response(dt, time));
            float vertical = Mathf.Lerp(velocity.y, target.y, Response(dt, verticalResponseTime));
            velocity = new Vector3(flatVelocity.x, vertical, flatVelocity.z);
        }

        /// <summary>The current at a world position: push direction (unit) and strength 0-1+ inside the band.</summary>
        public Vector3 CurrentAt(Vector3 position, out float strength, float extraBand = 0f)
        {
            strength = 0f;
            if (world == null)
                return Vector3.zero;

            var centre = world.transform.position;
            var offset = new Vector3(position.x - centre.x, 0f, position.z - centre.z);
            float distance = offset.magnitude;
            float sideBand = sideCurrentZone + extraBand;
            float side = Mathf.Clamp((distance - (areaRadius - sideBand)) / sideBand, 0f, 1.5f);

            float ceiling = world.SurfaceHeight - surfaceClearance - hullBounds.max.y;
            float topBand = topCurrentZone + extraBand;
            float top = Mathf.Clamp((position.y - (ceiling - topBand)) / topBand, 0f, 1.5f);

            var push = Vector3.zero;
            if (side > 0f && distance > 1e-3f)
                push -= offset / distance * (side * side);
            if (top > 0f)
                push -= Vector3.up * (top * top);
            strength = Mathf.Max(side, top);
            return push.sqrMagnitude > 1e-8f ? push.normalized : Vector3.zero;
        }

        /// <summary>Edge and surface: currents push back, building up across the zone.</summary>
        void UpdateCurrents(float dt)
        {
            var position = transform.position - lastSway;
            var centre = world.transform.position;
            var offset = new Vector3(position.x - centre.x, 0f, position.z - centre.z);
            float distance = offset.magnitude;
            float side = Mathf.Clamp((distance - (areaRadius - sideCurrentZone)) / sideCurrentZone, 0f, 1.5f);
            float ceiling = world.SurfaceHeight - surfaceClearance - hullBounds.max.y;
            float top = Mathf.Clamp((position.y - (ceiling - topCurrentZone)) / topCurrentZone, 0f, 1.5f);

            var push = Vector3.zero;
            if (side > 0f && distance > 1e-3f)
                push -= offset / distance * (side * side);
            if (top > 0f)
                push -= Vector3.up * (top * top);
            push *= maxSpeed * currentStrength;
            current = Vector3.Lerp(current, push, Response(dt, currentResponseTime));
            currentZone = Mathf.Max(side, top);
        }

        /// <summary>
        /// Contact with ground or rock: the motion into the surface is stopped (briefly braked, with a shake on a
        /// real hit) and the rest glides along it with light friction, up and over slopes or under overhangs as
        /// far as <see cref="collisionLift"/> allows, so the player doesn't get stuck. Turns that would swing the
        /// hull into something are stopped.
        /// </summary>
        void ApplyCollisions(float dt)
        {
            var position = transform.position - lastSway;
            var intended = velocity;
            var v = velocity;
            bool contact = false;
            float impact = 0f;
            var contactNormal = Vector3.zero;

            // Two passes so sliding off one surface into another (a corner) is handled too
            for (int pass = 0; pass < 2; pass++)
            {
                float speed = v.magnitude;
                if (speed < 1e-3f)
                    break;
                var direction = v / speed;
                float free = FreeDistance(position, direction, speed * contactBrakeTime + 1f, out var normal);
                if (speed * contactBrakeTime <= free)
                    break;

                contact = true;
                contactNormal += normal;
                float headOn = Mathf.Clamp01(-Vector3.Dot(direction, normal));
                float closing = -Vector3.Dot(v, normal);
                float allowedClosing = free * headOn / contactBrakeTime;
                if (closing > allowedClosing)
                {
                    impact = Mathf.Max(impact, closing);
                    v += normal * (closing - allowedClosing);
                }

                var normalPart = normal * Vector3.Dot(v, normal);
                var slide = v - normalPart;
                slide = Vector3.Lerp(slide, slide * (1f - slideFriction * headOn), Response(dt, frictionTime));
                v = normalPart + slide;
            }

            float strictY = Mathf.Clamp(v.y, Mathf.Min(intended.y, 0f), Mathf.Max(intended.y, 0f));
            v.y = Mathf.Lerp(strictY, v.y, collisionLift);
            if (contact && !braking && impact > minImpactSpeed)
            {
                Shake(impact);
                Bumped?.Invoke(impact, -contactNormal.normalized);
            }
            braking = contact;
            ScrapeSpeed = contact ? v.magnitude : 0f;
            if (contact)
                ScrapeDirection = -contactNormal.normalized;
            velocity = v;

            // The current never shoves the hull into terrain either
            float currentSpeed = current.magnitude;
            if (currentSpeed > 1e-3f)
            {
                var direction = current / currentSpeed;
                float free = FreeDistance(position, direction, currentSpeed * contactBrakeTime + 1f, out _);
                current = direction * Mathf.Min(currentSpeed, free / contactBrakeTime);
            }

            // Turning: stop the turn if the swung hull would dig deeper into ground or rock
            if (Mathf.Abs(yawRate) > 1e-3f)
            {
                var pivot = viewer.position;
                var turn = Quaternion.Euler(0f, yawRate * dt * 3f, 0f); // look a few frames ahead
                var turnedPosition = pivot + turn * (position - pivot);
                float turnedYaw = yaw + yawRate * dt * 3f;
                bool floorWorse = FloorDeficit(turnedPosition, turnedYaw) > Mathf.Max(floorTolerance, FloorDeficit(position, yaw) + 1e-3f);
                bool rockWorse = RockPenetration(turn, pivot) > RockPenetration(Quaternion.identity, pivot) + 0.05f;
                if (floorWorse || rockWorse)
                {
                    float tailSpeed = Mathf.Abs(yawRate) * Mathf.Deg2Rad * collisionBounds.size.z;
                    if (tailSpeed > minImpactSpeed)
                    {
                        Shake(tailSpeed);
                        // The tail swings towards the side it's turning to
                        Bumped?.Invoke(tailSpeed, transform.right * Mathf.Sign(yawRate));
                    }
                    yawRate = 0f;
                }
            }
        }

        /// <summary>
        /// How far the hull can travel from a position along a direction before it would get closer to the
        /// seafloor than the clearance or touch rock, and the surface normal there. Already overlapping? Only moves
        /// that don't make it worse.
        /// </summary>
        float FreeDistance(Vector3 from, Vector3 direction, float maxDistance, out Vector3 normal)
        {
            float free = maxDistance;
            normal = -direction;

            // Seafloor: march along the path over the hull's footprint
            float allowedDeficit = Mathf.Max(floorTolerance, FloorDeficit(from, yaw) + 1e-3f);
            for (float d = floorProbeStep; d <= maxDistance + 1e-3f; d += floorProbeStep)
            {
                if (FloorDeficit(from + direction * d, yaw) > allowedDeficit)
                {
                    free = Mathf.Max(0f, d - floorProbeStep);
                    normal = FloorNormal(from + direction * free);
                    break;
                }
            }

            // Rock: sweep each hull sphere
            if (rockMask != 0)
            {
                var offset = from - (transform.position - lastSway);
                foreach (var sphere in hullSpheres)
                {
                    var centre = transform.TransformPoint(sphere) + offset;
                    float radius = sphere.w * transform.lossyScale.x;

                    int overlaps = Physics.OverlapSphereNonAlloc(centre, radius, rockOverlaps, rockMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < overlaps; i++)
                    {
                        var closest = rockOverlaps[i].ClosestPoint(centre);
                        var away = centre - closest;
                        var outward = away.sqrMagnitude > 1e-8f ? away.normalized : (centre - rockOverlaps[i].bounds.center).normalized;
                        if (Vector3.Dot(direction, outward) < 0f)
                        {
                            normal = outward;
                            return 0f;
                        }
                    }

                    int hits = Physics.SphereCastNonAlloc(centre, radius, direction, rockHits, free, rockMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hits; i++)
                    {
                        float distance = Mathf.Max(0f, rockHits[i].distance - rockClearance);
                        if (rockHits[i].distance > 0f && distance < free)
                        {
                            free = distance;
                            normal = rockHits[i].normal;
                        }
                    }
                }
            }
            return free;
        }

        /// <summary>Normal of the seafloor limit surface (the height the hull may not go below) at a position.</summary>
        Vector3 FloorNormal(Vector3 at)
        {
            const float h = 0.75f;
            float dx = FloorLimit(at + Vector3.right * h, yaw) - FloorLimit(at - Vector3.right * h, yaw);
            float dz = FloorLimit(at + Vector3.forward * h, yaw) - FloorLimit(at - Vector3.forward * h, yaw);
            return new Vector3(-dx, 2f * h, -dz).normalized;
        }

        /// <summary>How far (m) the hull's reference point is below where the seafloor allows it (> 0 = too low).</summary>
        float FloorDeficit(Vector3 at, float atYaw) => FloorLimit(at, atYaw) - at.y;

        /// <summary>Total overlap (m) of the hull spheres with rock, with the hull turned about a pivot.</summary>
        float RockPenetration(Quaternion turn, Vector3 pivot)
        {
            if (rockMask == 0)
                return 0f;
            float total = 0f;
            foreach (var sphere in hullSpheres)
            {
                var centre = pivot + turn * (transform.TransformPoint(sphere) - lastSway - pivot);
                float radius = sphere.w * transform.lossyScale.x;
                int overlaps = Physics.OverlapSphereNonAlloc(centre, radius, rockOverlaps, rockMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < overlaps; i++)
                {
                    var closest = rockOverlaps[i].ClosestPoint(centre);
                    float distance = Vector3.Distance(centre, closest);
                    total += distance > 1e-4f ? radius - distance : radius;
                }
            }
            return total;
        }

        void Shake(float impactSpeed)
        {
            float amplitude = Mathf.Min(maxShake, impactSpeed * shakePerSpeed);
            if (shakeTime > 0f && amplitude < shakeAmplitude * (shakeTime / shakeDuration))
                return;
            shakeAmplitude = amplitude;
            shakeTime = shakeDuration;
            shakeSeed = UnityEngine.Random.value * 100f;
        }

        /// <summary>
        /// Advances the hull and moves the camera rig rigidly along with it. The rotation pivots around the
        /// viewer, so turning feels like turning on the spot rather than being swung around the hull's origin.
        /// </summary>
        void Move(float dt)
        {
            yaw += yawRate * dt;

            // Nose up when climbing, bank into turns
            float verticalMax = Mathf.Max(0.01f, maxSpeed * verticalSpeedScale);
            float targetPitch = -Mathf.Clamp(velocity.y / verticalMax, -1f, 1f) * maxPitchTilt;
            float targetRoll = -Mathf.Clamp(yawRate / Mathf.Max(0.01f, maxTurnRate), -1f, 1f) * maxBank;
            float k = Response(dt, tiltResponseTime);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            roll = Mathf.Lerp(roll, targetRoll, k);

            float phase = Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, swayPeriod);
            var sway = Vector3.up * (Mathf.Sin(phase) * swayHeight);

            // Impact shake rides on the sway offset (relative per frame, so it never accumulates)
            if (shakeTime > 0f)
            {
                shakeTime = Mathf.Max(0f, shakeTime - dt);
                float envelope = shakeTime / Mathf.Max(0.01f, shakeDuration);
                float t = Time.time * shakeFrequency;
                var jitter = new Vector3(Mathf.PerlinNoise(t, shakeSeed) - 0.5f, Mathf.PerlinNoise(shakeSeed, t) - 0.5f,
                    Mathf.PerlinNoise(t + 31f, shakeSeed + 7f) - 0.5f) * 2f;
                sway += jitter * (shakeAmplitude * envelope * envelope);
            }
            // Directional jolt: a push along the direction that wobbles back to rest
            if (joltTime > 0f)
            {
                joltTime = Mathf.Max(0f, joltTime - dt);
                float age = joltDuration - joltTime;
                float envelope = joltTime / joltDuration;
                sway += joltDirection * (joltAmplitude * envelope * envelope * Mathf.Cos(age * 15f) * Mathf.Clamp01(age * 40f));
            }
            var rotation = Quaternion.Euler(pitch, yaw, roll + Mathf.Sin(phase * 0.71f + 1.3f) * swayRoll);

            var oldPosition = transform.position;
            var oldRotation = transform.rotation;
            var pivot = viewer.position;
            var turn = rotation * Quaternion.Inverse(oldRotation);
            var newPosition = pivot + turn * (oldPosition - pivot) + (velocity + current) * dt + (sway - lastSway);
            lastSway = sway;
            transform.SetPositionAndRotation(newPosition, rotation);

            var delta = Matrix4x4.TRS(newPosition, rotation, Vector3.one) * Matrix4x4.TRS(oldPosition, oldRotation, Vector3.one).inverse;
            rig.SetPositionAndRotation(delta.MultiplyPoint3x4(rig.position), delta.rotation * rig.rotation);
            if (blocker != null)
                blocker.Carry(delta);

            // Moved colliders must be current for the wall blocker and taps this frame
            Physics.SyncTransforms();
        }

        /// <summary>Lowest the hull's reference point may go at a position, over the colliding (front) part's footprint.</summary>
        float FloorLimit(Vector3 at, float atYaw)
        {
            var rotation = Quaternion.Euler(0f, atYaw, 0f);
            var b = collisionBounds;
            float limit = float.MinValue;
            var c = b.center;
            Span<Vector3> footprint = stackalloc Vector3[]
            {
                new(c.x, 0f, b.max.z), new(c.x, 0f, b.min.z), new(c.x, 0f, c.z),
                new(b.min.x, 0f, c.z), new(b.max.x, 0f, c.z),
            };
            foreach (var p in footprint)
            {
                float ground = world.SampleSeafloorHeight(at + rotation * p);
                limit = Mathf.Max(limit, ground + floorClearance - b.min.y);
            }
            return limit;
        }

        /// <summary>Hull extents around this transform (unrotated, world units), from its renderers.</summary>
        Bounds MeasureHull()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            // Rotation-aligned but in world units, so the extents follow the submarine's scale
            var toLocal = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var lb = filter.sharedMesh.bounds;
                var toHull = toLocal * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toHull.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            return bounds;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            foreach (var sphere in hullSpheres)
                Gizmos.DrawWireSphere(transform.TransformPoint(sphere), sphere.w * transform.lossyScale.x);
        }

        static float Response(float dt, float timeConstant) => 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, timeConstant));

        static float Smooth01(float x) => x * x * (3f - 2f * x);
    }
}
