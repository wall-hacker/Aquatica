using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Two covered key switches that only work together, like a two-person launch/arming system.
    /// The covers stay locked shut while the interlock works. Fault: both lights blink red and the covers unlock; the
    /// player opens both covers and has both keys turned at the same moment (holding both, or turning the second
    /// before the first springs back). Harder faults spring the keys back sooner. Once repaired, the covers close by
    /// themselves.
    /// </summary>
    public class KeySwitchPair : CockpitDevice
    {
        [SerializeField] KeySwitch first;
        [SerializeField] KeySwitch second;
        [Tooltip("Seconds a key stays turned after release, per difficulty 1, 2, 3.")]
        [SerializeField] float[] springBack = { 1.2f, 0.8f, 0.45f };
        [Tooltip("Seconds after the repair before the covers close.")]
        [SerializeField] float closeDelay = 0.9f;

        /// <summary>Both keys were turned together (fires whether or not there is a fault).</summary>
        public event Action Activated;

        bool wasBoth;
        float closeAt = -1f;

        void Awake()
        {
            first.KeyChanged += _ => Evaluate();
            second.KeyChanged += _ => Evaluate();
            first.CoverChanged += _ => Refresh();
            second.CoverChanged += _ => Refresh();
            if (!IsFaulty)
                SetLocked(true);
        }

        void Start() => Refresh();

        void Update()
        {
            if (closeAt >= 0f && Time.time >= closeAt)
            {
                closeAt = -1f;
                if (!IsFaulty)
                    SetLocked(true);
            }
        }

        protected override void OnFaultBegan()
        {
            closeAt = -1f;
            float delay = springBack[Mathf.Clamp(Difficulty - 1, 0, springBack.Length - 1)];
            first.SpringBackDelay = delay;
            second.SpringBackDelay = delay;
            SetLocked(false);
            Refresh();
        }

        protected override void OnRepaired()
        {
            closeAt = Time.time + closeDelay;
            Refresh();
        }

        void SetLocked(bool locked)
        {
            first.CoverLocked = locked;
            second.CoverLocked = locked;
        }

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
