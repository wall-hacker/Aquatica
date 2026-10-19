using UnityEngine;

namespace Aquatica.Navigation
{
    /// <summary>
    /// Shows the currents at the edge of the navigable area: streaks of rushing water and tumbling bubbles flowing
    /// the way the current pushes. They only appear in the band near the (invisible) limit, thickening towards
    /// it, so there is no wall, just water that is clearly moving. Particles are spawned around the viewer and
    /// never on a path through the hull, so nothing streams through the cabin.
    /// </summary>
    public class CurrentEffects : MonoBehaviour
    {
        [SerializeField] SubmarineMotor motor;
        [SerializeField] Material streakMaterial;
        [SerializeField] Material bubbleMaterial;

        [Header("Where")]
        [Tooltip("Particles are spawned within this distance of the viewer (m).")]
        [SerializeField] float spawnRadius = 28f;
        [Tooltip("How far before the current zone (where the push starts) the water starts to visibly move (m).")]
        [SerializeField] float previewBand = 0f;
        [Tooltip("Extra margin (m) kept between particle paths and the hull.")]
        [SerializeField] float hullMargin = 1.5f;

        [Header("Streaks")]
        [SerializeField] float streakRate = 260f;
        [SerializeField] float streakSpeed = 9f;
        [SerializeField] Vector2 streakLifetime = new(1.2f, 2f);
        [SerializeField] Vector2 streakWidth = new(0.04f, 0.1f);
        [SerializeField] Color streakColor = new(0.55f, 0.85f, 1f, 0.45f);

        [Header("Bubbles")]
        [SerializeField] float bubbleRate = 70f;
        [SerializeField] float bubbleSpeed = 4.5f;
        [SerializeField] Vector2 bubbleLifetime = new(2f, 3.5f);
        [SerializeField] Vector2 bubbleSize = new(0.06f, 0.22f);
        [SerializeField] Color bubbleColor = new(0.75f, 0.95f, 1f, 0.7f);

        ParticleSystem streaks;
        ParticleSystem bubbles;
        Transform viewer;
        float streakBudget;
        float bubbleBudget;

        void Awake()
        {
            streaks = CreateSystem("CurrentStreaks", streakMaterial, true);
            bubbles = CreateSystem("CurrentBubbles", bubbleMaterial, false);
        }

        void Update()
        {
            if (motor == null || !motor.IsReady)
                return;
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null)
                    return;
                viewer = cam.transform;
            }

            // Nothing near enough to the limit: skip the per-particle work entirely
            motor.CurrentAt(viewer.position, out float near, previewBand + spawnRadius);
            if (near <= 0f)
            {
                streakBudget = bubbleBudget = 0f;
                return;
            }

            float dt = Time.deltaTime;
            streakBudget += streakRate * dt;
            bubbleBudget += bubbleRate * dt;
            for (; streakBudget >= 1f; streakBudget -= 1f)
                TryEmit(streaks, true);
            for (; bubbleBudget >= 1f; bubbleBudget -= 1f)
                TryEmit(bubbles, false);
        }

        /// <summary>Picks a random spot around the viewer; emits there if it lies in the current band.</summary>
        void TryEmit(ParticleSystem system, bool streak)
        {
            var offset = Random.insideUnitSphere * spawnRadius;
            offset.y *= 0.6f;
            var position = viewer.position + offset;

            var direction = motor.CurrentAt(position, out float strength, previewBand);
            if (strength <= 0f || Random.value > Mathf.Clamp01(strength * 1.3f))
                return;

            float speed = (streak ? streakSpeed : bubbleSpeed) * Random.Range(0.7f, 1.3f) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(strength));
            var velocity = direction * speed + Random.insideUnitSphere * (streak ? 0.4f : 0.8f);
            if (!streak)
                velocity += Vector3.up * 0.6f;
            float lifetime = streak ? Random.Range(streakLifetime.x, streakLifetime.y) : Random.Range(bubbleLifetime.x, bubbleLifetime.y);

            if (CrossesHull(position, position + velocity * lifetime))
                return;

            var color = streak ? streakColor : bubbleColor;
            color.a *= Mathf.Clamp01(0.35f + strength);
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startLifetime = lifetime,
                startSize = streak ? Random.Range(streakWidth.x, streakWidth.y) : Random.Range(bubbleSize.x, bubbleSize.y),
                startColor = color,
                applyShapeToPosition = false,
            };
            system.Emit(emit, 1);
        }

        /// <summary>Segment against the hull's box (slab test in the hull's frame).</summary>
        bool CrossesHull(Vector3 from, Vector3 to)
        {
            var hull = motor.HullBounds;
            hull.Expand(hullMargin * 2f);
            var toHull = Matrix4x4.TRS(motor.transform.position, motor.transform.rotation, Vector3.one).inverse;
            var a = toHull.MultiplyPoint3x4(from);
            var d = toHull.MultiplyPoint3x4(to) - a;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < 1e-6f)
                {
                    if (a[axis] < hull.min[axis] || a[axis] > hull.max[axis])
                        return false;
                    continue;
                }
                float t1 = (hull.min[axis] - a[axis]) / d[axis];
                float t2 = (hull.max[axis] - a[axis]) / d[axis];
                tMin = Mathf.Max(tMin, Mathf.Min(t1, t2));
                tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
                if (tMin > tMax)
                    return false;
            }
            return true;
        }

        ParticleSystem CreateSystem(string systemName, Material material, bool streak)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = streak ? 700 : 300;
            main.scalingMode = ParticleSystemScalingMode.Shape; // sizes in metres, whatever the submarine's scale

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            // Fade in and out over each particle's life
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            if (!streak)
            {
                // Bubbles tumble and wobble
                var noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.6f;
                noise.scrollSpeed = 0.4f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (streak)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.22f;
                renderer.lengthScale = 2f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            system.Play();
            return system;
        }
    }
}
