using Aquatica.Navigation;
using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// The engine heard from inside the cabin: a soft hum and propeller wash that swells and rises in pitch as the
    /// engine spools up, then winds down slowly after the throttle is let go (its own long release on top of the
    /// motor's spool-down, plus a little of the coasting speed), so it never cuts off abruptly.
    /// </summary>
    public class EngineSound : MonoBehaviour
    {
        [Tooltip("Found in children if left empty.")]
        [SerializeField] SubmarineMotor motor;
        [Tooltip("Volume at full thrust: kept low, it sits under everything else.")]
        [SerializeField] float maxVolume = 0.22f;
        [Tooltip("Seconds to swell in when the engine spools up.")]
        [SerializeField] float attackTime = 0.35f;
        [Tooltip("Time constant (s) of the engine winding down after the throttle is released.")]
        [SerializeField] float releaseTime = 1.4f;
        [SerializeField] float idlePitch = 0.62f;
        [SerializeField] float fullPitch = 1f;
        [Tooltip("Speed (m/s) treated as full speed for the wash that lingers while coasting.")]
        [SerializeField] float referenceSpeed = 3.6f;

        AudioSource source;
        float level;

        void Start()
        {
            if (motor == null)
                motor = GetComponentInChildren<SubmarineMotor>();
            source = gameObject.AddComponent<AudioSource>();
            source.clip = SoundBank.Engine;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 64;
        }

        void Update()
        {
            if (motor == null || source == null)
                return;
            float dt = Time.deltaTime;
            float target = motor.EngineLevel;
            float time = target > level ? attackTime : releaseTime;
            level = Mathf.Lerp(level, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, time)));
            if (level < 0.002f && target <= 0f)
                level = 0f;

            float coast = Mathf.Clamp01(motor.Speed / Mathf.Max(0.1f, referenceSpeed));
            float drive = Mathf.Max(level, coast * 0.35f * Mathf.Min(1f, level * 6f));
            source.volume = maxVolume * Mathf.SmoothStep(0f, 1f, drive);
            source.pitch = Mathf.Lerp(idlePitch, fullPitch, level) + coast * 0.04f;

            if (source.volume > 0.001f && !source.isPlaying)
                source.Play();
            else if (source.volume <= 0.001f && source.isPlaying)
                source.Stop();
        }
    }
}
