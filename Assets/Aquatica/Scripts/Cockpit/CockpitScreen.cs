using Aquatica.Audio;
using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    public enum TextAlign { Left, Center, Right }

    /// <summary>
    /// Low-resolution monochrome display on a cockpit monitor: a tiny bitmap drawn on the CPU with
    /// <see cref="PixelFont"/> and shown through the Aquatica/CockpitScreen shader (phosphor dots, scanlines,
    /// glow). Whoever owns the screen redraws it between <see cref="Begin"/> and <see cref="End"/>; the screen
    /// glitches briefly whenever the page changes. Tapping the screen raises <see cref="Tapped"/>, so it can
    /// stand in for a dialog prompt inside the cockpit.
    /// </summary>
    public class CockpitScreen : MonoBehaviour, ICockpitInteractable
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        static readonly int PowerId = Shader.PropertyToID("_Power");

        [SerializeField] Renderer surface;
        [SerializeField] Vector2Int resolution = new(128, 86);
        [SerializeField, ColorUsage(false, true)] Color glowColor = new(0.3f, 1.4f, 0.55f);
        [SerializeField] float glitchDuration = 0.25f;
        [SerializeField] float colorFadeSpeed = 6f;
        [SerializeField] float powerFadeSpeed = 3f;

        public event Action<CockpitScreen> Tapped;

        public int Width => resolution.x;
        public int Height => resolution.y;

        Texture2D texture;
        PixelCanvas canvas;
        Material material;
        Color currentColor;
        Color targetColor;
        float glitch;
        float power;
        bool powered = true;
        int pageHash;
        int tapCount;

        void Awake()
        {
            texture = new Texture2D(resolution.x, resolution.y, TextureFormat.R8, false)
            {
                name = name + "Display",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            canvas = new PixelCanvas(resolution.x, resolution.y);
            material = surface.material;
            material.SetTexture(MainTexId, texture);
            currentColor = targetColor = glowColor;
            End();
        }

        void OnDestroy()
        {
            Destroy(texture);
            Destroy(material);
        }

        void Update()
        {
            float k = 1f - Mathf.Exp(-colorFadeSpeed * Time.deltaTime);
            currentColor = Color.Lerp(currentColor, targetColor, k);
            glitch = Mathf.MoveTowards(glitch, 0f, Time.deltaTime / Mathf.Max(0.01f, glitchDuration));
            power = Mathf.MoveTowards(power, powered ? 1f : 0f, powerFadeSpeed * Time.deltaTime);
            material.SetColor(GlowColorId, currentColor);
            material.SetFloat(GlitchId, glitch);
            material.SetFloat(PowerId, power);
        }

        /// <summary>Text colour (HDR); fades over from the current one. Null returns to the default colour.</summary>
        public void SetColor(Color? color) => targetColor = color ?? glowColor;

        public void SetPowered(bool on) => powered = on;

        /// <summary>Plays the page-change glitch.</summary>
        public void Glitch() => glitch = 1f;

        /// <summary>Starts a new frame: clears the bitmap.</summary>
        public void Begin() => canvas.Clear();

        /// <summary>
        /// Uploads the frame. A frame drawn with a different page key than the previous one glitches
        /// the screen, so callers can redraw every frame (blinking, live values) and only real page
        /// changes look like a switch.
        /// </summary>
        public void End(string pageKey = null)
        {
            int hash = pageKey?.GetHashCode() ?? 0;
            if (hash != pageHash)
            {
                pageHash = hash;
                Glitch();
            }
            texture.SetPixelData(canvas.Pixels, 0);
            texture.Apply(false);
        }

        /// <summary>The bitmap the drawing calls below go to.</summary>
        public PixelCanvas Canvas => canvas;

        /// <summary>Draws one line of text; y is the top of the line, in pixels from the top of the screen.</summary>
        public void Text(string text, int x, int y, TextAlign align = TextAlign.Left, int scale = 1, float intensity = 1f) =>
            canvas.Text(text, x, y, align, scale, intensity);

        /// <summary>Centered text that wraps on spaces to fit maxWidth; returns the y below the last line.</summary>
        public int Paragraph(string text, int centerX, int y, int maxWidth, int scale = 1, float intensity = 1f) =>
            canvas.Paragraph(text, centerX, y, maxWidth, scale, intensity);

        public void Rect(int x, int y, int width, int height, float intensity = 1f) => canvas.Rect(x, y, width, height, intensity);

        public void FillRect(int x, int y, int width, int height, float intensity = 1f) => canvas.FillRect(x, y, width, height, intensity);

        /// <summary>Straight line between two pixels (inclusive).</summary>
        public void Line(int x0, int y0, int x1, int y1, float intensity = 1f) => canvas.Line(x0, y0, x1, y1, intensity);

        /// <summary>Horizontal dashed rule.</summary>
        public void Rule(int y, int margin = 4, float intensity = 0.5f) => canvas.Rule(y, margin, intensity);

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            if (!powered)
                return;
            // Alternate two beeps so quick repeated taps don't drone
            Sfx.PlayAt((tapCount++ & 1) == 0 ? SoundBank.ScreenBeep : SoundBank.ScreenBeepAlt, transform.position, 0.6f, 0f);
            Tapped?.Invoke(this);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() { }
    }
}
