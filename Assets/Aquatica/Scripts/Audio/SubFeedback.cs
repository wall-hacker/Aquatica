using Aquatica.Cockpit;
using Aquatica.Navigation;
using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// Physical feedback for the hull and for faults.
    /// <para>
    /// Brushing ground or rock: faint vibration ticks while the hull slides along something, a light pulse on a
    /// real bump.
    /// </para>
    /// <para>
    /// Faults (a phone has a single vibration motor, so direction comes from the other senses and from how the
    /// pulses change): when a fault breaks out the cabin is jolted away from the failing device, a boom rings out
    /// from it and the phone gives a hard double pulse. While it stays broken, a pulse comes every couple of seconds
    /// with a small knock from that side, stronger the more directly the player faces the device, like a "warmer,
    /// colder" guide towards it. The pulses ease off while the player is working on the device.
    /// </para>
    /// </summary>
    public class SubFeedback : MonoBehaviour
    {
        [Header("Hull contact")]
        [Tooltip("Sliding speed (m/s) below which scraping is not felt.")]
        [SerializeField] float minScrapeSpeed = 0.15f;
        [SerializeField] float scrapeStrength = 0.1f;
        [SerializeField] Vector2 scrapeInterval = new(0.09f, 0.2f);
        [Tooltip("Vibration strength per m/s of impact.")]
        [SerializeField] float bumpStrengthPerSpeed = 0.12f;
        [SerializeField] float maxBumpStrength = 0.45f;

        [Header("Fault breaking out")]
        [SerializeField] float faultJolt = 0.07f;
        [SerializeField] float faultHitVolume = 0.9f;

        [Header("Fault ongoing")]
        [SerializeField] float pulseInterval = 2.2f;
        [Tooltip("Pulse strength when facing away from / straight at the faulty device.")]
        [SerializeField] Vector2 pulseStrength = new(0.3f, 0.85f);
        [SerializeField] float pulseJolt = 0.018f;
        [Tooltip("Pulse strength multiplier while the player is working on the device.")]
        [SerializeField] float workingScale = 0.3f;

        static readonly float[] faultPattern = { 0.16f, 0.07f, 0.32f };
        static readonly float[] faultStrengths = { 1f, 0.75f };

        SubmarineMotor motor;
        CockpitSystems systems;
        float scrapeTimer;
        float pulseTimer;

        void Start()
        {
            motor = GetComponentInChildren<SubmarineMotor>();
            systems = GetComponentInChildren<CockpitSystems>();
            if (motor != null)
                motor.Bumped += OnBumped;
            if (systems != null)
                systems.FaultStarted += OnFaultStarted;
        }

        void OnDestroy()
        {
            if (motor != null)
                motor.Bumped -= OnBumped;
            if (systems != null)
                systems.FaultStarted -= OnFaultStarted;
            Haptics.Cancel();
        }

        void OnBumped(float impact, Vector3 direction) =>
            Haptics.Pulse(Mathf.Min(maxBumpStrength, 0.12f + impact * bumpStrengthPerSpeed), 0.04f + Mathf.Min(impact, 2f) * 0.025f);

        void OnFaultStarted(CockpitDevice device)
        {
            var from = DirectionTo(device);
            if (motor != null)
                motor.Jolt(-from, faultJolt, 1.1f);
            Sfx.PlayAt(SoundBank.FaultHit, device.Location, faultHitVolume, 0.06f);
            Haptics.Pattern(faultPattern, faultStrengths);
            pulseTimer = pulseInterval;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateScrape(dt);
            UpdateFaultPulse(dt);
        }

        void UpdateScrape(float dt)
        {
            if (motor == null || motor.ScrapeSpeed < minScrapeSpeed)
            {
                scrapeTimer = 0f;
                return;
            }
            scrapeTimer -= dt;
            if (scrapeTimer > 0f)
                return;
            float speed = motor.ScrapeSpeed;
            // Faster slides tick faster and a little harder, like dragging over rough rock
            float k = Mathf.Clamp01(speed / 2f);
            scrapeTimer = Mathf.Lerp(scrapeInterval.y, scrapeInterval.x, k) * Random.Range(0.75f, 1.25f);
            Haptics.Pulse(scrapeStrength * (0.6f + 0.6f * k), 0.025f);
        }

        void UpdateFaultPulse(float dt)
        {
            if (systems == null || systems.ActiveFaults <= 0 || systems.AlarmsMuted)
                return;
            pulseTimer -= dt;
            if (pulseTimer > 0f)
                return;
            pulseTimer = pulseInterval * Random.Range(0.9f, 1.1f);

            // The faulty device the player faces most decides the strength
            var cam = Camera.main;
            CockpitDevice nearest = null;
            float bestFacing = -2f;
            foreach (var device in systems.Devices)
            {
                if (!device.IsFaulty)
                    continue;
                float facing = cam != null ? Vector3.Dot(cam.transform.forward, DirectionTo(device)) : 0f;
                if (facing > bestFacing)
                {
                    bestFacing = facing;
                    nearest = device;
                }
            }
            if (nearest == null)
                return;

            float aim = Mathf.InverseLerp(-0.2f, 0.95f, bestFacing);
            float strength = Mathf.Lerp(pulseStrength.x, pulseStrength.y, aim * aim);
            if (nearest.AlarmDucked)
                strength *= workingScale;
            Haptics.Pattern(new[] { 0.06f, 0.08f, 0.1f }, new[] { strength * 0.7f, strength });
            if (motor != null && !nearest.AlarmDucked)
                motor.Jolt(-DirectionTo(nearest), pulseJolt, 0.5f);
        }

        /// <summary>Direction from the player's head to a device (world space).</summary>
        static Vector3 DirectionTo(CockpitDevice device)
        {
            var cam = Camera.main;
            var from = cam != null ? cam.transform.position : device.transform.position + Vector3.back;
            var d = device.Location - from;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }
    }
}
