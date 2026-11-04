using Aquatica.Cockpit;
using Aquatica.Navigation;
using Aquatica.Underwater;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Runs the demo once the crates are buried: the radar shows the nearest crates and the gateway, the console
    /// offers the magnet over a crate, and each crate brought up is scanned on the monitor. What a crate holds is
    /// decided when it comes up, following <see cref="ExpeditionStory.Order"/>, so the story reads in order and the
    /// three Tide Crystals can't be found before the player has seen most of the place, whichever way they go. If the
    /// dive runs long, the next crate holds a crystal. With all three aboard the gateway opens as the submarine nears
    /// it; passing through ends the demo.
    /// </summary>
    public class ExpeditionDirector : MonoBehaviour
    {
        [SerializeField] CrateField crateField;
        [SerializeField] MagnetRig magnet;
        [SerializeField] ExitGateway gatewayPrefab;
        [SerializeField] Shader crtShader;

        [Header("Radar")]
        [Tooltip("Distance (m) shown at the radar's outer ring; anything further sits on the rim.")]
        [SerializeField] float radarRange = 150f;
        [Tooltip("How many of the nearest crates the radar shows.")]
        [SerializeField] int radarCrates = 3;
        [Tooltip("Should match the radar screen's sweep speed (rad/s).")]
        [SerializeField] float sweepSpeed = 1.6f;
        [SerializeField] float pingVolume = 0.35f;

        [Header("Pacing")]
        [Tooltip("After this long (s) without all crystals, the next crate holds one.")]
        [SerializeField] float crystalFallbackAfter = 660f;
        [Tooltip("The radar breaks down once, this many seconds after the given crate is scanned.")]
        [SerializeField] int radarFaultAfterCrate = 5;
        [SerializeField] float radarFaultDelay = 15f;
        [Tooltip("With all crystals aboard, the gateway opens when the submarine comes this close (m).")]
        [SerializeField] float gateOpenDistance = 45f;

        public int Crystals { get; private set; }
        public int Logs { get; private set; }
        public int Artifacts { get; private set; }
        public bool MagnetBusy => magnet != null && magnet.Busy;
        public Crate NearestCrate { get; private set; }
        public float NearestCrateDistance { get; private set; }

        UnderwaterWorld world;
        SubmarineMotor motor;
        NavigationStation station;
        RadarConsole radar;
        ExitGateway gateway;
        ExpeditionScreens screens;
        AudioSource audioSource;
        bool started, ended;
        float startTime;
        int recovered;
        int crystalsGiven, logsGiven, artifactsGiven;
        float radarFaultAt = -1f;
        bool radarFaultDone;
        readonly Vector4[] contacts = new Vector4[RadarConsole.MaxContacts];
        readonly System.Collections.Generic.List<Crate> sorted = new();
        float lastSweepBehind;

        void Update()
        {
            if (!started && !TryStart())
                return;
            if (ended)
                return;
            TrackCrates();
            UpdateRadar();
            UpdateGateway();
            UpdateRadarFault();
        }

        bool TryStart()
        {
            if (crateField == null || !crateField.IsReady || !crateField.TryGetLayout(out var layout))
                return false;
            world = GetComponentInParent<UnderwaterWorld>();
            motor = world.GetComponentInChildren<SubmarineMotor>();
            station = world.GetComponentInChildren<NavigationStation>();
            radar = world.GetComponentInChildren<RadarConsole>();

            screens = new ExpeditionScreens(this);
            if (station != null)
                station.Overlay = screens;
            magnet.Captured += OnCaptured;

            if (layout.HasGate && gatewayPrefab != null)
            {
                gateway = Instantiate(gatewayPrefab);
                gateway.name = "ExitGateway";
                gateway.Place(world.SeafloorTransform, layout.GatePosition, layout.GateRotation, motor.transform,
                    LayerMask.NameToLayer(UnderwaterWorld.TerrainLayerName));
                gateway.Crossed += OnGatewayCrossed;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            var cam = Camera.main;
            if (cam != null && FindAnyObjectByType<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();

            startTime = Time.time;
            started = true;
            return true;
        }

        // ---------------------------------------------------------------- crates and the magnet

        void TrackCrates()
        {
            var hang = motor.transform.position;
            NearestCrate = null;
            NearestCrateDistance = float.MaxValue;
            foreach (var crate in crateField.Crates)
            {
                if (crate.Captured)
                    continue;
                var p = crate.transform.position;
                float d = new Vector2(p.x - hang.x, p.z - hang.z).magnitude;
                if (d < NearestCrateDistance)
                {
                    NearestCrateDistance = d;
                    NearestCrate = crate;
                }
            }
        }

        public bool CanReach(Crate crate, out string hint) => magnet.CanReach(crate, out hint);

        /// <summary>Console tapped: engage the magnet if a crate is in reach (true if the tap was used).</summary>
        public bool TryEngageMagnet()
        {
            if (ended || MagnetBusy || NearestCrate == null || !magnet.CanReach(NearestCrate, out _))
                return false;
            Play(Tones.Blip);
            magnet.Engage(NearestCrate);
            return true;
        }

        void OnCaptured(Crate crate)
        {
            recovered++;
            var content = NextContent();
            string header = content.kind switch
            {
                CrateContentKind.Log => $"LOG {logsGiven}/{ExpeditionStory.TotalLogs}",
                CrateContentKind.Artifact => $"ARTIFACT {artifactsGiven}/{ExpeditionStory.Artifacts.Length}",
                _ => $"CRYSTAL {crystalsGiven}/{ExpeditionStory.CrystalCount}",
            };
            if (content.kind == CrateContentKind.Crystal && crystalsGiven >= ExpeditionStory.CrystalCount)
                content.text += " ALL THREE ABOARD. THE GATE IS ANSWERING: FOLLOW THE BEACON.";
            Play(Tones.Blip);
            screens.ShowCrate(content, header, () => Reveal(content));
        }

        void Reveal(CrateContent content)
        {
            switch (content.kind)
            {
                case CrateContentKind.Crystal:
                    Crystals++;
                    if (gateway != null)
                        gateway.SetCrystals(Crystals);
                    Play(Tones.Chime);
                    break;
                case CrateContentKind.Log:
                    Logs++;
                    Play(Tones.Reveal);
                    break;
                default:
                    Artifacts++;
                    Play(Tones.Reveal);
                    break;
            }
            if (recovered == radarFaultAfterCrate && !radarFaultDone)
                radarFaultAt = Time.time + radarFaultDelay;
        }

        /// <summary>What the crate just recovered holds (see the class summary).</summary>
        CrateContent NextContent()
        {
            int crystalsLeft = ExpeditionStory.CrystalCount - crystalsGiven;
            int cratesLeft = 0;
            foreach (var c in crateField.Crates)
                if (!c.Captured)
                    cratesLeft++;
            // Never run out of crates before the crystals, and don't let a long dive drag on
            bool force = crystalsLeft > 0 && (cratesLeft < crystalsLeft || Time.time - startTime > crystalFallbackAfter);

            var planned = recovered - 1 < ExpeditionStory.Order.Length ? ExpeditionStory.Order[recovered - 1] : CrateContentKind.Artifact;
            var candidates = force
                ? new[] { CrateContentKind.Crystal }
                : new[] { planned, CrateContentKind.Log, CrateContentKind.Artifact, CrateContentKind.Crystal };
            foreach (var kind in candidates)
            {
                switch (kind)
                {
                    case CrateContentKind.Crystal when crystalsGiven < ExpeditionStory.CrystalCount:
                        return ExpeditionStory.Make(kind, crystalsGiven++);
                    case CrateContentKind.Log when logsGiven < ExpeditionStory.Logs.Length:
                        return ExpeditionStory.Make(kind, logsGiven++);
                    case CrateContentKind.Artifact when artifactsGiven < ExpeditionStory.Artifacts.Length:
                        return ExpeditionStory.Make(kind, artifactsGiven++);
                }
            }
            // Everything's been found: an empty crate
            return new CrateContent { kind = CrateContentKind.Artifact, index = -1, title = "EMPTY", text = "NOTHING BUT SILT AND A TORN LABEL." };
        }

        // ---------------------------------------------------------------- radar

        void UpdateRadar()
        {
            if (radar == null)
                return;
            sorted.Clear();
            foreach (var crate in crateField.Crates)
                if (!crate.Captured)
                    sorted.Add(crate);
            var sub = motor.transform.position;
            sorted.Sort((a, b) => Flat(a.transform.position - sub).sqrMagnitude.CompareTo(Flat(b.transform.position - sub).sqrMagnitude));

            int count = 0;
            for (int i = 0; i < sorted.Count && i < radarCrates && count < contacts.Length; i++)
                contacts[count++] = Contact(sorted[i].transform.position, 1f, i == 0 ? 1f : 0.75f);
            if (gateway != null && count < contacts.Length)
                contacts[count++] = Contact(gateway.transform.position, 2f, Crystals >= ExpeditionStory.CrystalCount ? 1.2f : 0.6f);
            radar.SetContacts(contacts, count);

            // Ping as the sweep passes the nearest crate (the shader's sweep runs clockwise from level load)
            if (sorted.Count > 0 && !radar.IsFaulty && !MagnetBusy)
            {
                var c = contacts[0];
                float sweep = -Time.timeSinceLevelLoad * sweepSpeed;
                float behind = Mathf.Repeat((Mathf.Atan2(c.y, c.x) - sweep) / (Mathf.PI * 2f), 1f);
                if (behind < lastSweepBehind - 0.5f)
                {
                    float near = 1f - Mathf.Clamp01(Flat(sorted[0].transform.position - sub).magnitude / radarRange);
                    Play(Tones.Ping, pingVolume * (0.35f + 0.65f * near));
                }
                lastSweepBehind = behind;
            }
        }

        /// <summary>A radar blip for a world position: heading up, starboard right, clamped to the rim when out of range.</summary>
        Vector4 Contact(Vector3 worldPosition, float kind, float brightness)
        {
            var offset = Quaternion.Inverse(Quaternion.Euler(0f, motor.transform.eulerAngles.y, 0f)) * (worldPosition - motor.transform.position);
            var p = new Vector2(offset.x, offset.z) / radarRange * 0.9f;
            if (p.magnitude > 0.9f)
            {
                p = p.normalized * 0.9f;
                brightness *= 0.6f;
            }
            return new Vector4(p.x, p.y, kind, brightness);
        }

        void UpdateRadarFault()
        {
            if (radarFaultAt < 0f || radarFaultDone || Time.time < radarFaultAt || MagnetBusy || screens.Reading)
                return;
            radarFaultDone = true;
            if (radar != null)
                radar.BeginFault();
        }

        // ---------------------------------------------------------------- gateway and the end

        void UpdateGateway()
        {
            if (gateway == null || gateway.IsOpen || gateway.IsOpening || Crystals < ExpeditionStory.CrystalCount)
                return;
            if (Flat(gateway.transform.position - motor.transform.position).magnitude > gateOpenDistance)
                return;
            gateway.Open();
            Play(Tones.Rumble);
            Logs++;
            var (title, text) = ExpeditionStory.FinalLog;
            screens.ShowText(new CrateContent { kind = CrateContentKind.Log, index = ExpeditionStory.Logs.Length, title = title, text = text },
                $"LOG {Logs}/{ExpeditionStory.TotalLogs}");
        }

        void OnGatewayCrossed()
        {
            ended = true;
            motor.Anchored = true;
            radar?.SetContacts(contacts, 0);
            var interactor = FindAnyObjectByType<CockpitInteractor>();
            if (interactor != null)
                interactor.enabled = false;
            ExpeditionEnding.Play(crtShader, Logs, Artifacts, Time.time - startTime);
        }

        void Play(AudioClip clip, float volume = 0.6f)
        {
            if (audioSource != null)
                audioSource.PlayOneShot(clip, volume);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        void OnDestroy()
        {
            if (magnet != null)
                magnet.Captured -= OnCaptured;
            if (station != null && ReferenceEquals(station.Overlay, screens))
                station.Overlay = null;
        }
    }
}
