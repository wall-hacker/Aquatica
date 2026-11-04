using Aquatica.Cockpit;
using UnityEngine;
using UnityEngine.UI;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Full-screen "looking at a CRT monitor" overlay: a camera feed (or black) shown curved, scanlined and noisy inside
    /// a dark bezel, with a low-resolution phosphor HUD (<see cref="Hud"/>, drawn with <see cref="PixelFont"/>) on top.
    /// Drawn on a top-most screen-space canvas; destroy the GameObject to remove it.
    /// </summary>
    public class CrtOverlay : MonoBehaviour
    {
        static readonly int HudTexId = Shader.PropertyToID("_HudTex");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int StaticId = Shader.PropertyToID("_Static");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int FadeColorId = Shader.PropertyToID("_FadeColor");
        static readonly int FeedId = Shader.PropertyToID("_Feed");

        RawImage image;
        Material material;
        Texture2D hudTexture;
        int screenWidth, screenHeight;
        int hudColumns;

        /// <summary>The HUD bitmap: draw into it, then <see cref="Present"/>.</summary>
        public PixelCanvas Hud { get; private set; }

        /// <param name="hudColumns">HUD width in pixels along the screen's shorter side; the other side follows the aspect.</param>
        public static CrtOverlay Create(Shader shader, int hudColumns = 132, int sortingOrder = short.MaxValue - 1)
        {
            var go = new GameObject("CrtOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var overlay = go.AddComponent<CrtOverlay>();
            overlay.image = new GameObject("Screen").AddComponent<RawImage>();
            overlay.image.transform.SetParent(go.transform, false);
            overlay.image.raycastTarget = false;
            var rect = overlay.image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            overlay.material = new Material(shader);
            overlay.image.material = overlay.material;
            overlay.image.texture = Texture2D.blackTexture;
            overlay.hudColumns = hudColumns;
            overlay.Resize();
            return overlay;
        }

        /// <summary>Camera feed shown on the screen (null = a dark, empty tube).</summary>
        public void SetFeed(Texture feed)
        {
            image.texture = feed != null ? feed : Texture2D.blackTexture;
            material.SetFloat(FeedId, feed != null ? 1f : 0f);
        }

        /// <summary>Snow and roll over the whole picture (0-1).</summary>
        public void SetStatic(float amount) => material.SetFloat(StaticId, amount);

        /// <summary>Fades the tube to a flat colour (0 = picture, 1 = colour).</summary>
        public void SetFade(float amount, Color color)
        {
            material.SetFloat(FadeId, amount);
            material.SetColor(FadeColorId, color);
        }

        /// <summary>Uploads the HUD drawn since the last call.</summary>
        public void Present()
        {
            hudTexture.SetPixelData(Hud.Pixels, 0);
            hudTexture.Apply(false);
        }

        void Update()
        {
            if (Screen.width != screenWidth || Screen.height != screenHeight)
                Resize();
        }

        /// <summary>Matches the HUD bitmap to the screen's aspect (the phone may rotate).</summary>
        void Resize()
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            float aspect = (float)screenWidth / Mathf.Max(1, screenHeight);
            int w = aspect >= 1f ? Mathf.RoundToInt(hudColumns * aspect) : hudColumns;
            int h = aspect >= 1f ? hudColumns : Mathf.RoundToInt(hudColumns / aspect);
            if (hudTexture != null)
                Destroy(hudTexture);
            hudTexture = new Texture2D(w, h, TextureFormat.R8, false)
            {
                name = "CrtHud",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Hud = new PixelCanvas(w, h);
            material.SetTexture(HudTexId, hudTexture);
            material.SetFloat(AspectId, aspect);
            Present();
        }

        void OnDestroy()
        {
            Destroy(material);
            if (hudTexture != null)
                Destroy(hudTexture);
        }
    }
}
