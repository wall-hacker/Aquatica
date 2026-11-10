using System;
using Aquatica.Expedition;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Radar monitor with a row of status lights above the screen and a push button under each light.
    /// Fault: the screen garbles, the lights turn red and a "PLAY ERR SEQ" button appears over the screen. Tapping it
    /// plays the error sequence on the lights, one note per light, with the buttons locked. The player then presses
    /// the buttons in the same order from memory. A wrong press (or hesitating too long) buzzes, and the sequence
    /// plays again from the start. Harder faults have longer, faster sequences.
    /// Contacts (<see cref="SetContacts"/>) are drawn as blips that light up when the sweep passes them; a faulty
    /// radar shows none.
    /// </summary>
    public class RadarConsole : CockpitDevice
    {
        static readonly int FaultId = Shader.PropertyToID("_Fault");
        static readonly int ContactsId = Shader.PropertyToID("_Contacts");

        /// <summary>Most contacts the screen draws.</summary>
        public const int MaxContacts = 8;

        enum Phase { Idle, AwaitStart, Playing, Input, Wrong }

        [SerializeField] Renderer screen;
        [SerializeField] IndicatorLight[] statusLights;
        [SerializeField] PushButton[] buttons;
        [Tooltip("Tap target drawn over the garbled screen during a fault.")]
        [SerializeField] CockpitScreen sequenceButton;
        [Tooltip("Sequence length (min, max) per difficulty 1, 2, 3.")]
        [SerializeField] Vector2Int[] sequenceLengths = { new(5, 5), new(6, 7), new(8, 9) };
        [Tooltip("Seconds each light stays on during playback, per difficulty 1, 2, 3.")]
        [SerializeField] float[] noteTime = { 0.5f, 0.42f, 0.34f };
        [SerializeField] float noteGap = 0.18f;
        [Tooltip("A press that takes longer than this (s) counts as a mistake.")]
        [SerializeField] float inputTimeout = 6f;
        [SerializeField] float screenFadeSpeed = 3f;
        [SerializeField] float volume = 0.55f;

        /// <summary>Any button press (index into the button row), faulty or not.</summary>
        public event Action<int> ButtonPressed;

        int[] sequence = Array.Empty<int>();
        int progress;
        Phase phase;
        float phaseTime;
        int playIndex = -1;
        float flashUntil;
        int flashLight = -1;
        float fault;
        Material screenMaterial;
        AudioSource audioSource;
        readonly Vector4[] contacts = new Vector4[MaxContacts];

        void Awake()
        {
            screenMaterial = screen.material;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i].Pressed += _ => OnButton(index);
            }
            if (sequenceButton != null)
            {
                sequenceButton.Tapped += _ => OnSequenceButton();
                sequenceButton.gameObject.SetActive(false);
            }
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.8f;
            audioSource.minDistance = 1.5f;
        }

        void Start() => ShowNormal();

        void OnDestroy() => Destroy(screenMaterial);

        void Update()
        {
            fault = Mathf.MoveTowards(fault, IsFaulty ? 1f : 0f, screenFadeSpeed * Time.deltaTime);
            screenMaterial.SetFloat(FaultId, fault);
            screenMaterial.SetVectorArray(ContactsId, contacts);

            if (!IsFaulty)
                return;
            phaseTime += Time.deltaTime;
            switch (phase)
            {
                case Phase.Playing:
                    UpdatePlayback();
                    break;
                case Phase.Input when phaseTime > inputTimeout:
                    Mistake();
                    break;
                case Phase.Wrong when phaseTime > 1.1f:
                    BeginPlayback();
                    break;
            }
            ShowLights();
            DrawSequenceButton();
        }

        /// <summary>Once the sequence has been asked for, the klaxon keeps down so the notes can be heard.</summary>
        public override bool AlarmDucked => IsFaulty && phase != Phase.AwaitStart;

        /// <summary>
        /// Blips to draw: xy = position on the screen (-1..1, heading up, starboard right; the rings are at 0.3,
        /// 0.6 and 0.9), z = kind (0 = none, 1 = contact, 2 = beacon), w = brightness. Extra entries are ignored.
        /// </summary>
        public void SetContacts(Vector4[] values, int count)
        {
            for (int i = 0; i < MaxContacts; i++)
                contacts[i] = i < count && !IsFaulty ? values[i] : Vector4.zero;
        }

        protected override void OnFaultBegan()
        {
            var range = sequenceLengths.Length > 0 ? sequenceLengths[Mathf.Clamp(Difficulty - 1, 0, sequenceLengths.Length - 1)] : new Vector2Int(5, 5);
            int length = Mathf.Max(1, UnityEngine.Random.Range(range.x, range.y + 1));
            sequence = new int[length];
            for (int i = 0; i < sequence.Length; i++)
            {
                int pick;
                do pick = UnityEngine.Random.Range(0, buttons.Length);
                while (i > 0 && pick == sequence[i - 1] && buttons.Length > 1);
                sequence[i] = pick;
            }
            progress = 0;
            SetPhase(Phase.AwaitStart);
        }

        protected override void OnRepaired()
        {
            SetPhase(Phase.Idle);
            ShowNormal();
            Play(Tones.Note(3));
        }

        void SetPhase(Phase next)
        {
            phase = next;
            phaseTime = 0f;
            if (sequenceButton != null)
                sequenceButton.gameObject.SetActive(next == Phase.AwaitStart);
        }

        void OnSequenceButton()
        {
            if (IsFaulty && phase == Phase.AwaitStart)
                BeginPlayback();
        }

        void BeginPlayback()
        {
            progress = 0;
            playIndex = -1;
            SetPhase(Phase.Playing);
        }

        float NoteTime => noteTime.Length > 0 ? noteTime[Mathf.Clamp(Difficulty - 1, 0, noteTime.Length - 1)] : 0.45f;

        void UpdatePlayback()
        {
            // A short pause, then each note: light on for NoteTime, off for noteGap
            const float lead = 0.6f;
            float step = NoteTime + noteGap;
            int index = phaseTime < lead ? -1 : Mathf.FloorToInt((phaseTime - lead) / step);
            if (index >= sequence.Length)
            {
                SetPhase(Phase.Input);
                return;
            }
            if (index != playIndex && index >= 0)
            {
                playIndex = index;
                Play(Tones.Note(sequence[index]));
            }
            bool lit = index >= 0 && (phaseTime - lead) - index * step < NoteTime;
            flashLight = lit ? sequence[index] : -1;
            flashUntil = lit ? float.MaxValue : 0f;
        }

        void OnButton(int index)
        {
            ButtonPressed?.Invoke(index);
            if (!IsFaulty || phase != Phase.Input)
                return;

            if (index != sequence[progress])
            {
                Mistake();
                return;
            }
            Play(Tones.Note(index));
            flashLight = index;
            flashUntil = Time.time + 0.3f;
            progress++;
            phaseTime = 0f;
            if (progress >= sequence.Length)
                CompleteRepair();
        }

        void Mistake()
        {
            Play(Tones.Buzz);
            progress = 0;
            flashLight = -1;
            SetPhase(Phase.Wrong);
        }

        void ShowLights()
        {
            bool flashing = flashLight >= 0 && Time.time < flashUntil;
            for (int i = 0; i < statusLights.Length; i++)
            {
                var light = statusLights[i];
                if (flashing && i == flashLight)
                    SetLight(light, phase == Phase.Playing ? IndicatorState.Amber : IndicatorState.Green, false);
                else if (phase == Phase.Playing || (phase == Phase.Input && flashing))
                    SetLight(light, IndicatorState.Off, false);
                else if (phase == Phase.Wrong)
                    SetLight(light, IndicatorState.Red, true);
                else
                    SetLight(light, IndicatorState.Red, phase == Phase.AwaitStart);
            }
        }

        static void SetLight(IndicatorLight light, IndicatorState state, bool blink)
        {
            if (light.State != state || light.Blinking != blink)
                light.SetState(state, blink);
        }

        void DrawSequenceButton()
        {
            if (sequenceButton == null || !sequenceButton.gameObject.activeInHierarchy)
                return;
            var s = sequenceButton;
            float pulse = Mathf.Repeat(Time.time * 1.2f, 1f) < 0.6f ? 1f : 0.55f;
            s.Begin();
            s.Rect(0, 0, s.Width, s.Height, pulse);
            s.Rect(2, 2, s.Width - 4, s.Height - 4, pulse * 0.35f);
            s.Text("PLAY ERR SEQ", s.Width / 2, s.Height / 2 - 3, TextAlign.Center, 1, pulse);
            s.End("Sequence");
        }

        void ShowNormal()
        {
            foreach (var l in statusLights)
                l.SetState(IndicatorState.Green);
        }

        void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, volume);
        }
    }
}
