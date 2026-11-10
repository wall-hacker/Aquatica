using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// Synthesised cockpit and world sounds (the project has no audio assets), built once on first use:
    /// mechanical clicks and clacks for physical controls, digital beeps for screens, the engine loop and the two
    /// ambience beds. Everything that loops is built so its seam is inaudible: periodic parts complete whole
    /// cycles in the loop length and noise is crossfaded tail-to-head.
    /// </summary>
    public static class SoundBank
    {
        const int Rate = 44100;
        const int AmbienceRate = 22050; // the beds are mostly low and soft, so half the rate is plenty
        const float TwoPi = Mathf.PI * 2f;

        // ---------------------------------------------------------------- physical controls

        static AudioClip click, clickUp, clack, coverClack, keyTurn, leverGrab, leverThunk, valveGrab;

        /// <summary>Push button going down: a crisp, small plastic-and-spring click.</summary>
        public static AudioClip Click => click ??= Build("Click", 0.07f, 101, (t, n) =>
            n * Mathf.Exp(-t * 900f) * 0.45f
            + Mathf.Sin(t * 3300f * TwoPi) * Mathf.Exp(-t * 260f) * 0.3f
            + Mathf.Sin(t * 950f * TwoPi) * Mathf.Exp(-t * 110f) * 0.25f);

        /// <summary>Push button coming back up: a lighter, higher tick.</summary>
        public static AudioClip ClickUp => clickUp ??= Build("ClickUp", 0.05f, 102, (t, n) =>
            n * Mathf.Exp(-t * 1300f) * 0.3f + Mathf.Sin(t * 4300f * TwoPi) * Mathf.Exp(-t * 320f) * 0.2f);

        /// <summary>Toggle switch snapping over: a light tick, then the heavier clack of the lever landing.</summary>
        public static AudioClip Clack => clack ??= Build("Clack", 0.14f, 103, (t, n) =>
        {
            float first = n * Mathf.Exp(-t * 1100f) * 0.25f + Mathf.Sin(t * 2600f * TwoPi) * Mathf.Exp(-t * 300f) * 0.15f;
            float u = t - 0.014f;
            float second = u < 0f ? 0f
                : n * Mathf.Exp(-u * 600f) * 0.5f
                  + (Mathf.Sin(u * 640f * TwoPi) + Mathf.Sin(u * 1370f * TwoPi) * 0.5f) * Mathf.Exp(-u * 55f) * 0.35f;
            return first + second;
        });

        /// <summary>Safety cover flipping: a hollow, heavier plastic-on-metal clack.</summary>
        public static AudioClip CoverClack => coverClack ??= Build("CoverClack", 0.2f, 104, (t, n) =>
            n * Mathf.Exp(-t * 500f) * 0.45f
            + (Mathf.Sin(t * 410f * TwoPi) + Mathf.Sin(t * 1090f * TwoPi) * 0.45f + Mathf.Sin(t * 2230f * TwoPi) * 0.2f) * Mathf.Exp(-t * 38f) * 0.35f);

        /// <summary>Key turning: a short ratchet of detents, then the lock clunking home.</summary>
        public static AudioClip KeyTurn => keyTurn ??= Build("KeyTurn", 0.16f, 105, (t, n) =>
        {
            float v = 0f;
            for (int i = 0; i < 3; i++)
            {
                float u = t - i * 0.02f;
                if (u >= 0f)
                    v += (n * Mathf.Exp(-u * 1500f) * 0.25f + Mathf.Sin(u * 3800f * TwoPi) * Mathf.Exp(-u * 400f) * 0.15f) * (1f - i * 0.15f);
            }
            float w = t - 0.065f;
            if (w >= 0f)
                v += n * Mathf.Exp(-w * 700f) * 0.35f + (Mathf.Sin(w * 520f * TwoPi) + Mathf.Sin(w * 1240f * TwoPi) * 0.4f) * Mathf.Exp(-w * 60f) * 0.35f;
            return v;
        });

        /// <summary>Throttle grabbed: the lever's latch disengaging with a metallic clack.</summary>
        public static AudioClip LeverGrab => leverGrab ??= Build("LeverGrab", 0.22f, 106, (t, n) =>
            n * Mathf.Exp(-t * 700f) * 0.4f
            + (Mathf.Sin(t * 1870f * TwoPi) * 0.35f + Mathf.Sin(t * 2950f * TwoPi) * 0.15f) * Mathf.Exp(-t * 45f)
            + Mathf.Sin(t * 310f * TwoPi) * Mathf.Exp(-t * 40f) * 0.35f);

        /// <summary>Throttle swinging back to STOP: a dull, heavy thunk against the end stop.</summary>
        public static AudioClip LeverThunk => leverThunk ??= Build("LeverThunk", 0.35f, 107, (t, n) =>
            (Mathf.Sin(t * Mathf.Lerp(150f, 105f, Mathf.Clamp01(t / 0.1f)) * TwoPi) + Mathf.Sin(t * 260f * TwoPi) * 0.4f) * Mathf.Exp(-t * 26f) * 0.55f
            + n * Mathf.Exp(-t * 300f) * 0.3f
            + Mathf.Sin(t * 1620f * TwoPi) * Mathf.Exp(-t * 70f) * 0.08f);

        /// <summary>Hand closing on the valve wheel: a soft metallic knock.</summary>
        public static AudioClip ValveGrab => valveGrab ??= Build("ValveGrab", 0.18f, 108, (t, n) =>
            n * Mathf.Exp(-t * 900f) * 0.25f
            + (Mathf.Sin(t * 720f * TwoPi) + Mathf.Sin(t * 1930f * TwoPi) * 0.35f) * Mathf.Exp(-t * 45f) * 0.3f);

        // ---------------------------------------------------------------- screens

        static AudioClip screenBeep, screenBeepAlt;

        /// <summary>Touch on a screen: a short, clean digital beep with a square-wave edge.</summary>
        public static AudioClip ScreenBeep => screenBeep ??= Build("ScreenBeep", 0.075f, 0, (t, _) =>
            Beep(t, 1975f, 0f, 0.065f) * 0.32f);

        /// <summary>A second beep, a fifth up then back: used on alternate taps so repeated taps don't drone.</summary>
        public static AudioClip ScreenBeepAlt => screenBeepAlt ??= Build("ScreenBeepAlt", 0.11f, 0, (t, _) =>
            (Beep(t, 2637f, 0f, 0.045f) + Beep(t, 1975f, 0.055f, 0.1f)) * 0.28f);

        static float Beep(float t, float f, float start, float end)
        {
            if (t < start || t > end)
                return 0f;
            float gate = Mathf.Clamp01((t - start) * 1500f) * Mathf.Clamp01((end - t) * 600f);
            float square = Mathf.Repeat(t * f, 1f) < 0.5f ? 1f : -1f;
            return (Mathf.Sin(t * f * TwoPi) * 0.75f + square * 0.12f) * gate;
        }

        // ---------------------------------------------------------------- faults

        static AudioClip faultHit;

        /// <summary>A fault breaking out: a deep boom under a metallic groan and ringing hull partials.</summary>
        public static AudioClip FaultHit => faultHit ??= Build("FaultHit", 1.6f, 109, (t, n) =>
        {
            float boom = Mathf.Sin(t * Mathf.Lerp(70f, 38f, Mathf.Clamp01(t / 0.4f)) * TwoPi) * Mathf.Exp(-t * 4f) * 0.6f;
            float ring = (Mathf.Sin(t * 233f * TwoPi) + Mathf.Sin(t * 377f * TwoPi) * 0.7f + Mathf.Sin(t * 611f * TwoPi) * 0.45f + Mathf.Sin(t * 987f * TwoPi) * 0.25f)
                         * Mathf.Exp(-t * 2.6f) * 0.16f;
            float groan = Mathf.Sin(t * Mathf.Lerp(180f, 120f, t / 1.6f) * TwoPi + Mathf.Sin(t * 7f * TwoPi) * 2f) * Mathf.Exp(-t * 1.8f) * Mathf.Clamp01(t * 6f) * 0.12f;
            return boom + ring + groan + n * Mathf.Exp(-t * 60f) * 0.35f;
        });

        // ---------------------------------------------------------------- engine

        static AudioClip engine;

        /// <summary>
        /// Engine (seamless 4 s loop): a soft electric-motor hum with a slow beat, a propeller wash pulsing at the
        /// blade rate, and a faint whine. Played pitched and faded by <see cref="EngineSound"/>.
        /// </summary>
        public static AudioClip Engine => engine ??= BuildEngine();

        static AudioClip BuildEngine()
        {
            const float seconds = 4f, overlap = 0.4f;
            int count = Mathf.RoundToInt(seconds * Rate), extra = Mathf.RoundToInt(overlap * Rate);
            var wash = new float[count + extra];
            var rng = new System.Random(201);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < wash.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp1 += (white - lp1) * 0.06f;
                lp2 += (lp1 - lp2) * 0.08f;
                wash[i] = lp2;
            }
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                // All frequencies are multiples of 0.25 Hz, so they complete whole cycles in 4 s
                float t = i / (float)Rate;
                float noise = wash[i];
                if (i < extra)
                {
                    float w = i / (float)extra;
                    noise = wash[i] * w + wash[count + i] * (1f - w);
                }
                float blades = 0.6f + 0.4f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 5f * TwoPi), 2f);
                float hum = Mathf.Sin(t * 46f * TwoPi) * 0.5f + Mathf.Sin(t * 46.5f * TwoPi) * 0.25f
                            + Mathf.Sin(t * 92f * TwoPi) * 0.3f + Mathf.Sin(t * 138f * TwoPi) * 0.12f;
                float whine = Mathf.Sin(t * 420f * TwoPi) * 0.03f * (0.8f + 0.2f * Mathf.Sin(t * 0.5f * TwoPi));
                data[i] = hum * 0.55f + noise * 8f * blades + whine;
            }
            Normalise(data, 0.75f);
            return Clip("EngineLoop", data, Rate);
        }

        // ---------------------------------------------------------------- ambience

        public const float AmbienceSeconds = 24f;

        static AudioClip calm, tense;
        static Task<float[]> calmTask, tenseTask;

        /// <summary>Starts building both ambience beds off the main thread (they are long).</summary>
        public static void PrepareAmbience()
        {
            calmTask ??= Task.Run(BuildCalm);
            tenseTask ??= Task.Run(BuildTense);
        }

        /// <summary>
        /// The calm bed, once built (null until then): a slow, shifting minor drone with a deep ocean wash, glassy
        /// bell notes drifting in and out and a distant, whale-like glide. Seamless 24 s loop.
        /// </summary>
        public static AudioClip Calm => calm != null ? calm : calm = Finish(calmTask, "AmbienceCalm");

        /// <summary>
        /// The fault bed, once built (null until then): the same key and length as <see cref="Calm"/> so the two can
        /// be crossfaded in sync. A racing double heartbeat, a nervous minor-second ostinato, an endlessly rising
        /// Shepard tone and hull creaks.
        /// </summary>
        public static AudioClip Tense => tense != null ? tense : tense = Finish(tenseTask, "AmbienceTense");

        static AudioClip Finish(Task<float[]> task, string name)
        {
            if (task == null || !task.IsCompleted)
                return null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                return null;
            }
            return Clip(name, task.Result, AmbienceRate);
        }

        /// <summary>Rounds a frequency to a whole number of cycles in the ambience loop.</summary>
        static double Q(double f) => Math.Round(f * AmbienceSeconds) / AmbienceSeconds;

        static float[] BuildCalm()
        {
            const int rate = AmbienceRate;
            int count = Mathf.RoundToInt(AmbienceSeconds * rate), extra = rate; // 1 s noise crossfade
            var data = new float[count];
            var rng = new System.Random(301);

            // Drone: D minor add9, each voice a detuned pair breathing on its own slow cycle
            double[] voices = { 73.42, 110.0, 146.83, 174.61, 220.0, 329.63 };
            float[] levels = { 0.30f, 0.20f, 0.16f, 0.12f, 0.08f, 0.05f };
            double[] swellPeriods = { 24, 12, 8, 24, 6, 12 };
            for (int v = 0; v < voices.Length; v++)
            {
                double f1 = Q(voices[v]), f2 = Q(voices[v]) + 2.0 / AmbienceSeconds * (v % 2 == 0 ? 1 : -1);
                double swell = 1.0 / swellPeriods[v];
                double offset = v * 1.7;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)rate;
                    double s = Math.Sin(t * f1 * TwoPi) + Math.Sin(t * f2 * TwoPi + offset) * 0.8;
                    double breathe = 0.55 + 0.45 * Math.Sin(t * swell * TwoPi + offset);
                    data[i] += (float)(s * breathe * levels[v]);
                }
            }

            // Ocean wash: deep filtered noise surging slowly
            var wash = new float[count + extra];
            float a = 0f, b = 0f;
            for (int i = 0; i < wash.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                a += (white - a) * 0.012f;
                b += (a - b) * 0.02f;
                wash[i] = b;
            }
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float n = wash[i];
                if (i < extra)
                {
                    float w = i / (float)extra;
                    n = wash[i] * w + wash[count + i] * (1f - w);
                }
                float surge = 0.6f + 0.25f * Mathf.Sin(t / 8f * TwoPi) + 0.15f * Mathf.Sin(t / 12f * TwoPi + 1f);
                data[i] += n * 22f * surge;
            }

            // Glassy bells drifting in (sine plus an inharmonic partial, long soft attack), tails wrapped round the loop
            (float time, double freq, float level)[] bells =
            {
                (2.5f, 587.33, 0.10f), (6.8f, 880.0, 0.07f), (10.2f, 698.46, 0.08f),
                (14.9f, 659.25, 0.09f), (18.4f, 523.25, 0.08f), (21.6f, 440.0, 0.07f),
            };
            foreach (var bell in bells)
                AddWrapped(data, rate, bell.time, 6f, t =>
                {
                    float env = Mathf.Clamp01(t / 0.35f) * Mathf.Exp(-t * 0.7f);
                    return (float)(Math.Sin(t * bell.freq * TwoPi) + Math.Sin(t * bell.freq * 2.76 * TwoPi) * 0.25) * env * bell.level;
                });

            // A distant whale-like call: a slow vibrato glide, swelling in and out
            AddGlide(data, rate, 11.5f, 4.5f, 0.06f);

            Normalise(data, 0.8f);
            return data;
        }

        static float[] BuildTense()
        {
            const int rate = AmbienceRate;
            int count = Mathf.RoundToInt(AmbienceSeconds * rate), extra = rate / 2;
            var data = new float[count];
            var rng = new System.Random(401);

            // Racing heartbeat: a double thump every 0.6 s (100 bpm, 40 beats per loop)
            const float beat = 0.6f;
            for (int k = 0; k < 40; k++)
            {
                AddWrapped(data, rate, k * beat, 0.3f, t => Thump(t) * 0.55f);
                AddWrapped(data, rate, k * beat + 0.19f, 0.3f, t => Thump(t) * 0.33f);
            }

            // Nervous ostinato: D and E-flat alternating in fast pulses, swelling every 6 s
            double d = Q(146.83), eb = Q(155.56);
            const float step = 0.15f; // 160 steps per loop
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                int n = (int)(t / step);
                float u = t - n * step;
                double f = n % 2 == 0 ? d : eb;
                float pulse = Mathf.Exp(-u * 18f) * Mathf.Clamp01(u * 400f);
                double tone = Math.Sin(t * f * TwoPi) * 0.7 + Math.Sin(t * f * 2 * TwoPi) * 0.25 + Math.Sin(t * f * 3 * TwoPi) * 0.1;
                float swell = 0.55f + 0.45f * Mathf.Sin(t / 6f * TwoPi);
                data[i] += (float)tone * pulse * swell * 0.16f;
            }

            // Shepard tone: octave-spaced sines rising forever, one octave per loop. Phase is written in closed form,
            // so each component ends exactly where the next one starts and the loop has no seam.
            const int components = 6;
            const double f0 = 55.0;
            double k0 = f0 * AmbienceSeconds / Math.Log(2.0);
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)rate;
                double sum = 0;
                for (int c = 0; c < components; c++)
                {
                    double x = (c + t / AmbienceSeconds) / components;
                    double amp = Math.Pow(Math.Sin(Math.PI * x), 2);
                    double phase = k0 * Math.Pow(2.0, c + t / AmbienceSeconds);
                    sum += Math.Sin((phase - Math.Floor(phase)) * TwoPi) * amp;
                }
                data[i] += (float)(sum * 0.07);
            }

            // Hull creaks: resonant, slowly bending groans at fixed points of the loop
            float[] creaks = { 3.1f, 9.7f, 16.4f, 20.9f };
            foreach (float start in creaks)
            {
                double f = 90 + rng.NextDouble() * 70;
                AddWrapped(data, rate, start, 1.8f, t =>
                {
                    float env = Mathf.Sin(Mathf.Clamp01(t / 1.8f) * Mathf.PI);
                    double wobble = Math.Sin(t * 9 * TwoPi) * 3;
                    return (float)(Math.Sin(t * f * TwoPi + wobble) * 0.6 + Math.Sin(t * f * 2.13 * TwoPi + wobble) * 0.3) * env * env * 0.1f;
                });
            }

            // Pressure hiss: brighter noise than the calm wash, tightening and easing
            var hiss = new float[count + extra];
            float hp = 0f, prev = 0f, lp = 0f;
            for (int i = 0; i < hiss.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.96f * (hp + white - prev);
                prev = white;
                lp += (hp - lp) * 0.15f;
                hiss[i] = lp;
            }
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float n = hiss[i];
                if (i < extra)
                {
                    float w = i / (float)extra;
                    n = hiss[i] * w + hiss[count + i] * (1f - w);
                }
                data[i] += n * 0.12f * (0.5f + 0.5f * Mathf.Sin(t / 4f * TwoPi));
            }

            Normalise(data, 0.8f);
            return data;
        }

        static float Thump(float t) =>
            Mathf.Sin(t * Mathf.Lerp(80f, 42f, Mathf.Clamp01(t / 0.08f)) * TwoPi) * Mathf.Exp(-t * 16f) * Mathf.Clamp01(t * 300f);

        /// <summary>Adds a sound starting at a time in the loop; whatever runs past the end wraps round to the start.</summary>
        static void AddWrapped(float[] data, int rate, float start, float seconds, Func<float, float> wave)
        {
            int begin = Mathf.RoundToInt(start * rate), length = Mathf.RoundToInt(seconds * rate);
            for (int j = 0; j < length; j++)
            {
                float t = j / (float)rate;
                data[(begin + j) % data.Length] += wave(t) * Mathf.Clamp01((seconds - t) * 20f);
            }
        }

        /// <summary>The whale-like call: a phase-accumulated glide with vibrato and a soft octave, wrapped round the loop.</summary>
        static void AddGlide(float[] data, int rate, float start, float seconds, float level)
        {
            int begin = Mathf.RoundToInt(start * rate), length = Mathf.RoundToInt(seconds * rate);
            double phase = 0;
            for (int j = 0; j < length; j++)
            {
                double t = j / (double)rate;
                double k = t / seconds;
                double f = 190 + 70 * Math.Sin(k * Math.PI) - 30 * k + 4 * Math.Sin(t * 5.5 * TwoPi);
                phase += f / rate;
                float env = Mathf.Pow(Mathf.Sin((float)k * Mathf.PI), 1.5f);
                double s = Math.Sin(phase * TwoPi) + Math.Sin(phase * 2 * TwoPi) * 0.2;
                data[(begin + j) % data.Length] += (float)s * env * level;
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Builds a one-shot from a function of time and a white-noise sample, with click-free edges.</summary>
        static AudioClip Build(string name, float seconds, int seed, Func<float, float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float edge = Mathf.Clamp01(t * 2000f) * Mathf.Clamp01((seconds - t) * 80f);
                data[i] = Mathf.Clamp(wave(t, white) * edge, -1f, 1f);
            }
            return Clip(name, data, Rate);
        }

        static void Normalise(float[] data, float peakTarget)
        {
            float peak = 1e-4f;
            for (int i = 0; i < data.Length; i++)
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float k = peakTarget / peak;
            for (int i = 0; i < data.Length; i++)
                data[i] *= k;
        }

        static AudioClip Clip(string name, float[] data, int rate)
        {
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
