using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Radar monitor with a row of status lights above the screen and a push button under each light.
    /// Fault: the screen garbles and the lights turn red; the player re-calibrates it by pressing the
    /// buttons in the order shown (the light above the next button blinks amber). A wrong press restarts the sequence.
    /// </summary>
    public class RadarConsole : CockpitDevice
    {
        static readonly int FaultId = Shader.PropertyToID("_Fault");

        [SerializeField] Renderer screen;
        [SerializeField] IndicatorLight[] statusLights;
        [SerializeField] PushButton[] buttons;
        [SerializeField] int sequenceLength = 3;
        [SerializeField] float screenFadeSpeed = 3f;

        /// <summary>Any button press (index into the button row), faulty or not.</summary>
        public event Action<int> ButtonPressed;

        int[] sequence = Array.Empty<int>();
        int progress;
        float fault;
        Material screenMaterial;

        void Awake()
        {
            screenMaterial = screen.material;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i].Pressed += _ => OnButton(index);
            }
        }

        void Start() => ShowNormal();

        void OnDestroy() => Destroy(screenMaterial);

        void Update()
        {
            fault = Mathf.MoveTowards(fault, IsFaulty ? 1f : 0f, screenFadeSpeed * Time.deltaTime);
            screenMaterial.SetFloat(FaultId, fault);
        }

        protected override void OnFaultBegan()
        {
            sequence = new int[Mathf.Max(1, sequenceLength)];
            for (int i = 0; i < sequence.Length; i++)
            {
                int pick;
                do pick = UnityEngine.Random.Range(0, buttons.Length);
                while (i > 0 && pick == sequence[i - 1] && buttons.Length > 1);
                sequence[i] = pick;
            }
            progress = 0;
            ShowSequence();
        }

        protected override void OnRepaired() => ShowNormal();

        void OnButton(int index)
        {
            ButtonPressed?.Invoke(index);
            if (!IsFaulty)
                return;

            if (index == sequence[progress])
            {
                progress++;
                if (progress >= sequence.Length)
                {
                    CompleteRepair();
                    return;
                }
            }
            else
            {
                progress = 0;
            }
            ShowSequence();
        }

        void ShowSequence()
        {
            for (int i = 0; i < statusLights.Length; i++)
                statusLights[i].SetState(IndicatorState.Red);
            statusLights[sequence[progress]].SetState(IndicatorState.Amber, true);
        }

        void ShowNormal()
        {
            foreach (var l in statusLights)
                l.SetState(IndicatorState.Green);
        }
    }
}
