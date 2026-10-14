using UnityEngine;

namespace Aquatica.AR
{
    /// <summary>Scales the gateway in with a slight overshoot when it is spawned.</summary>
    public class GatewayAppear : MonoBehaviour
    {
        [SerializeField] float duration = 0.9f;
        [SerializeField] float overshoot = 1.6f;

        Vector3 targetScale;
        float elapsed;

        void Awake()
        {
            targetScale = transform.localScale;
            transform.localScale = Vector3.zero;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = targetScale * EaseOutBack(t);

            if (t >= 1f)
                enabled = false;
        }

        float EaseOutBack(float t)
        {
            float c3 = overshoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + overshoot * u * u;
        }
    }
}
