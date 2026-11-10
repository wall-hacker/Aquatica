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
        [Tooltip("Short name for the cockpit screens' fault list (about 13 characters at most). Empty: the display name.")]
        [SerializeField] string screenLabel = "";

        public string DisplayName => displayName;
        public string ScreenLabel => (string.IsNullOrEmpty(screenLabel) ? displayName : screenLabel).ToUpperInvariant();
        public bool IsFaulty { get; private set; }

        /// <summary>How hard the current (or last) fault is to repair: 1 = easy, 3 = hardest.</summary>
        public int Difficulty { get; private set; } = 1;

        public const int MaxDifficulty = 3;

        /// <summary>
        /// The player is working on this fault (e.g. listening to a sequence), so its klaxon should keep down
        /// (see <see cref="FaultKlaxons"/>).
        /// </summary>
        public virtual bool AlarmDucked => false;

        /// <summary>Raised when the device breaks.</summary>
        public event Action<CockpitDevice> FaultStarted;

        /// <summary>Raised when the player has finished repairing the device.</summary>
        public event Action<CockpitDevice> Repaired;

        /// <summary>
        /// Where the device is in the world: the middle of its visible parts (a device split across the cabin, like
        /// the two interlock key switches, sits between them; its own transform may be anywhere).
        /// </summary>
        public Vector3 Location
        {
            get
            {
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    return transform.position;
                var sum = Vector3.zero;
                foreach (var r in renderers)
                    sum += r.bounds.center;
                return sum / renderers.Length;
            }
        }

        /// <summary>Breaks the device at the given difficulty (1-3). Ignored if it is already faulty.</summary>
        public void BeginFault(int difficulty = 1)
        {
            if (IsFaulty)
                return;
            Difficulty = Mathf.Clamp(difficulty, 1, MaxDifficulty);
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
