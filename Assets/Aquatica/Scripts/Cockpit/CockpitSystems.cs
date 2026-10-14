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

        /// <summary>Breaks a random device that is currently working. Returns it, or null if everything is already broken.</summary>
        public CockpitDevice BreakRandomDevice()
        {
            var working = devices.FindAll(d => !d.IsFaulty);
            if (working.Count == 0)
                return null;
            var device = working[UnityEngine.Random.Range(0, working.Count)];
            device.BeginFault();
            return device;
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
