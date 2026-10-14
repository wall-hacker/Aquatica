using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// A cockpit system that can break and be repaired by the player. The (future) defect event system
    /// only needs this API: call <see cref="BeginFault"/> to break the device and listen to
    /// <see cref="Repaired"/> to know when the player has fixed it. Each device decides what "repairing"
    /// means and shows the fault on its own lights/screen.
    /// </summary>
    public abstract class CockpitDevice : MonoBehaviour
    {
        [SerializeField] string displayName = "Device";

        public string DisplayName => displayName;
        public bool IsFaulty { get; private set; }

        /// <summary>Raised when the device breaks.</summary>
        public event Action<CockpitDevice> FaultStarted;

        /// <summary>Raised when the player has finished repairing the device.</summary>
        public event Action<CockpitDevice> Repaired;

        /// <summary>Breaks the device. Ignored if it is already faulty.</summary>
        public void BeginFault()
        {
            if (IsFaulty)
                return;
            IsFaulty = true;
            OnFaultBegan();
            FaultStarted?.Invoke(this);
        }

        /// <summary>Fixes the device instantly without player input (e.g. for debugging or resetting a run).</summary>
        public void ForceRepair() => CompleteRepair();

        /// <summary>Called by subclasses once the player has done what the fault requires.</summary>
        protected void CompleteRepair()
        {
            if (!IsFaulty)
                return;
            IsFaulty = false;
            OnRepaired();
            Repaired?.Invoke(this);
        }

        /// <summary>Set up the fault: pick what the player must do and show it.</summary>
        protected abstract void OnFaultBegan();

        /// <summary>Return the device to its normal look.</summary>
        protected abstract void OnRepaired();
    }
}
