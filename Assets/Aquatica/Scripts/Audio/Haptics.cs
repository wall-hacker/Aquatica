using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// Phone vibration with strength control. On Android 8+ (API 26) it uses VibrationEffect, so a light brush
    /// can be a faint tick and a fault a hard rumble; older phones (and ones without amplitude control) get plain
    /// on/off pulses, with very light requests skipped so brushing doesn't buzz at full strength. Does nothing in
    /// the editor or on other platforms.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Master switch (e.g. for a settings toggle).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Requests weaker than this are dropped on phones without amplitude control.</summary>
        const float OnOffThreshold = 0.45f;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static bool initialised, hasAmplitude, modern;
        static float busyUntil;
        static float busyStrength;

        static bool Init()
        {
            if (initialised)
                return vibrator != null;
            initialised = true;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                {
                    vibrator = null;
                    return false;
                }
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                modern = version.GetStatic<int>("SDK_INT") >= 26;
                if (modern)
                {
                    effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    hasAmplitude = vibrator.Call<bool>("hasAmplitudeControl");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Aquatica] Vibration unavailable: {e.Message}");
                vibrator = null;
            }
            return vibrator != null;
        }
#endif

        /// <summary>One pulse: strength 0..1, length in seconds. A weaker pulse never cuts a stronger one short.</summary>
        public static void Pulse(float strength, float seconds)
        {
            if (!Enabled || strength <= 0.01f || seconds <= 0f)
                return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Init())
                return;
            float now = Time.unscaledTime;
            if (now < busyUntil && strength < busyStrength)
                return;
            busyUntil = now + seconds;
            busyStrength = strength;
            long ms = Mathf.Max(8, Mathf.RoundToInt(seconds * 1000f));
            try
            {
                if (modern)
                {
                    int amplitude = hasAmplitude ? Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 1, 255) : -1; // -1 = device default
                    if (!hasAmplitude && strength < OnOffThreshold)
                        return;
                    using var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude);
                    vibrator.Call("vibrate", effect);
                }
                else if (strength >= OnOffThreshold)
                    vibrator.Call("vibrate", ms);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Aquatica] Vibration failed: {e.Message}");
            }
#endif
        }

        /// <summary>
        /// A pattern: alternating on/off segments (seconds, starting with "on") with a strength per "on" segment.
        /// Replaces whatever is playing.
        /// </summary>
        public static void Pattern(float[] segments, float[] strengths)
        {
            if (!Enabled || segments == null || segments.Length == 0)
                return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Init())
                return;
            try
            {
                // Android waveforms start with an "off" delay
                var timings = new long[segments.Length + 1];
                var amplitudes = new int[segments.Length + 1];
                float total = 0f, peak = 0f;
                for (int i = 0; i < segments.Length; i++)
                {
                    timings[i + 1] = Mathf.Max(1, Mathf.RoundToInt(segments[i] * 1000f));
                    total += segments[i];
                    bool on = i % 2 == 0;
                    float s = on && strengths != null && i / 2 < strengths.Length ? strengths[i / 2] : on ? 1f : 0f;
                    peak = Mathf.Max(peak, s);
                    amplitudes[i + 1] = on ? (hasAmplitude ? Mathf.Clamp(Mathf.RoundToInt(s * 255f), 1, 255) : 255) : 0;
                }
                busyUntil = Time.unscaledTime + total;
                busyStrength = peak;
                if (modern)
                {
                    using var effect = hasAmplitude
                        ? effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1)
                        : effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1);
                    vibrator.Call("vibrate", effect);
                }
                else
                    vibrator.Call("vibrate", timings, -1);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Aquatica] Vibration failed: {e.Message}");
            }
#endif
        }

        /// <summary>Stops any vibration.</summary>
        public static void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Init())
                return;
            busyUntil = 0f;
            try { vibrator.Call("cancel"); }
            catch (System.Exception) { }
#endif
        }

#if UNITY_ANDROID
        // Referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the Android manifest.
        static bool neverTrue;
        internal static void EnsurePermission()
        {
            if (neverTrue)
                Handheld.Vibrate();
        }
#endif
    }
}
