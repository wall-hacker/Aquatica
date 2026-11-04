using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// A small one-channel bitmap drawn on the CPU with <see cref="PixelFont"/>: text, boxes and lines in
    /// top-down pixel coordinates. Backs the cockpit screens and the full-screen CRT overlays.
    /// </summary>
    public class PixelCanvas
    {
        readonly byte[] pixels;

        public int Width { get; }
        public int Height { get; }

        /// <summary>Rows bottom-up, as a texture expects them.</summary>
        public byte[] Pixels => pixels;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            pixels = new byte[width * height];
        }

        public void Clear() => Array.Clear(pixels, 0, pixels.Length);

        /// <summary>Draws one line of text; y is the top of the line, in pixels from the top.</summary>
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
            foreach (var line in Wrap(text, maxWidth, scale))
            {
                Text(line, centerX, y, TextAlign.Center, scale, intensity);
                y += PixelFont.LineHeight * scale;
            }
            return y;
        }

        /// <summary>Splits text on spaces into lines that fit maxWidth pixels.</summary>
        public static System.Collections.Generic.List<string> Wrap(string text, int maxWidth, int scale = 1)
        {
            int maxChars = Mathf.Max(1, (maxWidth + 1) / (PixelFont.Advance * scale));
            var lines = new System.Collections.Generic.List<string>();
            string line = "";
            foreach (var word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (candidate.Length > maxChars && line.Length > 0)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
            return lines;
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

        /// <summary>Circle outline, optionally dashed (every other step of dash pixels).</summary>
        public void Circle(int cx, int cy, int radius, float intensity = 1f, int dash = 0)
        {
            byte value = ToByte(intensity);
            int steps = Mathf.Max(8, Mathf.RoundToInt(radius * 6.3f));
            for (int i = 0; i < steps; i++)
            {
                if (dash > 0 && i / dash % 2 == 1)
                    continue;
                float a = i * Mathf.PI * 2f / steps;
                Fill(cx + Mathf.RoundToInt(Mathf.Cos(a) * radius), cy + Mathf.RoundToInt(Mathf.Sin(a) * radius), 1, 1, value);
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
    }
}
