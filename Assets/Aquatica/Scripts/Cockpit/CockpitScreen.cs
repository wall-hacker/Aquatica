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
        byte[] pixels;
        Material material;
        Color currentColor;
        Color targetColor;
        float glitch;
        float power;
        bool powered = true;
        int pageHash;

        void Awake()
        {
            texture = new Texture2D(resolution.x, resolution.y, TextureFormat.R8, false)
            {
                name = name + "Display",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            pixels = new byte[resolution.x * resolution.y];
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
        public void Begin() => Array.Clear(pixels, 0, pixels.Length);

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
            texture.SetPixelData(pixels, 0);
            texture.Apply(false);
        }

        /// <summary>Draws one line of text; y is the top of the line, in pixels from the top of the screen.</summary>
        public void Text(string text, int x, int y, TextAlign align = TextAlign.Left, int scale = 1, float intensity = 1f)
        {
            if (string.IsNullOrEmpty(text))
                return;
            scale = Mathf.Max(1, scale);
            int width = PixelFont.Measure(text, scale);
            if (align == TextAlign.Center)
                x -= width / 2;
            else if (align == TextAlign.Right)
                x -= width;

            byte value = ToByte(intensity);
            for (int i = 0; i < text.Length; i++)
            {
                int gx = x + i * PixelFont.Advance * scale;
                for (int column = 0; column < PixelFont.GlyphWidth; column++)
                for (int row = 0; row < PixelFont.GlyphHeight; row++)
                    if (PixelFont.IsSet(text[i], column, row))
                        Fill(gx + column * scale, y + row * scale, scale, scale, value);
            }
        }

        /// <summary>Centered text that wraps on spaces to fit maxWidth; returns the y below the last line.</summary>
        public int Paragraph(string text, int centerX, int y, int maxWidth, int scale = 1, float intensity = 1f)
        {
            int maxChars = Mathf.Max(1, (maxWidth + 1) / (PixelFont.Advance * scale));
            var words = text.Split(' ');
            string line = "";
            foreach (var word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (candidate.Length > maxChars && line.Length > 0)
                {
                    Text(line, centerX, y, TextAlign.Center, scale, intensity);
                    y += PixelFont.LineHeight * scale;
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            Text(line, centerX, y, TextAlign.Center, scale, intensity);
            return y + PixelFont.LineHeight * scale;
        }

        public void Rect(int x, int y, int width, int height, float intensity = 1f)
        {
            byte value = ToByte(intensity);
            Fill(x, y, width, 1, value);
            Fill(x, y + height - 1, width, 1, value);
            Fill(x, y, 1, height, value);
            Fill(x + width - 1, y, 1, height, value);
        }

        public void FillRect(int x, int y, int width, int height, float intensity = 1f) => Fill(x, y, width, height, ToByte(intensity));

        /// <summary>Straight line between two pixels (inclusive).</summary>
        public void Line(int x0, int y0, int x1, int y1, float intensity = 1f)
        {
            byte value = ToByte(intensity);
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                Fill(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), 1, 1, value);
            }
        }

        /// <summary>Horizontal dashed rule.</summary>
        public void Rule(int y, int margin = 4, float intensity = 0.5f)
        {
            byte value = ToByte(intensity);
            for (int x = margin; x < Width - margin; x += 3)
                Fill(x, y, 2, 1, value);
        }

        void Fill(int x, int y, int width, int height, byte value)
        {
            int x0 = Mathf.Max(0, x), x1 = Mathf.Min(Width, x + width);
            int y0 = Mathf.Max(0, y), y1 = Mathf.Min(Height, y + height);
            for (int py = y0; py < y1; py++)
            {
                // Texture rows go bottom-up; drawing coordinates go top-down
                int row = (Height - 1 - py) * Width;
                for (int px = x0; px < x1; px++)
                    pixels[row + px] = value;
            }
        }

        static byte ToByte(float intensity) => (byte)Mathf.RoundToInt(Mathf.Clamp01(intensity) * 255f);

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            if (powered)
                Tapped?.Invoke(this);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() { }
    }
}
