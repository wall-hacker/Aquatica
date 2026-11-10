using System.Collections.Generic;
using Aquatica.Expedition;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// A directional klaxon for every <see cref="CockpitDevice"/>: a looping alarm played from the device itself as 3D
    /// sound, so the player can turn towards a fault by ear. Each kind of device has its own call (radar: two-tone beep,
    /// valve: siren whoop, switch panel: buzzer, key switches: horn), so two faults at once can be told apart. A klaxon
    /// starts loud for a few seconds to get attention, then settles; it keeps down while the device says the player is
    /// working on it (<see cref="CockpitDevice.AlarmDucked"/>), and stops with the repair or when
    /// <see cref="CockpitSystems.AlarmsMuted"/> is set.
    /// </summary>
    [RequireComponent(typeof(CockpitSystems))]
    public class FaultKlaxons : MonoBehaviour
    {
        [Tooltip("Volume for the first few seconds of a fault.")]
        [SerializeField] float attentionVolume = 0.5f;
        [Tooltip("Seconds at the attention volume before settling.")]
        [SerializeField] float attentionTime = 4f;
        [Tooltip("Volume once settled.")]
        [SerializeField] float settledVolume = 0.25f;
        [Tooltip("Volume while the player is working on the device.")]
        [SerializeField] float duckedVolume = 0.06f;
        [SerializeField] float fadeSpeed = 1.5f;
        [Tooltip("Full volume within this distance (m); quieter beyond, so nearer faults are louder.")]
        [SerializeField] float minDistance = 0.6f;
        [SerializeField] float maxDistance = 8f;

        class Klaxon
        {
            public CockpitDevice device;
            public AudioSource source;
            public float faultTime;
        }

        CockpitSystems systems;
        readonly List<Klaxon> klaxons = new();

        void Awake()
        {
            systems = GetComponent<CockpitSystems>();
            systems.FaultStarted += OnFaultStarted;
        }

        void OnDestroy()
        {
            if (systems != null)
                systems.FaultStarted -= OnFaultStarted;
        }

        void Start()
        {
            foreach (var device in systems.Devices)
            {
                var go = new GameObject("Klaxon");
                go.transform.SetParent(device.transform, false);
                // From the middle of the device's visible parts (the interlock sits between its two key switches)
                go.transform.position = device.Location;
                var source = go.AddComponent<AudioSource>();
                source.clip = ClipFor(device);
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.dopplerLevel = 0f;
                source.spread = 0f;
                klaxons.Add(new Klaxon { device = device, source = source });
            }
        }

        static AudioClip ClipFor(CockpitDevice device) => device switch
        {
            RadarConsole => Tones.KlaxonBeep,
            ValveStation => Tones.KlaxonWhoop,
            SwitchPanel => Tones.KlaxonBuzzer,
            _ => Tones.KlaxonHorn,
        };

        void OnFaultStarted(CockpitDevice device)
        {
            foreach (var k in klaxons)
            {
                if (k.device != device)
                    continue;
                k.faultTime = Time.time;
                // Start the call from its beginning
                k.source.Stop();
                k.source.time = 0f;
            }
        }

        void Update()
        {
            foreach (var k in klaxons)
            {
                float target = 0f;
                if (k.device.IsFaulty && !systems.AlarmsMuted)
                    target = k.device.AlarmDucked ? duckedVolume
                        : Time.time - k.faultTime < attentionTime ? attentionVolume
                        : settledVolume;
                var s = k.source;
                // Snap up at the start of a fault, fade otherwise
                s.volume = target > 0f && !s.isPlaying ? target : Mathf.MoveTowards(s.volume, target, fadeSpeed * Time.deltaTime);
                if (s.volume > 0f && !s.isPlaying)
                    s.Play();
                else if (s.volume <= 0f && s.isPlaying)
                    s.Stop();
            }
        }
    }
}
