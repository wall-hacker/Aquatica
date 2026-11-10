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

        static readonly AudioClip[] notes = new AudioClip[4];
        static readonly float[] noteFrequencies = { 523.25f, 659.25f, 783.99f, 1046.5f };
        static AudioClip buzz;

        /// <summary>One of four rising notes (a soft square beep), e.g. one per button of a panel.</summary>
        public static AudioClip Note(int index)
        {
            index = Mathf.Clamp(index, 0, notes.Length - 1);
            float f = noteFrequencies[index];
            return notes[index] ??= Build("Note" + index, 0.32f, t =>
                (Mathf.Sin(t * f * Mathf.PI * 2f) * 0.7f + Square(t * f) * 0.12f) * Mathf.Exp(-t * 7f) * 0.45f);
        }

        /// <summary>Wrong input: a low, harsh double buzz.</summary>
        public static AudioClip Buzz => buzz ??= Build("Buzz", 0.5f, t =>
            Square(t * 110f) * (t < 0.2f || t > 0.27f ? 1f : 0f) * Mathf.Exp(-t * 2f) * 0.22f);

        static AudioClip klaxonBeep, klaxonWhoop, klaxonBuzzer, klaxonHorn;

        /// <summary>Fault alarm (loop): an electronic two-tone beep, high then low, then a pause.</summary>
        public static AudioClip KlaxonBeep => klaxonBeep ??= Build("KlaxonBeep", 1.2f, t =>
        {
            float f = t < 0.28f ? 960f : 720f;
            float tone = Mathf.Sin(t * f * Mathf.PI * 2f) * 0.8f + Square(t * f) * 0.12f;
            return tone * (Gate(t, 0f, 0.25f) + Gate(t, 0.3f, 0.55f)) * 0.4f;
        });

        /// <summary>Fault alarm (loop): a siren whoop sweeping up, then a pause.</summary>
        public static AudioClip KlaxonWhoop => klaxonWhoop ??= BuildSweep("KlaxonWhoop", 1.5f, 0.9f, 320f, 760f, 0f, 0.4f);

        /// <summary>Fault alarm (loop): three rasping buzzer pulses, then a pause.</summary>
        public static AudioClip KlaxonBuzzer => klaxonBuzzer ??= Build("KlaxonBuzzer", 1.4f, t =>
        {
            float buzz = Square(t * 180f) * 0.6f + Square(t * 361f) * 0.25f + Mathf.Sin(t * 540f * Mathf.PI * 2f) * 0.2f;
            return buzz * (Gate(t, 0f, 0.14f) + Gate(t, 0.24f, 0.38f) + Gate(t, 0.48f, 0.62f)) * 0.3f;
        });

        /// <summary>Fault alarm (loop): an old "aooga" horn, a growling rising blare, then a pause.</summary>
        public static AudioClip KlaxonHorn => klaxonHorn ??= BuildSweep("KlaxonHorn", 1.6f, 0.75f, 150f, 330f, 30f, 0.35f);

        /// <summary>
        /// A rising sweep (a phase-accumulated sawtooth softened by a sine, optionally growling) followed by silence,
        /// so it loops as a repeating alarm.
        /// </summary>
        static AudioClip BuildSweep(string name, float seconds, float sweep, float from, float to, float growl, float level)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float k = Mathf.Clamp01(t / sweep);
                float f = Mathf.Lerp(from, to, Mathf.Sin(k * Mathf.PI * 0.5f));
                phase += f / Rate;
                float saw = Mathf.Repeat(phase, 1f) * 2f - 1f;
                float tone = Mathf.Sin(phase * Mathf.PI * 2f) * 0.6f + saw * 0.4f;
                if (growl > 0f)
                    tone *= 0.75f + 0.25f * Mathf.Sin(t * growl * Mathf.PI * 2f);
                data[i] = tone * Gate(t, 0f, sweep, 0.03f) * level;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>1 between start and end (seconds), with short fades so the edges don't click.</summary>
        static float Gate(float t, float start, float end, float fade = 0.01f) =>
            Mathf.Clamp01((t - start) / fade) * Mathf.Clamp01((end - t) / fade);

        static AudioClip valveOpen, pumpLoop, valveClose, inflow, bubbles;

        /// <summary>Repressurising starts: a heavy valve clunk and a burst of air rushing in.</summary>
        public static AudioClip ValveOpen => valveOpen ??= BuildValve("ValveOpen", 1.3f, true);

        /// <summary>Repressurising runs: a pump humming under a steady hiss of air (seamless loop).</summary>
        public static AudioClip PumpLoop => pumpLoop ??= BuildPumpLoop();

        /// <summary>Repressurising done: the valve thunks shut and the pressure settles with a falling sigh.</summary>
        public static AudioClip ValveClose => valveClose ??= BuildValve("ValveClose", 1.6f, false);

        /// <summary>Water flooding in: rushing, gurgling, splashing (seamless loop).</summary>
        public static AudioClip Inflow => inflow ??= BuildInflow();

        /// <summary>Going under: a burst of bubbles over a muffled rumble.</summary>
        public static AudioClip Bubbles => bubbles ??= BuildBubbles();

        /// <summary>A valve clunk, then air: rushing in and settling (opening) or sighing away (closing).</summary>
        static AudioClip BuildValve(string name, float seconds, bool opening)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            var rng = new System.Random(opening ? 21 : 22);
            float hp = 0f, prev = 0f, lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                // Airy hiss: high-passed noise, then a low-pass whose cutoff sweeps (bright rush, darker sigh)
                hp = 0.97f * (hp + white - prev);
                prev = white;
                float k = Mathf.Clamp01(t / seconds);
                lp += (hp - lp) * (opening ? Mathf.Lerp(0.55f, 0.3f, k) : Mathf.Lerp(0.45f, 0.04f, k));
                float env = opening
                    ? Mathf.Clamp01((t - 0.08f) / 0.12f) * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((t - 0.2f) / (seconds - 0.2f)))
                    : Mathf.Clamp01((t - 0.05f) / 0.05f) * Mathf.Exp(-Mathf.Max(0f, t - 0.1f) * 2.4f);
                float air = lp * 1.4f * env;
                // The valve itself: a low metallic clunk with a click on top
                float clunk = (Mathf.Sin(t * 58f * Mathf.PI * 2f) + Mathf.Sin(t * 141f * Mathf.PI * 2f) * 0.35f) * Mathf.Exp(-t * 22f) * 0.6f
                              + white * Mathf.Exp(-t * 150f) * 0.35f;
                // Closing: the pressure settling, a soft tone gliding down
                float settle = opening ? 0f : Mathf.Sin(t * Mathf.Lerp(260f, 110f, Mathf.Clamp01(t / 1.2f)) * Mathf.PI * 2f) * 0.06f * env;
                float edge = Mathf.Clamp01(t * 400f) * Mathf.Clamp01((seconds - t) * 8f);
                data[i] = Mathf.Clamp((air + clunk + settle) * edge, -1f, 1f);
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip BuildPumpLoop()
        {
            // 4 s: the 48 Hz hum and the 6 Hz throb complete whole cycles, so only the noise needs crossfading
            const float seconds = 4f, overlap = 0.3f;
            int count = Mathf.CeilToInt(seconds * Rate), extra = Mathf.CeilToInt(overlap * Rate);
            var hiss = new float[count + extra];
            var rng = new System.Random(31);
            float hp = 0f, prev = 0f, lp = 0f;
            for (int i = 0; i < hiss.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.97f * (hp + white - prev);
                prev = white;
                lp += (hp - lp) * 0.35f;
                hiss[i] = lp;
            }
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float noise = hiss[i];
                if (i < extra)
                {
                    float w = i / (float)extra;
                    noise = hiss[i] * w + hiss[count + i] * (1f - w);
                }
                float throb = 0.8f + 0.2f * Mathf.Sin(t * 6f * Mathf.PI * 2f);
                float hum = (Mathf.Sin(t * 48f * Mathf.PI * 2f) + Mathf.Sin(t * 96f * Mathf.PI * 2f) * 0.5f + Mathf.Sin(t * 144f * Mathf.PI * 2f) * 0.25f) * 0.22f * throb;
                data[i] = Mathf.Clamp(hum + noise * 0.55f, -1f, 1f);
            }
            var clip = AudioClip.Create("PumpLoop", count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Water flooding in: a surging rush of turbulent water over a low churn, full of gurgling bubbles (each a tone
        /// gliding up as it rises, like real bubbles) and the odd splash.
        /// </summary>
        static AudioClip BuildInflow()
        {
            // 4 s: the surges (0.5 and 0.75 Hz) complete whole cycles; the rest is crossfaded tail-to-head
            const float seconds = 4f, overlap = 0.3f;
            int count = Mathf.CeilToInt(seconds * Rate), extra = Mathf.CeilToInt(overlap * Rate);
            int total = count + extra;
            var raw = new float[total];
            var rng = new System.Random(11);

            // Rush, churn and splashes from filtered noise
            float hp = 0f, prev = 0f, band = 0f, churn = 0f, churn2 = 0f, splash = 0f, splashHp = 0f, splashPrev = 0f;
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.95f * (hp + white - prev);
                prev = white;
                band += (hp - band) * 0.22f;
                churn += (white - churn) * 0.02f;
                churn2 += (churn - churn2) * 0.05f;
                if (rng.NextDouble() < 0.00003)
                    splash = 0.6f + (float)rng.NextDouble() * 0.4f;
                splash *= 0.99955f;
                splashHp = 0.9f * (splashHp + white - splashPrev);
                splashPrev = white;
                float surge = 0.65f + 0.35f * (0.6f * Mathf.Sin(t * 0.5f * Mathf.PI * 2f) + 0.4f * Mathf.Sin(t * 0.75f * Mathf.PI * 2f + 1f));
                raw[i] = band * 0.9f * surge + churn2 * 4f * surge + splashHp * splash * 0.35f;
            }

            // Gurgling bubbles
            int bubbleCount = Mathf.RoundToInt(total / (float)Rate * 32f);
            for (int b = 0; b < bubbleCount; b++)
            {
                int start = rng.Next(total);
                float f0 = 280f + (float)rng.NextDouble() * 1100f;
                float life = 0.02f + (float)rng.NextDouble() * 0.05f;
                float amp = 0.04f + (float)rng.NextDouble() * 0.12f;
                int length = Mathf.CeilToInt(life * Rate);
                float phase = 0f;
                for (int j = 0; j < length && start + j < total; j++)
                {
                    float bt = j / (float)Rate;
                    float f = f0 * (1f + 0.6f * bt / life);
                    phase += f / Rate;
                    raw[start + j] += Mathf.Sin(phase * Mathf.PI * 2f) * amp * Mathf.Exp(-bt / life * 3f) * Mathf.Clamp01(bt * 2000f);
                }
            }

            // Crossfade the tail into the head so the loop has no seam, then normalise
            var data = new float[count];
            float peak = 0.001f;
            for (int i = 0; i < count; i++)
            {
                float v = raw[i];
                if (i < extra)
                {
                    float w = i / (float)extra;
                    v = raw[i] * w + raw[count + i] * (1f - w);
                }
                data[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < count; i++)
                data[i] *= 0.8f / peak;
            var clip = AudioClip.Create("Inflow", count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip BuildBubbles()
        {
            const float seconds = 2.2f;
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            var rng = new System.Random(3);
            var starts = new float[18];
            var pitches = new float[18];
            for (int b = 0; b < starts.Length; b++)
            {
                starts[b] = (float)rng.NextDouble() * 1.7f;
                pitches[b] = 220f + (float)rng.NextDouble() * 500f;
            }
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.02f;
                float v = lp * 2.5f * Mathf.Clamp01(t * 4f) * Mathf.Clamp01((seconds - t) * 1.5f);
                for (int b = 0; b < starts.Length; b++)
                {
                    float bt = t - starts[b];
                    if (bt > 0f && bt < 0.06f)
                        v += Mathf.Sin(bt * Mathf.Lerp(pitches[b], pitches[b] * 2.2f, bt / 0.06f) * Mathf.PI * 2f) * Mathf.Sin(bt / 0.06f * Mathf.PI) * 0.3f;
                }
                data[i] = Mathf.Clamp(v * Mathf.Clamp01(t * 400f), -1f, 1f);
            }
            var clip = AudioClip.Create("Bubbles", count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

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
