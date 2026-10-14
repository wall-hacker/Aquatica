using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Two covered key switches that only work together, like a two-person launch/arming system.
    /// Fault: both lights blink red; the player opens both covers and has both keys turned at the
    /// same moment (holding both, or turning the second before the first springs back).
    /// </summary>
    public class KeySwitchPair : CockpitDevice
    {
        [SerializeField] KeySwitch first;
        [SerializeField] KeySwitch second;

        /// <summary>Both keys were turned together (fires whether or not there is a fault).</summary>
        public event Action Activated;

        bool wasBoth;

        void Awake()
        {
            first.KeyChanged += _ => Evaluate();
            second.KeyChanged += _ => Evaluate();
            first.CoverChanged += _ => Refresh();
            second.CoverChanged += _ => Refresh();
        }

        void Start() => Refresh();

        protected override void OnFaultBegan() => Refresh();

        protected override void OnRepaired() => Refresh();

        void Evaluate()
        {
            bool both = first.IsKeyTurned && second.IsKeyTurned;
            if (both && !wasBoth)
            {
                Activated?.Invoke();
                if (IsFaulty)
                    CompleteRepair();
            }
            wasBoth = both;
            Refresh();
        }

        void Refresh()
        {
            Show(first);
            Show(second);
        }

        void Show(KeySwitch k)
        {
            if (!IsFaulty)
                k.Indicator.SetState(IndicatorState.Green);
            else if (k.IsKeyTurned)
                k.Indicator.SetState(IndicatorState.Amber);
            else
                k.Indicator.SetState(IndicatorState.Red, true);
        }
    }
}
