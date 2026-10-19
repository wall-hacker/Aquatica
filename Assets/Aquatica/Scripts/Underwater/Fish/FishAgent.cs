using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    /// <summary>
    /// One fish: steers towards its slot in its <see cref="FishSchool"/>, keeps clear of its neighbours, the
    /// seafloor, rock and the submarine, scatters ("scuttles") away from a fast or very close submarine and picks
    /// the matching animation. Driven by <see cref="FishPopulation"/>, which also pools it.
    /// </summary>
    [RequireComponent(typeof(FishModel))]
    public class FishAgent : MonoBehaviour
    {
        const float FleeTriggerSpeed = 0.9f;
        const float TouchDistance = 1.2f;
        const float MinFleeTime = 2.5f;
        const float RockProbeInterval = 0.25f;
        const float MaxCruisePitch = 30f;
        const float FeedPitch = 42f;
        const float CuriousAngle = 20f; // how far (degrees) a fish passing the window turns towards the viewer

        static readonly Collider[] Overlaps = new Collider[4];

        FishModel model;
        FishSchool school;
        float length;
        Vector3 velocity;
        float yawRate;
        float lastYaw;
        bool fleeing;
        float fleeTime;
        Vector3 fleeDirection;
        float rockTimer;
        Vector3 rockAvoidance;
        float wanderTimer;

        public GameObject Prefab { get; set; }
        public bool IsFleeing => fleeing;
        /// <summary>Unit-ish random offset of this fish's place in the school.</summary>
        public Vector3 SlotOffset { get; private set; }
        /// <summary>Small personal drift around the slot (feeding hops, hovering), re-picked every few seconds.</summary>
        public Vector3 Wander { get; private set; }
        public float SlotDistance { get; private set; }

        void Awake() => model = GetComponent<FishModel>();

        public void Begin(FishSchool owner, Vector3 position, Quaternion heading, float bodyLength)
        {
            school = owner;
            length = bodyLength;
            transform.SetPositionAndRotation(position, heading);
            transform.localScale = Vector3.one * (bodyLength / Mathf.Max(0.01f, model.Length));
            SlotOffset = Random.insideUnitSphere;
            Wander = Vector3.zero;
            wanderTimer = Random.Range(0f, 3f);
            velocity = heading * Vector3.forward * owner.CruiseSpeed * 0.5f;
            lastYaw = heading.eulerAngles.y;
            yawRate = 0f;
            fleeing = false;
            rockTimer = Random.Range(0f, RockProbeInterval);
            rockAvoidance = Vector3.zero;
            model.ResetAnimation();
        }

        public void Tick(float dt)
        {
            var population = school.Population;
            var species = school.Species;
            var p = transform.position;

            UpdateThreat(population, species, p, dt);

            wanderTimer -= dt;
            if (wanderTimer <= 0f)
            {
                wanderTimer = Random.Range(2f, 4f);
                var w = Random.insideUnitSphere;
                Wander = new Vector3(w.x, w.y * 0.3f, w.z) * 0.8f;
            }

            // ---- what this fish wants to do
            Vector3 desired;
            float accel, turnRate;
            var anim = FishAnimation.Swim;
            float animSpeed = 1f;
            Vector3? face = null;
            bool feeding = false;
            float cruise = school.CruiseSpeed;

            if (fleeing)
            {
                desired = fleeDirection * species.fleeSpeed;
                accel = 9f;
                turnRate = 420f;
                anim = FishAnimation.Scuttle;
                animSpeed = Mathf.Clamp(velocity.magnitude / species.fleeSpeed, 0.6f, 1.3f);
                SlotDistance = float.MaxValue;
            }
            else
            {
                var slot = school.SlotPosition(this);
                if (school.Plan == FishPlan.Feed)
                    slot.y += length * 0.45f;
                var toSlot = slot - p;
                SlotDistance = toSlot.magnitude;

                if (school.Plan == FishPlan.Cruise)
                {
                    desired = Vector3.ClampMagnitude(school.LeaderVelocity + toSlot * 0.8f, cruise * 2.2f);
                    accel = 1.6f;
                    turnRate = 140f;
                }
                else
                {
                    desired = Vector3.ClampMagnitude(toSlot * 0.8f, cruise * 1.5f);
                    accel = 1.2f;
                    turnRate = 90f;
                    bool arrived = SlotDistance < Mathf.Max(0.35f, length * 2f);
                    // Near the window, curious fish already turn side-on while they settle into their pass
                    if (school.Plan == FishPlan.Curious && (school.Target - p).sqrMagnitude < 2.5f * 2.5f)
                        arrived = true;
                    if (arrived)
                    {
                        switch (school.Plan)
                        {
                            case FishPlan.Feed:
                                anim = FishAnimation.Feed;
                                feeding = true;
                                face = school.Heading + Wander * 0.5f;
                                break;
                            case FishPlan.Curious:
                                // Side-on to the viewer, travelling along the glass and angled a little towards it
                                school.WindowPass(out float passDirection);
                                var along = population.Sub.right * passDirection;
                                var toViewer = Vector3.ProjectOnPlane(population.Viewer.position - p, Vector3.up).normalized;
                                face = along * Mathf.Cos(CuriousAngle * Mathf.Deg2Rad) + toViewer * Mathf.Sin(CuriousAngle * Mathf.Deg2Rad);
                                bool cruising = velocity.magnitude > 0.12f;
                                anim = cruising ? FishAnimation.Swim : FishAnimation.Idle;
                                animSpeed = cruising ? Mathf.Clamp(velocity.magnitude / Mathf.Max(0.05f, cruise), 0.6f, 1.2f) : 1f;
                                break;
                            case FishPlan.Shelter:
                                anim = FishAnimation.Idle;
                                face = school.ShelterOutward;
                                break;
                            default:
                                anim = FishAnimation.Idle;
                                face = school.Heading;
                                break;
                        }
                    }
                }

                // Keep apart from school mates
                float separation = length * 1.6f;
                foreach (var other in school.Members)
                {
                    if (other == this)
                        continue;
                    var away = p - other.transform.position;
                    float d = away.magnitude;
                    if (d < separation && d > 1e-4f)
                        desired += away / d * (cruise * (1f - d / separation) * 1.5f);
                }

                if (anim == FishAnimation.Swim)
                    animSpeed = Mathf.Clamp(velocity.magnitude / Mathf.Max(0.05f, cruise), 0.6f, 1.8f);
            }

            desired += Avoidance(population, p, dt, Mathf.Max(cruise, desired.magnitude));

            // ---- move
            velocity = Vector3.MoveTowards(velocity, desired, accel * dt);
            p += velocity * dt;

            // Hard limits: never into the seafloor or the hull
            float floor = population.FloorHeight(p) + length * 0.3f;
            if (p.y < floor)
            {
                p.y = floor;
                velocity.y = Mathf.Max(velocity.y, 0f);
            }
            float hull = population.HullDistance(p, out var hullNormal) - length * 0.5f;
            if (hull < 0f)
            {
                p -= hullNormal * hull;
                velocity -= hullNormal * Mathf.Min(0f, Vector3.Dot(velocity, hullNormal));
            }
            transform.position = p;

            Orient(face, feeding, turnRate, dt);
            model.Play(anim, animSpeed);
        }

        void UpdateThreat(FishPopulation population, FishSpecies species, Vector3 p, float dt)
        {
            float hull = population.HullDistance(p, out var away);
            float subSpeed = population.SubSpeed;
            float scareRadius = species.skittishness * (2f + subSpeed * 3.5f) + length;
            bool threatened = hull < scareRadius && (subSpeed > FleeTriggerSpeed || hull < TouchDistance);

            if (threatened && !fleeing)
            {
                fleeing = true;
                fleeTime = 0f;
                fleeDirection = FleeDirection(population, p, away);
                school.NotifyScared();
            }
            if (!fleeing)
                return;

            fleeTime += dt;
            if (threatened)
                fleeDirection = Vector3.Slerp(fleeDirection, FleeDirection(population, p, away), 1f - Mathf.Exp(-dt * 3f));
            if (fleeTime > MinFleeTime && hull > scareRadius * 1.5f)
                fleeing = false;
        }

        /// <summary>Away from the hull and sideways out of the submarine's path (fish can't outswim it head-on).</summary>
        Vector3 FleeDirection(FishPopulation population, Vector3 p, Vector3 awayFromHull)
        {
            var dir = awayFromHull;
            var subVelocity = population.SubVelocity;
            if (subVelocity.sqrMagnitude > 0.01f)
            {
                var along = subVelocity.normalized;
                var relative = p - population.Sub.position;
                var lateral = relative - along * Vector3.Dot(relative, along);
                if (lateral.sqrMagnitude > 1e-4f)
                    dir = awayFromHull * 0.6f + lateral.normalized;
            }
            dir += Random.insideUnitSphere * 0.25f;
            dir.y *= 0.35f;
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
        }

        /// <summary>Steering away from the seafloor, the surface, the area edge, the hull and rock ahead.</summary>
        Vector3 Avoidance(FishPopulation population, Vector3 p, float dt, float speed)
        {
            var push = Vector3.zero;

            float ahead = population.FloorHeight(p + velocity * 0.6f);
            float wanted = Mathf.Max(ahead, population.FloorHeight(p)) + length * 0.45f + 0.25f;
            if (p.y < wanted && school.Plan != FishPlan.Feed)
                push.y += (wanted - p.y) * 2f;
            if (p.y > population.CeilingHeight)
                push.y -= (p.y - population.CeilingHeight) * 2f;

            push += population.AreaPush(p) * speed * 2f;

            float hull = population.HullDistance(p, out var hullNormal);
            if (hull < 1.5f)
                push += hullNormal * ((1.5f - hull) * 3f);

            rockTimer -= dt;
            if (rockTimer <= 0f && population.TerrainMask != 0)
            {
                rockTimer = RockProbeInterval;
                rockAvoidance = Vector3.zero;
                float radius = Mathf.Max(0.1f, length * 0.5f);
                int inside = Physics.OverlapSphereNonAlloc(p, radius, Overlaps, population.TerrainMask, QueryTriggerInteraction.Ignore);
                if (inside > 0)
                {
                    var outward = p - Overlaps[0].ClosestPoint(p);
                    if (outward.sqrMagnitude < 1e-6f)
                        outward = p - Overlaps[0].bounds.center;
                    rockAvoidance = outward.normalized * 2f;
                }
                else if (velocity.sqrMagnitude > 1e-4f)
                {
                    float probe = velocity.magnitude * 1.5f + length + 0.5f;
                    if (Physics.SphereCast(p, radius, velocity.normalized, out var hit, probe, population.TerrainMask, QueryTriggerInteraction.Ignore))
                    {
                        // Slide along the rock: drop the part of the motion into it, push off a little
                        float urgency = 1f - hit.distance / probe;
                        rockAvoidance = (hit.normal - velocity.normalized * Mathf.Max(0f, -Vector3.Dot(velocity.normalized, hit.normal))) * urgency * 1.5f;
                    }
                }
            }
            push += rockAvoidance * speed;
            return push;
        }

        void Orient(Vector3? face, bool feeding, float turnRate, float dt)
        {
            var forward = face ?? velocity;
            if (forward.sqrMagnitude < (0.05f * length + 0.02f) * (0.05f * length + 0.02f))
                forward = transform.forward;
            var flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward;

            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float pitch = feeding ? FeedPitch : Mathf.Clamp(-Mathf.Atan2(forward.y, flat.magnitude) * Mathf.Rad2Deg, -MaxCruisePitch, MaxCruisePitch);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, transform.eulerAngles.y) / dt, 1f - Mathf.Exp(-dt * 4f));
            lastYaw = transform.eulerAngles.y;
            float roll = Mathf.Clamp(-yawRate * 0.12f, -25f, 25f);

            var target = Quaternion.Euler(pitch, yaw, roll);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnRate * dt);
        }
    }
}
