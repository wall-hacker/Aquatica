using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Dial gauge whose needle idles with a slight wobble and climbs into the red, shaking, while <see cref="Alarm"/> is set.</summary>
    public class PressureGauge : MonoBehaviour
    {
        [Tooltip("Needle pivot; points up at 0 degrees and turns around its local Z axis.")]
        [SerializeField] Transform needle;
        [SerializeField] float zeroAngle = 135f;
        [SerializeField] float fullAngle = -135f;
        [SerializeField, Range(0f, 1f)] float normalReading = 0.35f;
        [SerializeField, Range(0f, 1f)] float alarmReading = 0.9f;
        [SerializeField] float response = 3f;

        float reading;
        float seed;

        public bool Alarm { get; set; }
        public float Reading => reading;

        void Awake()
        {
            seed = Random.value * 100f;
            reading = normalReading;
        }

        void Update()
        {
            float t = Time.time + seed;
            float goal = Alarm
                ? alarmReading + Mathf.Sin(t * 23f) * 0.02f + (Mathf.PerlinNoise(t * 3f, 0f) - 0.5f) * 0.1f
                : normalReading + (Mathf.PerlinNoise(t * 0.6f, 3f) - 0.5f) * 0.06f;
            reading = Mathf.Lerp(reading, Mathf.Clamp01(goal), 1f - Mathf.Exp(-response * Time.deltaTime));
            needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(zeroAngle, fullAngle, reading));
        }
    }
}
