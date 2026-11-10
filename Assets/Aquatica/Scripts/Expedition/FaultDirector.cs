using System;
using Aquatica.Cockpit;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Breaks cockpit devices during the dive, semi-scripted and semi-random. The phase table, keyed on crates
    /// recovered, sets how long the quiet stretches between faults are, how hard each fault is and how many come at
    /// at once (all the faults of a wave hit together). Within a phase, the exact timing and the devices are random. Faults never start while the magnet is
    /// working or a crate is being read, nor for a while after. The first fault is always the scripted one (the radar).
    /// When the gateway opens, a double fault hits at once, and from then on every wave is a hard double fault.
    /// The next wave's timer only runs while every device is working.
    /// </summary>
    [RequireComponent(typeof(ExpeditionDirector))]
    public class FaultDirector : MonoBehaviour
    {
        [Serializable]
        public struct Phase
        {
            [Tooltip("This phase applies once this many crates have been recovered.")]
            public int fromCrate;
            [Tooltip("Seconds of calm (min, max) before the next wave; 0 = no faults in this phase.")]
            public Vector2 interval;
            [Range(1, CockpitDevice.MaxDifficulty)] public int difficulty;
            [Tooltip("How many devices a wave breaks (min, max).")]
            public Vector2Int faults;
            [Tooltip("Multiplier on how fast the cabin floods while faults are active.")]
            public float floodRate;
        }

        [Tooltip("The scripted first fault (the radar). Leave empty for a random device.")]
        [SerializeField] CockpitDevice firstFault;

        [Tooltip("Must be sorted by fromCrate.")]
        [SerializeField] Phase[] phases =
        {
            new() { fromCrate = 0, interval = Vector2.zero, difficulty = 1, faults = new Vector2Int(1, 1), floodRate = 1f },
            new() { fromCrate = 2, interval = new Vector2(30f, 50f), difficulty = 1, faults = new Vector2Int(1, 1), floodRate = 1f },
            new() { fromCrate = 3, interval = new Vector2(100f, 150f), difficulty = 1, faults = new Vector2Int(1, 1), floodRate = 1f },
            new() { fromCrate = 6, interval = new Vector2(70f, 100f), difficulty = 2, faults = new Vector2Int(1, 2), floodRate = 1.25f },
            new() { fromCrate = 9, interval = new Vector2(45f, 70f), difficulty = 3, faults = new Vector2Int(2, 2), floodRate = 1.5f },
        };

        [Tooltip("Once the gateway opens (completionist run): every wave is a hard double fault.")]
        [SerializeField] Phase afterGate = new() { interval = new Vector2(35f, 50f), difficulty = 3, faults = new Vector2Int(2, 2), floodRate = 2f };

        [Tooltip("No fault starts for this long (s) after a crate has been read.")]
        [SerializeField] float quietAfterPickup = 20f;
        [Tooltip("Seconds after the gateway starts opening before its double fault.")]
        [SerializeField] float gateFaultDelay = 2.5f;
        [SerializeField] bool logFaults = true;

        public CockpitSystems Systems { get; private set; }
        /// <summary>The phase in effect now (the gate phase once the gateway has opened).</summary>
        public Phase Current => gatePhase ? afterGate : PhaseFor(expedition.Recovered);
        /// <summary>Faults to repair right now.</summary>
        public int ActiveFaults => Systems != null ? Systems.ActiveFaults : 0;
        /// <summary>
        /// How fast the cabin floods now (0 while everything works): the phase multiplier, plus half again for each
        /// extra fault, so a double fault floods 1.5x as fast as a single one.
        /// </summary>
        public float FloodRate => ActiveFaults > 0 ? (1f + 0.5f * (ActiveFaults - 1)) * Current.floodRate : 0f;

        ExpeditionDirector expedition;
        bool ready, stopped, gatePhase, firstDone;
        float nextWaveAt = -1f;
        float gateWaveAt = -1f;
        float lastBusy = float.NegativeInfinity;
        CockpitDevice lastBroken;

        void Awake()
        {
            expedition = GetComponent<ExpeditionDirector>();
            expedition.CrateRecovered += OnCrateRecovered;
            expedition.GateOpening += OnGateOpening;
            expedition.DiveEnded += Stop;
            expedition.DiveFailed += Freeze;
        }

        void OnDestroy()
        {
            expedition.CrateRecovered -= OnCrateRecovered;
            expedition.GateOpening -= OnGateOpening;
            expedition.DiveEnded -= Stop;
            expedition.DiveFailed -= Freeze;
        }

        void Update()
        {
            if (stopped || !expedition.Started)
                return;
            if (!ready && !TryInit())
                return;

            bool busy = expedition.MagnetBusy || expedition.Reading;
            if (busy)
                lastBusy = Time.time;
            bool calm = !busy && Time.time - lastBusy >= quietAfterPickup;

            // The gate's double fault ignores the reading (the final log is up), never the magnet
            if (gateWaveAt >= 0f)
            {
                if (Time.time >= gateWaveAt && !expedition.MagnetBusy)
                {
                    gateWaveAt = -1f;
                    BreakWave(Mathf.Max(2, afterGate.faults.x));
                }
                return;
            }

            // The quiet stretch only counts down while everything works
            if (Systems.ActiveFaults > 0)
            {
                nextWaveAt = -1f;
                return;
            }
            if (nextWaveAt < 0f)
            {
                Schedule();
                return;
            }
            if (Time.time >= nextWaveAt && calm)
            {
                var phase = Current;
                nextWaveAt = -1f;
                BreakWave(Mathf.Max(1, UnityEngine.Random.Range(phase.faults.x, phase.faults.y + 1)));
            }
        }

        bool TryInit()
        {
            var world = expedition.World;
            if (world == null)
                return false;
            Systems = world.GetComponentInChildren<CockpitSystems>(true);
            if (Systems == null)
                return false;
            if (firstFault == null)
                firstFault = world.GetComponentInChildren<RadarConsole>(true);
            ready = true;
            return true;
        }

        void Schedule()
        {
            var phase = Current;
            nextWaveAt = phase.interval.y <= 0f ? -1f : Time.time + UnityEngine.Random.Range(phase.interval.x, phase.interval.y);
        }

        void OnCrateRecovered(int recovered)
        {
            // Entering a busier phase: don't keep waiting on a long timer rolled for a calmer one
            var phase = Current;
            if (phase.interval.y > 0f && (nextWaveAt < 0f || nextWaveAt > Time.time + phase.interval.y))
                nextWaveAt = -1f;
        }

        void OnGateOpening()
        {
            gatePhase = true;
            gateWaveAt = Time.time + gateFaultDelay;
            nextWaveAt = -1f;
        }

        void Stop()
        {
            stopped = true;
            gateWaveAt = -1f;
            if (Systems != null)
                Systems.RepairAll();
        }

        /// <summary>Breaks several devices in the same frame.</summary>
        void BreakWave(int count)
        {
            for (int i = 0; i < count; i++)
                Break();
        }

        /// <summary>The dive was lost: no more faults, and the broken ones stay broken (a repair would start the pumps).</summary>
        void Freeze()
        {
            stopped = true;
            gateWaveAt = -1f;
            if (Systems != null)
                Systems.AlarmsMuted = true;
        }

        void Break()
        {
            var phase = Current;
            CockpitDevice device = null;
            if (!firstDone && firstFault != null && !firstFault.IsFaulty)
            {
                firstFault.BeginFault(phase.difficulty);
                device = firstFault;
            }
            else
            {
                device = Systems.BreakRandomDevice(phase.difficulty, lastBroken);
            }
            firstDone = true;
            if (device == null)
                return;
            lastBroken = device;
            if (logFaults)
                Debug.Log($"[Faults] {device.DisplayName} broke (difficulty {device.Difficulty}, crates {expedition.Recovered}, active {Systems.ActiveFaults}{(gatePhase ? ", gate phase" : "")})");
        }

        Phase PhaseFor(int recovered)
        {
            var result = phases.Length > 0 ? phases[0] : afterGate;
            foreach (var p in phases)
                if (recovered >= p.fromCrate)
                    result = p;
            return result;
        }

        // ---------------------------------------------------------------- testing

        [ContextMenu("Break one device now")]
        void DebugBreakOne()
        {
            if (ready)
                Break();
        }

        [ContextMenu("Start a wave now")]
        void DebugWave()
        {
            nextWaveAt = Time.time;
            lastBusy = float.NegativeInfinity;
        }

        [ContextMenu("Repair everything")]
        void DebugRepairAll() => Systems?.RepairAll();
    }
}
