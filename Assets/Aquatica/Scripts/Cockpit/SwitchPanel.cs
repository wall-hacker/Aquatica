using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Bank of toggle switches, each with a light underneath. Fault: some switches are in the wrong
    /// position (their lights turn red); the panel is repaired once every light is green again.
    /// </summary>
    public class SwitchPanel : CockpitDevice
    {
        [SerializeField] ToggleSwitch[] switches;
        [Tooltip("How many switches a fault puts in the wrong position.")]
        [SerializeField] Vector2Int wrongSwitches = new(2, 4);

        bool[] required;

        public ToggleSwitch[] Switches => switches;

        void Awake()
        {
            required = new bool[switches.Length];
            foreach (var s in switches)
                s.Toggled += _ => Refresh();
        }

        void Start()
        {
            MatchCurrent();
            Refresh();
        }

        protected override void OnFaultBegan()
        {
            MatchCurrent();
            int count = Mathf.Clamp(Random.Range(wrongSwitches.x, wrongSwitches.y + 1), 1, switches.Length);
            var order = new int[switches.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            for (int i = 0; i < count; i++)
            {
                int j = Random.Range(i, order.Length);
                (order[i], order[j]) = (order[j], order[i]);
                required[order[i]] = !required[order[i]];
            }
            Refresh();
        }

        protected override void OnRepaired() => Refresh();

        void MatchCurrent()
        {
            for (int i = 0; i < switches.Length; i++)
                required[i] = switches[i].IsOn;
        }

        void Refresh()
        {
            if (!IsFaulty)
                MatchCurrent();

            bool allCorrect = true;
            for (int i = 0; i < switches.Length; i++)
            {
                bool correct = switches[i].IsOn == required[i];
                allCorrect &= correct;
                switches[i].Indicator.SetState(correct ? IndicatorState.Green : IndicatorState.Red, !correct);
            }

            if (IsFaulty && allCorrect)
                CompleteRepair();
        }
    }
}
