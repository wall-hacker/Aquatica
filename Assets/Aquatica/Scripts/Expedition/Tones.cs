using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>Small synthesised cockpit sounds (the project has no audio assets): built once, on first use.</summary>
    public static class Tones
    {
        const int Rate = 44100;
        static AudioClip ping, blip, chime, reveal, rumble;

        /// <summary>Sonar ping: a clean tone with a long decay.</summary>
        public static AudioClip Ping => ping ??= Build("Ping", 1.2f, t => Mathf.Sin(t * 1180f * Mathf.PI * 2f) * Mathf.Exp(-t * 5f) * 0.5f);

        /// <summary>Short UI blip.</summary>
        public static AudioClip Blip => blip ??= Build("Blip", 0.08f, t => Square(t * 1600f) * Mathf.Exp(-t * 40f) * 0.18f);

        /// <summary>Something recovered: two rising tones.</summary>
        public static AudioClip Reveal => reveal ??= Build("Reveal", 0.6f, t =>
            (Mathf.Sin(t * 660f * Mathf.PI * 2f) * Env(t, 0f, 0.25f) + Mathf.Sin(t * 990f * Mathf.PI * 2f) * Env(t, 0.15f, 0.4f)) * 0.3f);

        /// <summary>Tide Crystal: a shimmering chord.</summary>
        public static AudioClip Chime => chime ??= Build("Chime", 2.2f, t =>
        {
            float a = Mathf.Sin(t * 784f * Mathf.PI * 2f) + Mathf.Sin(t * 1175f * Mathf.PI * 2f) * 0.7f + Mathf.Sin(t * 1568f * Mathf.PI * 2f) * 0.4f;
            return a * Mathf.Exp(-t * 1.8f) * (0.8f + 0.2f * Mathf.Sin(t * 30f)) * 0.22f;
        });

        /// <summary>Deep rumble for the gateway.</summary>
        public static AudioClip Rumble => rumble ??= Build("Rumble", 4f, t =>
        {
            float body = Mathf.Sin(t * 42f * Mathf.PI * 2f) + Mathf.Sin(t * 63f * Mathf.PI * 2f) * 0.6f;
            float swell = Mathf.Sin(Mathf.Clamp01(t / 4f) * Mathf.PI);
            return (body + (Noise(t) - 0.5f) * 0.6f) * swell * 0.35f;
        });

        static float Env(float t, float start, float length) =>
            t < start ? 0f : Mathf.Exp(-(t - start) / length * 3f) * Mathf.Clamp01((t - start) * 200f);

        static float Square(float phase) => Mathf.Repeat(phase, 1f) < 0.5f ? 1f : -1f;

        static float Noise(float t) => Mathf.PerlinNoise(t * 180f, 0.3f);

        static AudioClip Build(string name, float seconds, System.Func<float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                // Short fades at both ends so nothing clicks
                float edge = Mathf.Clamp01(t * 400f) * Mathf.Clamp01((seconds - t) * 100f);
                data[i] = wave(t) * edge;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
