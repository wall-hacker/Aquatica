using Aquatica.Expedition;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Bank of toggle switches, each with a light underneath. Fault: some switches are in the wrong position (their
    /// lights blink red); the panel is repaired once every light is green again. The breakers are unstable: now and
    /// then, putting a switch right knocks another one (that was right) out a moment later, so the player has to
    /// catch it quickly too. Which switch gets knocked out is random every time, so there is no pattern to learn,
    /// and each fault has a small budget of these kickbacks, so the panel always settles. Harder faults start with
    /// more wrong switches and kick back more often. Outside a fault the switches work on their own.
    /// </summary>
    public class SwitchPanel : CockpitDevice
    {
        [SerializeField] ToggleSwitch[] switches;
        [Tooltip("How many switches a fault puts in the wrong position (min, max), per difficulty 1, 2, 3.")]
        [SerializeField] Vector2Int[] wrongCounts = { new(2, 3), new(3, 4), new(4, 5) };
        [Tooltip("Most kickbacks one fault can cause, per difficulty 1, 2, 3.")]
        [SerializeField] int[] kickbackBudget = { 1, 2, 3 };
        [Tooltip("Chance that putting a switch right knocks another one out, per difficulty 1, 2, 3.")]
        [SerializeField] float[] kickbackChance = { 0.35f, 0.45f, 0.55f };
        [Tooltip("Seconds between the player's flip and the switch it knocks out.")]
        [SerializeField] float kickbackDelay = 0.35f;
        [SerializeField] float kickbackVolume = 0.4f;

        bool[] required;
        int kicksLeft;
        int pendingKick = -1;
        float kickAt;
        AudioSource audioSource;

        public ToggleSwitch[] Switches => switches;

        void Awake()
        {
            required = new bool[switches.Length];
            for (int i = 0; i < switches.Length; i++)
            {
                int index = i;
                switches[i].Toggled += _ => OnToggled(index);
            }
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.8f;
            audioSource.minDistance = 1.5f;
        }

        void Start()
        {
            // A fault may already have scrambled the panel (broken on its first frame): keep its solution
            if (!IsFaulty)
                MatchCurrent();
            Refresh();
        }

        void Update()
        {
            if (pendingKick < 0 || Time.time < kickAt)
                return;
            int target = pendingKick;
            pendingKick = -1;
            if (!IsFaulty)
                return;
            var s = switches[target];
            s.SetOn(!s.IsOn, false);
            audioSource.PlayOneShot(Tones.Buzz, kickbackVolume);
            Refresh();
        }

        protected override void OnFaultBegan()
        {
            MatchCurrent();
            int d = Mathf.Clamp(Difficulty - 1, 0, 2);
            var range = wrongCounts.Length > 0 ? wrongCounts[Mathf.Min(d, wrongCounts.Length - 1)] : new Vector2Int(2, 4);
            int count = Mathf.Clamp(Random.Range(range.x, range.y + 1), 1, switches.Length);
            var order = Shuffled();
            for (int i = 0; i < count; i++)
                required[order[i]] = !required[order[i]];
            kicksLeft = kickbackBudget.Length > 0 ? kickbackBudget[Mathf.Min(d, kickbackBudget.Length - 1)] : 0;
            pendingKick = -1;
            Refresh();
        }

        protected override void OnRepaired()
        {
            pendingKick = -1;
            Refresh();
        }

        void OnToggled(int index)
        {
            // Putting a switch right may knock another right one out, a moment later
            if (IsFaulty && switches[index].IsOn == required[index] && pendingKick < 0 && kicksLeft > 0)
            {
                int d = Mathf.Clamp(Difficulty - 1, 0, 2);
                float chance = kickbackChance.Length > 0 ? kickbackChance[Mathf.Min(d, kickbackChance.Length - 1)] : 0f;
                int target = RandomCorrectSwitch(index);
                if (target >= 0 && Random.value < chance)
                {
                    kicksLeft--;
                    pendingKick = target;
                    kickAt = Time.time + kickbackDelay;
                }
            }
            Refresh();
        }

        /// <summary>A random switch that is currently right, other than the one just flipped (-1 if none).</summary>
        int RandomCorrectSwitch(int except)
        {
            int found = -1, seen = 0;
            for (int i = 0; i < switches.Length; i++)
            {
                if (i == except || switches[i].IsOn != required[i])
                    continue;
                seen++;
                if (Random.Range(0, seen) == 0)
                    found = i;
            }
            return found;
        }

        int[] Shuffled()
        {
            var order = new int[switches.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            for (int i = 0; i < order.Length; i++)
            {
                int j = Random.Range(i, order.Length);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

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

            // Not done while a kickback is about to land
            if (IsFaulty && allCorrect && pendingKick < 0)
                CompleteRepair();
        }
    }
}
