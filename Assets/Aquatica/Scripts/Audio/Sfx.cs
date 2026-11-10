using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// Fire-and-forget one-shots from a small pool of audio sources. Sounds played at a point are partly
    /// spatialised, so a click on the left of the cockpit is heard a little from the left without getting quiet;
    /// sounds without a point (controls carried with the camera) play flat. Pitch varies slightly per play so
    /// repeated presses don't sound machine-identical.
    /// </summary>
    public static class Sfx
    {
        const int PoolSize = 10;
        static AudioSource[] pool;
        static int next;

        public static void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0.04f) =>
            Play(clip, null, volume, pitchJitter);

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0.04f) =>
            Play(clip, position, volume, pitchJitter);

        static void Play(AudioClip clip, Vector3? position, float volume, float pitchJitter)
        {
            if (clip == null || !EnsurePool())
                return;
            var source = pool[next];
            next = (next + 1) % PoolSize;
            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            if (position.HasValue)
            {
                source.transform.position = position.Value;
                source.spatialBlend = 0.65f;
            }
            else
                source.spatialBlend = 0f;
            source.Play();
        }

        static bool EnsurePool()
        {
            if (pool != null && pool[0] != null)
                return true;
            if (!Application.isPlaying)
                return false;
            var root = new GameObject("Sfx");
            Object.DontDestroyOnLoad(root);
            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 1f;
                s.maxDistance = 12f;
                s.dopplerLevel = 0f;
                s.spread = 60f;
                pool[i] = s;
            }
            next = 0;
            return true;
        }
    }
}
