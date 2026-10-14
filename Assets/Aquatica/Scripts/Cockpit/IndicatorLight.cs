using UnityEngine;

namespace Aquatica.Cockpit
{
    public enum IndicatorState { Off, Green, Red, Amber, White, Warm }

    /// <summary>
    /// An emissive indicator (LED dome or light fixture diffuser). Colour and blinking can be driven from code;
    /// uses a property block so each one is independent. Optional real lights follow the same colour and
    /// blink, so a fixture's glow and the light it casts always match.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class IndicatorLight : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] IndicatorState state = IndicatorState.Green;
        [SerializeField] float intensity = 2.2f;
        [SerializeField] float blinkRate = 2.5f;
        [Tooltip("Lights cast by this indicator (e.g. a ceiling fixture); they take its colour and go dark when it is off.")]
        [SerializeField] Light[] castLights = System.Array.Empty<Light>();
        [SerializeField] float castIntensity = 1f;

        Renderer target;
        MaterialPropertyBlock block;
        Color customColor;
        bool useCustomColor;
        bool blinking;
        bool lit = true;

        public IndicatorState State => state;

        void Awake()
        {
            target = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            Apply(true);
        }

        public void SetState(IndicatorState newState, bool blink = false)
        {
            state = newState;
            useCustomColor = false;
            blinking = blink;
            Apply(true);
        }

        /// <summary>Any colour, for effects the presets don't cover.</summary>
        public void SetColor(Color color, bool blink = false)
        {
            customColor = color;
            useCustomColor = true;
            blinking = blink;
            Apply(true);
        }

        void Update()
        {
            if (!blinking)
                return;
            bool on = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.55f;
            if (on != lit)
                Apply(on);
        }

        void Apply(bool on)
        {
            if (target == null)
                return;
            lit = on;
            var color = useCustomColor ? customColor : Preset(state);
            bool isOff = !on || (!useCustomColor && state == IndicatorState.Off);
            var final = isOff ? DarkGlass(color) : color * intensity;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(ColorId, final);
            target.SetPropertyBlock(block);

            foreach (var l in castLights)
            {
                if (l == null)
                    continue;
                l.color = color;
                l.intensity = isOff ? 0f : castIntensity;
            }
        }

        static Color Preset(IndicatorState s) => s switch
        {
            IndicatorState.Green => new Color(0.15f, 1f, 0.3f),
            IndicatorState.Red => new Color(1f, 0.08f, 0.05f),
            IndicatorState.Amber => new Color(1f, 0.6f, 0.05f),
            IndicatorState.White => new Color(0.9f, 0.95f, 1f),
            IndicatorState.Warm => new Color(1f, 0.86f, 0.68f),
            _ => new Color(0.3f, 0.3f, 0.3f),
        };

        static Color DarkGlass(Color c) => Color.Lerp(new Color(0.03f, 0.03f, 0.03f), c, 0.12f);
    }
}
