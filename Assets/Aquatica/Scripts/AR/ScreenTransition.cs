using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Aquatica.AR
{
    /// <summary>
    /// Full-screen iris transition drawn on a top-most overlay canvas. The iris closes to black, the
    /// callback runs while the screen is fully covered (so heavy work like world generation is hidden),
    /// then the iris opens again and the overlay removes itself.
    /// </summary>
    public class ScreenTransition : MonoBehaviour
    {
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");

        Material material;

        public static ScreenTransition Play(Material template, float closeDuration, float holdDuration, float openDuration, Action onCovered)
        {
            var go = new GameObject("ScreenTransition");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var image = new GameObject("Iris").AddComponent<RawImage>();
            image.transform.SetParent(go.transform, false);
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var transition = go.AddComponent<ScreenTransition>();
            transition.material = new Material(template);
            image.material = transition.material;
            transition.StartCoroutine(transition.Run(closeDuration, holdDuration, openDuration, onCovered));
            return transition;
        }

        IEnumerator Run(float close, float hold, float open, Action onCovered)
        {
            yield return Animate(0f, 1f, close);

            // Let the fully black frame present before doing any heavy work
            yield return null;
            onCovered?.Invoke();
            yield return new WaitForSeconds(hold);

            yield return Animate(1f, 0f, open);
            Destroy(gameObject);
        }

        IEnumerator Animate(float from, float to, float duration)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                SetProgress(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetProgress(to);
        }

        void SetProgress(float progress)
        {
            material.SetFloat(ProgressId, progress);
            material.SetFloat(AspectId, (float)Screen.width / Mathf.Max(1, Screen.height));
        }

        void OnDestroy() => Destroy(material);
    }
}
