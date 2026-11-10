using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Registry of every <see cref="CockpitDevice"/> in the cockpit: the hook for the future defect event
    /// system, which can pick a device to break and react to faults/repairs without knowing device types.
    /// </summary>
    public class CockpitSystems : MonoBehaviour
    {
        readonly List<CockpitDevice> devices = new();

        public IReadOnlyList<CockpitDevice> Devices => devices;
        public int ActiveFaults { get; private set; }

        /// <summary>Silences the fault klaxons (e.g. once the dive is lost) without repairing anything.</summary>
        public bool AlarmsMuted { get; set; }

        public event Action<CockpitDevice> FaultStarted;
        public event Action<CockpitDevice> Repaired;

        void Awake()
        {
            GetComponentsInChildren(true, devices);
            foreach (var device in devices)
            {
                device.FaultStarted += OnFaultStarted;
                device.Repaired += OnRepaired;
            }
        }

        /// <summary>
        /// Breaks a random device that is currently working, avoiding <paramref name="avoid"/> when another one is available.
        /// Returns it, or null if everything is already broken.
        /// </summary>
        public CockpitDevice BreakRandomDevice(int difficulty = 1, CockpitDevice avoid = null)
        {
            var working = devices.FindAll(d => !d.IsFaulty && d.isActiveAndEnabled);
            if (working.Count > 1 && avoid != null)
                working.Remove(avoid);
            if (working.Count == 0)
                return null;
            var device = working[UnityEngine.Random.Range(0, working.Count)];
            device.BeginFault(difficulty);
            return device;
        }

        /// <summary>Repairs every faulty device at once (debugging, resetting a run).</summary>
        public void RepairAll()
        {
            foreach (var device in devices)
                device.ForceRepair();
        }

        void OnFaultStarted(CockpitDevice device)
        {
            ActiveFaults++;
            FaultStarted?.Invoke(device);
        }

        void OnRepaired(CockpitDevice device)
        {
            ActiveFaults--;
            Repaired?.Invoke(device);
        }
    }
}
