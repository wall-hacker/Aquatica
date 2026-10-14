using TMPro;
using UnityEngine;

namespace Aquatica.AR
{
    /// <summary>
    /// The projected water-ripple area: fades/scales in and out and toggles its bubbles and prompt text.
    /// </summary>
    public class PlacementArea : MonoBehaviour
    {
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] Renderer rippleRenderer;
        [SerializeField] ParticleSystem bubbles;
        [SerializeField] TMP_Text prompt;
        [SerializeField] float fadeDuration = 0.25f;

        MaterialPropertyBlock block;
        Vector3 baseScale;
        float fade;
        float targetFade;
        bool promptVisible = true;

        public bool IsVisible => fade > 0.01f;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
        }

        public void Show() => SetTarget(1f);

        public void Hide() => SetTarget(0f);

        public void HideImmediate()
        {
            targetFade = fade = 0f;
            Apply();
            if (bubbles != null)
                bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetPromptVisible(bool visible)
        {
            promptVisible = visible;
            Apply();
        }

        void SetTarget(float value)
        {
            if (Mathf.Approximately(targetFade, value))
                return;

            targetFade = value;
            if (bubbles == null)
                return;
            if (value > 0f)
                bubbles.Play(true);
            else
                bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (Mathf.Approximately(fade, targetFade))
                return;

            fade = Mathf.MoveTowards(fade, targetFade, Time.deltaTime / Mathf.Max(fadeDuration, 0.001f));
            Apply();
        }

        void Apply()
        {
            float eased = Mathf.SmoothStep(0f, 1f, fade);
            transform.localScale = baseScale * Mathf.Lerp(0.6f, 1f, eased);

            if (rippleRenderer != null)
            {
                rippleRenderer.GetPropertyBlock(block);
                block.SetFloat(FadeId, eased);
                rippleRenderer.SetPropertyBlock(block);
            }

            if (prompt != null)
            {
                prompt.alpha = promptVisible ? eased : 0f;
                prompt.gameObject.SetActive(promptVisible && eased > 0f);
            }
        }
    }
}
