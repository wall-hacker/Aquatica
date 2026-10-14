using UnityEngine;

namespace Aquatica.AR
{
    /// <summary>Gently bobs, spins and pulses a sparkle so the portal interior feels alive.</summary>
    public class FloatingSparkle : MonoBehaviour
    {
        [SerializeField] float bobAmplitude = 0.02f;
        [SerializeField] float bobSpeed = 1.2f;
        [SerializeField] float spinSpeed = 20f;
        [SerializeField] float pulse = 0.15f;

        Vector3 basePosition;
        Vector3 baseScale;
        float phase;

        void Awake()
        {
            basePosition = transform.localPosition;
            baseScale = transform.localScale;
            phase = Random.value * 10f;
        }

        void Update()
        {
            float t = Time.time + phase;
            transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobAmplitude);
            transform.localRotation = Quaternion.Euler(0f, 0f, t * spinSpeed);
            transform.localScale = baseScale * (1f + Mathf.Sin(t * 2.3f) * pulse);
        }
    }
}
