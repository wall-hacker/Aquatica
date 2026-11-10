using Aquatica.Cockpit;
using Aquatica.Expedition;
using UnityEngine;

namespace Aquatica.Audio
{
    /// <summary>
    /// The underwater soundscape: a calm, enigmatic bed that loops seamlessly while exploring, and a frantic one
    /// (racing heartbeat, nervous ostinato, an endlessly rising tone) that comes in while faults are active. Both
    /// beds share a key and a loop length and run locked together, so the change is a smooth crossfade rather
    /// than a cut; more faults at once push it further. Fades in when the world is entered and out when the
    /// expedition ends.
    /// </summary>
    public class OceanAmbience : MonoBehaviour
    {
        [SerializeField] float calmVolume = 0.32f;
        [SerializeField] float tenseVolume = 0.38f;
        [Tooltip("How much of the calm bed stays under the tense one during a fault.")]
        [Range(0f, 1f)] [SerializeField] float calmUnderFault = 0.35f;
        [SerializeField] float fadeInTime = 5f;
        [SerializeField] float fadeOutTime = 3f;
        [Tooltip("Seconds to swing into the fault mix, and back out once everything is repaired.")]
        [SerializeField] float toFaultTime = 2.5f;
        [SerializeField] float fromFaultTime = 6f;

        AudioSource calm, tense;
        CockpitSystems systems;
        ExpeditionDirector expedition;
        float master, mix;

        void Awake() => SoundBank.PrepareAmbience();

        void Update()
        {
            if (calm == null && !TryStart())
                return;

            float dt = Time.deltaTime;
            if (systems == null)
                systems = GetComponentInChildren<CockpitSystems>();
            if (expedition == null)
                expedition = FindAnyObjectByType<ExpeditionDirector>();

            bool ended = expedition != null && expedition.Ended;
            master = Mathf.MoveTowards(master, ended ? 0f : 1f, dt / Mathf.Max(0.1f, ended ? fadeOutTime : fadeInTime));

            int faults = systems != null && !ended ? systems.ActiveFaults : 0;
            float targetMix = faults <= 0 ? 0f : Mathf.Min(1f, 0.8f + 0.2f * (faults - 1));
            mix = Mathf.MoveTowards(mix, targetMix, dt / Mathf.Max(0.1f, targetMix > mix ? toFaultTime : fromFaultTime));

            float m = Mathf.SmoothStep(0f, 1f, mix);
            calm.volume = master * calmVolume * Mathf.Lerp(1f, calmUnderFault, m);
            tense.volume = master * tenseVolume * m;
        }

        /// <summary>Waits for both beds to be built, then starts them on the same audio frame so they stay locked.</summary>
        bool TryStart()
        {
            var calmClip = SoundBank.Calm;
            var tenseClip = SoundBank.Tense;
            if (calmClip == null || tenseClip == null)
                return false;
            calm = NewSource(calmClip);
            tense = NewSource(tenseClip);
            double at = AudioSettings.dspTime + 0.1;
            calm.PlayScheduled(at);
            tense.PlayScheduled(at);
            return true;
        }

        AudioSource NewSource(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 32;
            return source;
        }
    }
}
