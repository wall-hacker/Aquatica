using Aquatica.Audio;
using System;
using Aquatica.Expedition;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Handwheel valve on a flanged base, with a needle on the base plate and a wall <see cref="PressureGauge"/> that
    /// both show the line pressure (0-1; the green band is in the middle, red on both sides). Drag around the wheel
    /// to spin it: clockwise pushes the needles clockwise, anticlockwise pushes them back.
    /// Fault: the pressure surges one way, then the other, so the player keeps turning the wheel both ways to hold
    /// the needle in the green. Time in the green fills a hold timer (a ticking that speeds up); time outside drains
    /// it. Once it is full the line is steady again. Harder faults drift faster, change direction more often and need
    /// a longer hold. A status light on the flange is green while all is well, blinks red while the needle is out of
    /// the green and turns amber while holding.
    /// </summary>
    public class ValveStation : CockpitDevice, ICockpitInteractable
    {
        [SerializeField] Transform wheel;
        [SerializeField] Transform needle;
        [SerializeField] PressureGauge gauge;
        [SerializeField] IndicatorLight statusLight;

        [Header("Dial")]
        [Tooltip("Needle angle (local Z) at pressure 0 and 1; the pointer lies along local +X.")]
        [SerializeField] float needleMinAngle = 225f;
        [SerializeField] float needleMaxAngle = -45f;
        [Tooltip("Width of the green band around the middle (fraction of the dial). Must match the dial art.")]
        [SerializeField, Range(0.05f, 0.6f)] float greenBand = 0.24f;
        [SerializeField] float needleSpeed = 12f;

        [Header("Fault (per difficulty 1, 2, 3)")]
        [Tooltip("Wheel turns that move the needle across the whole dial.")]
        [SerializeField] float turnsFullScale = 1.25f;
        [Tooltip("How fast the pressure drifts (dial per second).")]
        [SerializeField] float[] driftSpeed = { 0.055f, 0.08f, 0.11f };
        [Tooltip("Seconds (min, max) before the drift changes direction.")]
        [SerializeField] Vector2[] surgeEvery = { new(2.5f, 5f), new(1.8f, 4f), new(1.2f, 3f) };
        [Tooltip("Seconds in the green needed to repair.")]
        [SerializeField] float[] holdTime = { 5f, 7f, 9f };
        [Tooltip("How fast the hold timer drains while out of the green (relative to filling).")]
        [SerializeField] float holdDrain = 0.5f;
        [SerializeField] float tickVolume = 0.35f;

        /// <summary>Degrees turned this drag step; positive = clockwise as seen by the player.</summary>
        public event Action<float> Turned;

        /// <summary>While the player turns the wheel the klaxon keeps down (the ticking is the feedback that matters).</summary>
        public override bool AlarmDucked => IsFaulty && dragging;

        /// <summary>Line pressure, 0-1 (0.5 is the middle of the green).</summary>
        public float Pressure { get; private set; } = 0.5f;
        /// <summary>Repair progress during a fault, 0-1.</summary>
        public float HoldProgress => IsFaulty ? hold / CurrentHoldTime : 1f;
        public bool InGreen => Mathf.Abs(Pressure - 0.5f) <= greenBand * 0.5f;

        float wheelAngle;
        float lastPointerAngle;
        bool dragging;
        float hold;
        float drift;
        float nextSurge;
        float nextTick;
        float needleAngle;
        float seed;
        AudioSource audioSource;

        int D => Mathf.Clamp(Difficulty - 1, 0, 2);
        float CurrentHoldTime => holdTime[Mathf.Min(D, holdTime.Length - 1)];

        void Awake()
        {
            seed = UnityEngine.Random.value * 100f;
            needleAngle = Mathf.Lerp(needleMinAngle, needleMaxAngle, Pressure);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.8f;
            audioSource.minDistance = 1.5f;
        }

        void Start() => ShowStatus();

        void Update()
        {
            if (IsFaulty)
                UpdateFault();
            else
                Pressure = 0.5f + (Mathf.PerlinNoise(Time.time * 0.5f + seed, 1.7f) - 0.5f) * 0.06f;

            wheel.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);
            float target = Mathf.Lerp(needleMinAngle, needleMaxAngle, Pressure);
            needleAngle = Mathf.Lerp(needleAngle, target, 1f - Mathf.Exp(-needleSpeed * Time.deltaTime));
            needle.localRotation = Quaternion.Euler(0f, 0f, needleAngle);
            if (gauge != null)
                gauge.Drive(Mathf.InverseLerp(needleMinAngle, needleMaxAngle, needleAngle));
        }

        void UpdateFault()
        {
            if (Time.time >= nextSurge)
                Surge();
            float wobble = (Mathf.PerlinNoise(Time.time * 1.3f + seed, 4.2f) - 0.5f) * driftSpeed[Mathf.Min(D, driftSpeed.Length - 1)];
            Pressure = Mathf.Clamp01(Pressure + (drift + wobble) * Time.deltaTime);

            float needed = CurrentHoldTime;
            hold = InGreen ? hold + Time.deltaTime : Mathf.Max(0f, hold - Time.deltaTime * holdDrain);
            if (InGreen && Time.time >= nextTick)
            {
                // Ticks speed up as the hold timer fills
                nextTick = Time.time + Mathf.Lerp(0.6f, 0.12f, hold / needed);
                audioSource.PlayOneShot(Tones.Blip, tickVolume);
            }
            ShowStatus();
            if (hold >= needed)
                CompleteRepair();
        }

        /// <summary>The pressure turns and pushes the other way (sometimes harder).</summary>
        void Surge()
        {
            var every = surgeEvery[Mathf.Min(D, surgeEvery.Length - 1)];
            nextSurge = Time.time + UnityEngine.Random.Range(every.x, every.y);
            float speed = driftSpeed[Mathf.Min(D, driftSpeed.Length - 1)] * UnityEngine.Random.Range(0.7f, 1.3f);
            // Usually push back towards the other side, so the needle swings both ways
            float side = Pressure > 0.5f ? -1f : 1f;
            if (UnityEngine.Random.value < 0.3f)
                side = -side;
            drift = side * speed;
        }

        protected override void OnFaultBegan()
        {
            hold = 0f;
            // Start out of the green, on a random side, already drifting further out
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            Pressure = 0.5f + side * UnityEngine.Random.Range(greenBand * 0.5f + 0.12f, 0.42f);
            drift = side * driftSpeed[Mathf.Min(D, driftSpeed.Length - 1)];
            var every = surgeEvery[Mathf.Min(D, surgeEvery.Length - 1)];
            nextSurge = Time.time + UnityEngine.Random.Range(every.x, every.y);
            ShowStatus();
        }

        protected override void OnRepaired()
        {
            hold = 0f;
            drift = 0f;
            ShowStatus();
            audioSource.PlayOneShot(Tones.Note(3), 0.5f);
        }

        void ShowStatus()
        {
            if (statusLight == null)
                return;
            var state = !IsFaulty ? IndicatorState.Green : InGreen ? IndicatorState.Amber : IndicatorState.Red;
            bool blink = IsFaulty && !InGreen;
            if (statusLight.State != state || statusLight.Blinking != blink)
                statusLight.SetState(state, blink);
        }

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            dragging = PointerAngle(ray, out lastPointerAngle);
            Sfx.PlayAt(SoundBank.ValveGrab, wheel.position, 0.6f);
        }

        public void OnPointerDrag(Ray ray)
        {
            if (!PointerAngle(ray, out float angle))
                return;
            if (!dragging)
            {
                dragging = true;
                lastPointerAngle = angle;
                return;
            }

            // The wheel follows the finger; angles grow counter-clockwise as the player sees it
            float delta = Mathf.DeltaAngle(lastPointerAngle, angle);
            lastPointerAngle = angle;
            wheelAngle += delta;

            float clockwise = -delta;
            Turned?.Invoke(clockwise);
            if (IsFaulty)
                Pressure = Mathf.Clamp01(Pressure + clockwise / (360f * turnsFullScale));
        }

        public void OnPointerUp() => dragging = false;

        /// <summary>Angle of the pointer around the wheel axis, in the station's plane (x right, y up as the player sees it).</summary>
        bool PointerAngle(Ray ray, out float angle)
        {
            angle = 0f;
            var plane = new Plane(-wheel.forward, wheel.position);
            if (!plane.Raycast(ray, out float distance))
                return false;
            var local = transform.InverseTransformPoint(ray.GetPoint(distance)) - transform.InverseTransformPoint(wheel.position);
            if (new Vector2(local.x, local.y).sqrMagnitude < 1e-4f)
                return false;
            angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            return true;
        }
    }
}
