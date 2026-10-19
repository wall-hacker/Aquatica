using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    public enum FishPlan
    {
        Cruise,   // swim between waypoints around the group's home
        Idle,     // hover in place
        Feed,     // graze / peck at the seafloor
        Curious,  // come and look in through the cockpit window
        Shelter,  // hide next to a rock after a scare
    }

    /// <summary>
    /// A group of fish of one species (a solo fish is a group of one, a pair a group of two). The group decides
    /// what to do next and moves a virtual leader; members steer towards their own slot around it (see
    /// <see cref="FishAgent"/>), which keeps schools together and lets them regroup after the submarine has
    /// scattered them.
    /// </summary>
    public class FishSchool
    {
        const float HomeRange = 22f;
        const float LeaderAcceleration = 0.8f;
        const float CuriosityCheckInterval = 6f;
        const float ShelterSearchRange = 30f;
        const float PassPeriod = 14f;      // s for a full to-and-fro pass in front of the window
        const float PassAmplitude = 1.1f;  // m either side of the window spot

        readonly List<FishAgent> members = new();
        readonly Vector3 home;
        float planTime;
        int legs;
        float curiousCooldown;
        float travelTime;
        float passTime;
        bool scared;
        Vector3 leaderVelocity;

        public FishPopulation Population { get; }
        public FishSpecies Species { get; }
        public FishPlan Plan { get; private set; }
        public Vector3 Target { get; private set; }
        public Vector3 LeaderPosition { get; private set; }
        public Vector3 LeaderVelocity => leaderVelocity;
        /// <summary>Flat direction the group faces (cruising direction, or where it hovers facing).</summary>
        public Vector3 Heading { get; private set; }
        public Vector3 ShelterOutward { get; private set; }
        public float CruiseSpeed { get; }
        public IReadOnlyList<FishAgent> Members => members;
        public int Count => members.Count;

        /// <summary>Spread (m) of members around the leader / target.</summary>
        public float Spread => Species.social switch
        {
            FishSocial.School => Species.spread,
            FishSocial.Pair => 0.45f,
            _ => 0f,
        };

        Vector3 windowJitter;

        /// <param name="centre">Where the group appears.</param>
        /// <param name="homePosition">Where it settles and roams around (it swims there first if it isn't the centre).</param>
        public FishSchool(FishPopulation population, FishSpecies species, Vector3 centre, Vector3 homePosition)
        {
            Population = population;
            Species = species;
            home = homePosition;
            LeaderPosition = centre;
            Heading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            CruiseSpeed = species.cruiseSpeed * Random.Range(0.9f, 1.1f);
            curiousCooldown = Random.Range(4f, 20f);

            if ((homePosition - centre).sqrMagnitude > 4f)
            {
                Plan = FishPlan.Cruise;
                Target = homePosition;
                legs = 1;
                planTime = 120f;
                var flat = Vector3.ProjectOnPlane(homePosition - centre, Vector3.up);
                if (flat.sqrMagnitude > 1e-4f)
                    Heading = flat.normalized;
            }
            else
            {
                StartPlan(Random.value < 0.6f ? FishPlan.Cruise : FishPlan.Idle);
            }
        }

        public void Add(FishAgent agent) => members.Add(agent);

        public Vector3 Centre
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var m in members)
                    sum += m.transform.position;
                return members.Count > 0 ? sum / members.Count : LeaderPosition;
            }
        }

        /// <summary>A member started fleeing: once everyone has calmed down the group moves on (or hides).</summary>
        public void NotifyScared()
        {
            scared = true;
            if (Plan == FishPlan.Curious)
                curiousCooldown = 60f;
        }

        public void Tick(float dt)
        {
            curiousCooldown -= dt;
            planTime -= dt;
            if (scared && AllCalm())
                AfterScare();
            UpdatePlan();
            MoveLeader(dt);
            for (int i = 0; i < members.Count; i++)
                members[i].Tick(dt);
        }

        bool AllCalm()
        {
            foreach (var m in members)
                if (m.IsFleeing)
                    return false;
            return true;
        }

        void AfterScare()
        {
            scared = false;
            if (Species.social != FishSocial.School)
            {
                // Solo fish and pairs carry on from where they fled to, rather than swimming back
                LeaderPosition = Centre;
                leaderVelocity = Vector3.zero;
                if (Random.value < Species.shelter &&
                    Population.TryFindShelter(LeaderPosition, ShelterSearchRange, out var spot, out var outward))
                {
                    Target = spot;
                    ShelterOutward = outward;
                    Plan = FishPlan.Shelter;
                    planTime = Random.Range(8f, 20f);
                    return;
                }
            }
            StartPlan(FishPlan.Cruise, awayFromSub: true);
        }

        void UpdatePlan()
        {
            switch (Plan)
            {
                case FishPlan.Cruise:
                    if ((Target - LeaderPosition).sqrMagnitude < 2f)
                    {
                        if (--legs > 0)
                            PickWaypoint(false);
                        else
                            NextPlan();
                    }
                    else if (planTime <= 0f)
                    {
                        NextPlan();
                    }
                    break;

                case FishPlan.Curious:
                    Target = Population.WindowPoint(windowJitter);
                    // The visit only counts down once the group is at the window; the trip there may take a while
                    bool atWindow = (Target - LeaderPosition).sqrMagnitude < 9f;
                    if (!atWindow)
                        planTime += Time.deltaTime * (travelTime < 90f ? 1f : 0f);
                    else
                        passTime += Time.deltaTime;
                    travelTime += Time.deltaTime;
                    if (planTime <= 0f || Population.SubSpeed > 0.6f)
                    {
                        curiousCooldown = Random.Range(40f, 80f);
                        StartPlan(FishPlan.Cruise, awayFromSub: true);
                    }
                    break;

                default:
                    if (planTime <= 0f)
                        NextPlan();
                    break;
            }

            // Fish near a still submarine sometimes come over to look in
            if (Plan is FishPlan.Cruise or FishPlan.Idle && curiousCooldown <= 0f)
            {
                curiousCooldown = CuriosityCheckInterval;
                float distance = Vector3.Distance(LeaderPosition, Population.Viewer.position);
                if (distance < Population.HiddenDistance * 0.8f && Random.value < Species.curiosity * 0.5f && Population.CanBecomeCurious())
                    StartPlan(FishPlan.Curious);
            }
        }

        void NextPlan()
        {
            float cruise = 0.45f, idle = 0.25f, feed = Species.feeding * 0.6f;
            float roll = Random.value * (cruise + idle + feed);
            StartPlan(roll < cruise ? FishPlan.Cruise : roll < cruise + idle ? FishPlan.Idle : FishPlan.Feed);
        }

        void StartPlan(FishPlan plan, bool awayFromSub = false)
        {
            Plan = plan;
            switch (plan)
            {
                case FishPlan.Cruise:
                    legs = Random.Range(1, 4);
                    planTime = 45f;
                    PickWaypoint(awayFromSub);
                    break;
                case FishPlan.Idle:
                    Target = ClampHeight(LeaderPosition);
                    planTime = Random.Range(6f, 15f);
                    break;
                case FishPlan.Feed:
                    var spot = LeaderPosition + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, 6f);
                    spot.y = Population.FloorHeight(spot);
                    if (!Population.PathClear(LeaderPosition, spot + Vector3.up * 0.5f, 0.3f))
                    {
                        StartPlan(FishPlan.Idle);
                        return;
                    }
                    Target = spot;
                    planTime = Random.Range(10f, 25f);
                    break;
                case FishPlan.Curious:
                    // Off to one side of the canopy's centre strut, a little above eye level
                    windowJitter = new Vector3((Random.value < 0.5f ? -1f : 1f) * Random.Range(0.7f, 1.8f), Random.Range(-0.1f, 0.7f), Random.value);
                    Target = Population.WindowPoint(windowJitter);
                    planTime = Random.Range(12f, 22f); // long enough for a pass or two across the window
                    travelTime = 0f;
                    passTime = Random.Range(0f, PassPeriod);
                    break;
                case FishPlan.Shelter:
                    planTime = Random.Range(8f, 20f);
                    break;
            }
        }

        /// <summary>Next cruising waypoint around home, with a clear path; optionally the one furthest from the submarine.</summary>
        void PickWaypoint(bool awayFromSub)
        {
            var subPosition = Population.Sub.position;
            float bestScore = float.MinValue;
            var best = Vector3.zero;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var circle = Random.insideUnitCircle * HomeRange;
                var p = home + new Vector3(circle.x, 0f, circle.y);
                if ((p - LeaderPosition).sqrMagnitude < 25f || !Population.InArea(p, 0f))
                    continue;
                p = ClampHeight(new Vector3(p.x, Population.FloorHeight(p) + Random.Range(Species.heightAboveFloor.x, Species.heightAboveFloor.y), p.z));
                if (Population.HullDistance(p, out _) < 5f || !Population.PathClear(LeaderPosition, p, 0.5f + Spread * 0.5f))
                    continue;
                float score = awayFromSub ? (p - subPosition).sqrMagnitude : Random.value;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    if (!awayFromSub)
                        break;
                }
            }

            if (bestScore == float.MinValue)
            {
                Plan = FishPlan.Idle;
                Target = ClampHeight(LeaderPosition);
                planTime = Random.Range(3f, 6f);
                return;
            }
            Target = best;
        }

        Vector3 ClampHeight(Vector3 p)
        {
            float floor = Population.FloorHeight(p);
            p.y = Mathf.Clamp(p.y, floor + Species.heightAboveFloor.x, Mathf.Min(floor + Species.heightAboveFloor.y, Population.CeilingHeight));
            return p;
        }

        void MoveLeader(float dt)
        {
            var aim = Plan == FishPlan.Feed ? Target + Vector3.up * 0.6f : Target;
            var toTarget = aim - LeaderPosition;
            float distance = toTarget.magnitude;
            float speed = Plan == FishPlan.Cruise ? CruiseSpeed : Mathf.Min(CruiseSpeed * 1.2f, distance * 0.6f);
            var desired = distance > 1e-3f ? toTarget / distance * speed : Vector3.zero;

            // Wait for stragglers (e.g. members regrouping after a scare)
            int behind = 0;
            foreach (var m in members)
                if (m.IsFleeing || m.SlotDistance > Spread * 2f + 2f)
                    behind++;
            if (members.Count > 1 && behind * 3 > members.Count)
                desired *= 0.35f;

            // The whole group veers away from an approaching submarine (curious fish only keep a small gap)
            float hull = Population.HullDistance(LeaderPosition, out var away);
            float avoid = Plan == FishPlan.Curious ? 1.5f : Species.skittishness * (3f + Population.SubSpeed * 2.5f) + Spread;
            if (hull < avoid)
                desired += away * (CruiseSpeed * 2f * (1f - hull / avoid));

            // A target on the far side of the hull: swim around it instead of pressing against it
            if (hull < 4f && Vector3.Dot(toTarget, away) < 0f)
            {
                var around = Vector3.ProjectOnPlane(toTarget, away);
                if (around.sqrMagnitude < 1e-4f)
                    around = Vector3.Cross(away, Vector3.up);
                desired = Vector3.ProjectOnPlane(desired, away) + around.normalized * CruiseSpeed + away * (CruiseSpeed * 0.3f);
            }
            desired += Population.AreaPush(LeaderPosition) * CruiseSpeed * 2f;

            leaderVelocity = Vector3.MoveTowards(leaderVelocity, desired, LeaderAcceleration * dt);
            var p = LeaderPosition + leaderVelocity * dt;
            float floor = Population.FloorHeight(p);
            p.y = Mathf.Clamp(p.y, floor + Mathf.Min(0.3f, Species.heightAboveFloor.x), Population.CeilingHeight);
            // Never through the hull
            float inside = Population.HullDistance(p, out var outward) - 0.3f;
            if (inside < 0f)
                p -= outward * inside;
            LeaderPosition = p;

            var flat = new Vector3(leaderVelocity.x, 0f, leaderVelocity.z);
            if (Plan == FishPlan.Cruise && flat.sqrMagnitude > 0.0025f)
                Heading = Vector3.Slerp(Heading, flat.normalized, 1f - Mathf.Exp(-dt * 2f));
        }

        /// <summary>
        /// Curious fish don't stare head-on into the cockpit (seen nose-on these thin fish are just a line): they
        /// cruise slowly to and fro across the window, side-on to the viewer. Returns the sideways offset (m, along
        /// the hull's right) and the direction of travel (+1 = towards the hull's right).
        /// </summary>
        public float WindowPass(out float direction)
        {
            float amplitude = PassAmplitude + Spread * 0.3f;
            // Slow swimmers make slower passes, so they glide across rather than chase their spot
            float period = Mathf.Max(PassPeriod, Mathf.PI * 2f * amplitude / Mathf.Max(0.05f, CruiseSpeed * 0.6f));
            float phase = passTime * Mathf.PI * 2f / period;
            direction = Mathf.Cos(phase) >= 0f ? 1f : -1f;
            return Mathf.Sin(phase) * amplitude;
        }

        /// <summary>Where a member should be: its slot in the moving school, or its spot around the group's target.</summary>
        public Vector3 SlotPosition(FishAgent agent)
        {
            var offset = agent.SlotOffset * Spread;
            switch (Plan)
            {
                case FishPlan.Cruise:
                    var rotation = Quaternion.LookRotation(Heading);
                    return LeaderPosition + rotation * new Vector3(offset.x, offset.y * 0.5f, offset.z * 1.4f);
                case FishPlan.Feed:
                    var p = Target + new Vector3(offset.x, 0f, offset.z) * 1.3f + agent.Wander;
                    p.y = Population.FloorHeight(p);
                    return p;
                case FishPlan.Curious:
                    return Target + Vector3.ClampMagnitude(offset, 0.9f) + agent.Wander * 0.3f + Population.Sub.right * WindowPass(out _);
                case FishPlan.Shelter:
                    return Target + offset * 0.5f;
                default:
                    return Target + offset + agent.Wander * 0.5f;
            }
        }
    }
}
