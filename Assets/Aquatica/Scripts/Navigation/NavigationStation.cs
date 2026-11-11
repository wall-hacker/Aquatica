using Aquatica.Cockpit;
using UnityEngine;

namespace Aquatica.Navigation
{
    /// <summary>
    /// The pilot's spot at the front of the cockpit. When the player steps into the zone in front of the seats,
    /// the console screen offers navigation mode; tapping the screen engages it (and tapping again leaves it).
    /// In navigation mode the floating <see cref="ThrottleLever"/> appears and, while held, drives the
    /// <see cref="SubmarineMotor"/> towards wherever the player is looking. Navigation mode switches itself off
    /// when the player walks out of the zone or turns away from the dome for a moment.
    /// The upper monitor shows depth and heading, and warns when the currents at the edge of the area push back.
    /// An <see cref="Overlay"/> can take over either screen and the console tap.
    /// While any cockpit device is faulty the helm is locked: navigation mode drops out and can't be entered, the
    /// console tap does nothing, and both screens list the faults and where in the cabin they are (the monitor still
    /// lets an overlay finish what it is showing, e.g. the gateway's final log).
    /// </summary>
    public class NavigationStation : MonoBehaviour
    {
        enum State { Standby, Prompt, Active }

        [SerializeField] SubmarineMotor motor;
        [SerializeField] ThrottleLever lever;
        [SerializeField] CockpitScreen console;
        [SerializeField] CockpitScreen monitor;
        [Tooltip("Faulty devices lock the helm. Found in the parents if left empty.")]
        [SerializeField] CockpitSystems systems;

        [Header("Pilot zone (this transform's space)")]
        [SerializeField] Vector3 zoneCenter = new(0f, 1.9f, 2.7f);
        [SerializeField] Vector3 zoneSize = new(2.6f, 4f, 1.5f);
        [Tooltip("Seconds the player may be outside the zone before navigation mode switches off.")]
        [SerializeField] float leaveGrace = 0.5f;

        [Header("Looking away")]
        [Tooltip("Navigation mode switches off when the player looks further than this (degrees) from the bow...")]
        [SerializeField] float lookAwayAngle = 150f;
        [Tooltip("...for this long (s).")]
        [SerializeField] float lookAwayGrace = 1f;

        [Header("Screens")]
        [SerializeField] float redrawInterval = 0.05f;
        [SerializeField, ColorUsage(false, true)] Color warningColor = new(1.6f, 0.35f, 0.08f);
        [Tooltip("Text colour of the fault warning.")]
        [SerializeField, ColorUsage(false, true)] Color faultColor = new(2.2f, 0.1f, 0.06f);

        Camera viewCamera;
        State state;
        float outsideTime;
        float lookAwayTime;
        float redrawTimer;
        float warningHold;
        Vector3 lastPush = Vector3.forward;

        public bool NavigationActive => state == State.Active;

        /// <summary>A cockpit device is faulty: navigation mode is off and can't be entered.</summary>
        public bool Locked => systems != null && systems.ActiveFaults > 0;

        /// <summary>Gets the first say on what the screens show and what a console tap does.</summary>
        public IStationOverlay Overlay { get; set; }

        void Awake()
        {
            console.Tapped += OnConsoleTapped;
            if (systems == null)
                systems = GetComponentInParent<CockpitSystems>();
            if (systems != null)
            {
                systems.FaultStarted += OnFaultsChanged;
                systems.Repaired += OnFaultsChanged;
            }
        }

        void OnFaultsChanged(CockpitDevice _)
        {
            if (Locked && state == State.Active)
                SetState(viewCamera != null && InZone(viewCamera.transform.position) ? State.Prompt : State.Standby);
            redrawTimer = 0f;
        }

        void OnDestroy()
        {
            if (systems != null)
            {
                systems.FaultStarted -= OnFaultsChanged;
                systems.Repaired -= OnFaultsChanged;
            }
            // The lever lives under the camera while attached; it belongs to this cockpit
            if (lever != null)
                Destroy(lever.gameObject);
        }

        void Update()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                    return;
                lever.Attach(viewCamera);
            }

            var view = viewCamera.transform;
            bool inZone = InZone(view.position);
            outsideTime = inZone ? 0f : outsideTime + Time.deltaTime;

            float offBow = Vector3.Angle(Flat(view.forward), Flat(motor.transform.forward));
            // Anchored (e.g. the magnet is down and the phone points at the floor): looking away doesn't count
            lookAwayTime = offBow > lookAwayAngle && !motor.Anchored ? lookAwayTime + Time.deltaTime : 0f;

            switch (state)
            {
                case State.Standby when inZone:
                    SetState(State.Prompt);
                    break;
                case State.Prompt when !inZone:
                    SetState(State.Standby);
                    break;
                case State.Active when outsideTime > leaveGrace:
                    SetState(State.Standby);
                    break;
                case State.Active when lookAwayTime > lookAwayGrace:
                    SetState(inZone ? State.Prompt : State.Standby);
                    break;
            }

            motor.Heading = view.forward;
            motor.Throttle = state == State.Active && lever.IsHeld && !Locked;

            redrawTimer -= Time.deltaTime;
            if (redrawTimer <= 0f)
            {
                redrawTimer = redrawInterval;
                DrawConsole();
                DrawMonitor();
            }
        }

        void OnConsoleTapped(CockpitScreen _)
        {
            if (Locked)
                return;
            if (Overlay != null && Overlay.ConsoleTapped())
            {
                redrawTimer = 0f;
                return;
            }
            if (state == State.Prompt && !Locked)
                SetState(State.Active);
            else if (state == State.Active)
                SetState(InZone(viewCamera.transform.position) ? State.Prompt : State.Standby);
        }

        void SetState(State next)
        {
            if (state == next)
                return;
            state = next;
            lookAwayTime = 0f;
            lever.SetVisible(state == State.Active);
            if (state != State.Active)
                motor.Throttle = false;
            redrawTimer = 0f;
            Debug.Log($"[Aquatica] Navigation: {state}");
        }

        bool InZone(Vector3 worldPoint)
        {
            var p = transform.InverseTransformPoint(worldPoint) - zoneCenter;
            var h = zoneSize * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        static bool Blink(float rate, float duty = 0.5f) => Mathf.Repeat(Time.time * rate, 1f) < duty;

        void DrawConsole()
        {
            var s = console;
            if (Locked)
            {
                DrawFaults(s);
                return;
            }
            // Back to the normal colour after a fault page (an overlay may set its own)
            s.SetColor(null);
            if (Overlay != null && Overlay.DrawConsole(s))
                return;
            int cx = s.Width / 2;
            s.Begin();
            switch (state)
            {
                case State.Standby:
                    s.Text("NAV SYSTEM", cx, 22, TextAlign.Center, 1, 0.7f);
                    s.Rule(34);
                    s.Text("STANDBY", cx, 42, TextAlign.Center, 1, 0.45f);
                    if (Blink(0.8f))
                        s.Text("_", cx + 24, 42, TextAlign.Left, 1, 0.45f);
                    break;

                case State.Prompt:
                    if (Blink(1.6f, 0.7f))
                        s.Text("HUMAN DETECTED!", cx, 8, TextAlign.Center);
                    s.Rule(20);
                    DrawButton(s, 30, "ENTER", "NAVIGATION MODE", Blink(1.1f, 0.6f) ? 1f : 0.55f);
                    s.Text("TAP SCREEN", cx, 72, TextAlign.Center, 1, 0.45f);
                    break;

                case State.Active:
                    s.Text("NAVIGATION ACTIVE", cx, 5, TextAlign.Center);
                    s.Rule(15);
                    DrawTelemetry(s, 20);
                    DrawButton(s, 52, "EXIT", "NAVIGATION MODE", 0.8f);
                    break;
            }
            s.End(state.ToString());
        }

        void DrawTelemetry(CockpitScreen s, int y)
        {
            float knots = motor.Speed * 1.94384f;
            string thrust = lever.IsHeld ? "AHEAD" : "STOP";
            s.Text($"THR {thrust}", 8, y, TextAlign.Left, 1, 0.85f);
            s.Text($"{knots:0.0} KN", s.Width - 8, y, TextAlign.Right, 1, 0.85f);
            s.Text($"DEPTH {motor.DepthBelowSurface(viewCamera.transform.position):0.0} M", 8, y + 11, TextAlign.Left, 1, 0.85f);
            s.Text($"HDG {Mathf.RoundToInt(motor.Bearing) % 360:000}", 8, y + 22, TextAlign.Left, 1, 0.85f);

            // Engine level bar
            int barX = s.Width - 40, barW = 32;
            s.Rect(barX, y + 22, barW, 7, 0.6f);
            s.FillRect(barX + 2, y + 24, Mathf.RoundToInt((barW - 4) * motor.EngineLevel), 3, 0.9f);
        }

        static void DrawButton(CockpitScreen s, int y, string verb, string label, float intensity)
        {
            int cx = s.Width / 2;
            int width = PixelFont.Measure(label) + 14;
            s.Rect(cx - width / 2, y, width, 30, intensity);
            s.Rect(cx - width / 2 + 2, y + 2, width - 4, 26, intensity * 0.4f);
            s.Text(verb, cx, y + 6, TextAlign.Center, 1, intensity);
            s.Text(label, cx, y + 17, TextAlign.Center, 1, intensity);
        }

        void DrawMonitor()
        {
            var s = monitor;
            int cx = s.Width / 2;
            bool warning = motor.InCurrents;
            warningHold = warning ? 2f : Mathf.Max(0f, warningHold - redrawInterval);
            s.Begin();
            if (Locked)
            {
                if (Overlay == null || !Overlay.DrawMonitor(s))
                    DrawFaults(s);
            }
            else if (warning || warningHold > 0f)
            {
                if (motor.CurrentPush.sqrMagnitude > 0.01f)
                    lastPush = motor.CurrentPush;
                DrawCurrentWarning(s);
            }
            else if (Overlay == null || !Overlay.DrawMonitor(s))
            {
                s.SetColor(null);
                s.Text("AQUATICA", cx, 5, TextAlign.Center, 1, 0.8f);
                s.Rule(16);
                s.Text($"DEPTH {motor.DepthBelowSurface(viewCamera.transform.position):0.0} M", 6, 24, TextAlign.Left, 1, 0.85f);
                s.Text($"HDG   {Mathf.RoundToInt(motor.Bearing) % 360:000}", 6, 35, TextAlign.Left, 1, 0.85f);
                var lines = Overlay?.StatusLines;
                if (lines == null)
                    s.Text(state == State.Active ? "HELM: PILOT" : "HELM: IDLE", 6, 46, TextAlign.Left, 1, 0.6f);
                else
                    for (int i = 0; i < lines.Length; i++)
                        s.Text(lines[i], 6, 46 + i * 11, TextAlign.Left, 1, 0.75f);
                s.End("Status");
            }
        }

        /// <summary>Size of the fault page: the smaller screen (the monitor). The console draws it centred, so both match.</summary>
        static readonly Vector2Int FaultPage = new(112, 68);

        /// <summary>
        /// Fault warning, drawn identically on both screens: blinking header, one line per faulty device with an
        /// arrow towards it (as seen from the pilot's seat), and that the helm is locked until everything is repaired.
        /// </summary>
        void DrawFaults(CockpitScreen s)
        {
            int ox = (s.Width - FaultPage.x) / 2, oy = (s.Height - FaultPage.y) / 2;
            int right = ox + FaultPage.x - 4;
            s.Begin();
            s.SetColor(faultColor);
            if (Blink(2f, 0.6f))
                s.Text("!! SYSTEM FAULT !!", ox + FaultPage.x / 2, oy + 2, TextAlign.Center);
            for (int x = ox + 4; x <= right; x += 2)
                s.FillRect(x, oy + 12, 1, 1, 0.5f);

            const int rowHeight = 10;
            int y = oy + 16;
            int maxChars = (right - 10 - (ox + 4)) / PixelFont.Advance;
            foreach (var device in systems.Devices)
            {
                if (!device.IsFaulty || y + rowHeight > oy + 56)
                    continue;
                string label = device.ScreenLabel;
                string arrow = Arrow(device.Location);
                s.Text(label.Length > maxChars ? label.Substring(0, maxChars) : label, ox + 4, y, TextAlign.Left, 1, 0.95f);
                s.Text(arrow, right, y, TextAlign.Right, 1, 0.95f);
                y += rowHeight;
            }
            if (Blink(0.9f, 0.7f))
                s.Text("HELM LOCKED", ox + FaultPage.x / 2, oy + 59, TextAlign.Center, 1, 0.8f);
            s.End("Faults" + systems.ActiveFaults);
        }

        /// <summary>Arrow towards a point in the cabin, seen from the pilot's seat facing the bow: ^ ahead, V behind, else &lt; or &gt;.</summary>
        string Arrow(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint) - zoneCenter;
            float angle = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
            if (angle < 35f)
                return "^";
            if (angle > 155f)
                return "V";
            return local.x < 0f ? "<" : ">";
        }

        /// <summary>
        /// Where the current comes from, relative to the bow ("FROM THE LEFT", "FROM ABOVE", ...), with a small
        /// diagram of water rushing at the submarine from that side and a bar for its strength.
        /// </summary>
        void DrawCurrentWarning(CockpitScreen s)
        {
            s.SetColor(warningColor);
            if (Blink(1.4f, 0.65f))
                s.Text("! STRONG CURRENT !", s.Width / 2, 3, TextAlign.Center);
            s.Rule(13);

            // Direction the water comes from (opposite to the push), split into vertical and bow-relative parts
            var source = -lastPush;
            var flat = new Vector3(source.x, 0f, source.z);
            bool fromAbove = source.y > 0.3f * source.magnitude;
            bool sideways = flat.sqrMagnitude > 0.09f * source.sqrMagnitude;
            float angle = sideways ? Vector3.SignedAngle(Flat(motor.transform.forward), flat, Vector3.up) : 0f;
            string side = Mathf.Abs(angle) < 45f ? "AHEAD" : Mathf.Abs(angle) > 135f ? "BEHIND" : angle > 0f ? "THE RIGHT" : "THE LEFT";

            const int textX = 50;
            s.Text("FROM", textX, 20, TextAlign.Left, 1, 0.7f);
            if (sideways)
            {
                s.Text(side, textX, 30);
                if (fromAbove)
                    s.Text("+ ABOVE", textX, 40);
            }
            else
            {
                s.Text("ABOVE", textX, 30);
            }
            if (Blink(1f, 0.6f))
                s.Text("TURN BACK", textX, 50, TextAlign.Left, 1, 0.75f);

            // Strength bar
            s.Rect(textX, 60, s.Width - textX - 4, 5, 0.5f);
            s.FillRect(textX + 1, 61, Mathf.RoundToInt((s.Width - textX - 6) * Mathf.Clamp01(motor.CurrentZone)), 3, 0.9f);

            // Diagram: top-down (bow up) for side currents, side view (bow right) for a current from above
            const int cx = 24, cy = 41, radius = 18;
            s.Rect(cx - radius - 3, cy - radius - 3, (radius + 3) * 2 + 1, (radius + 3) * 2 + 1, 0.3f);
            Vector2 from = sideways
                ? new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), -Mathf.Cos(angle * Mathf.Deg2Rad))
                : new Vector2(0f, -1f);
            var across = new Vector2(-from.y, from.x);
            for (int j = -1; j <= 1; j++)
            for (int k = 0; k < 3; k++)
            {
                float f = Mathf.Repeat(Time.time * 1.1f + k / 3f + j * 0.21f, 1f);
                float along = Mathf.Lerp(radius, -radius, f);
                var head = new Vector2(cx, cy) + from * along + across * (j * 8f);
                var tail = head + from * 5f;
                s.Line(Mathf.RoundToInt(tail.x), Mathf.RoundToInt(tail.y), Mathf.RoundToInt(head.x), Mathf.RoundToInt(head.y),
                    0.9f * Mathf.Sin(f * Mathf.PI));
            }
            if (sideways)
            {
                s.FillRect(cx - 3, cy - 6, 7, 14, 0.15f);
                s.Rect(cx - 3, cy - 6, 7, 14, 1f);
                s.Line(cx - 3, cy - 6, cx, cy - 9);
                s.Line(cx + 3, cy - 6, cx, cy - 9);
            }
            else
            {
                s.FillRect(cx - 7, cy - 3, 14, 7, 0.15f);
                s.Rect(cx - 7, cy - 3, 14, 7, 1f);
                s.Line(cx + 6, cy - 3, cx + 9, cy);
                s.Line(cx + 6, cy + 3, cx + 9, cy);
            }
            s.End("Currents");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.15f);
            Gizmos.DrawCube(zoneCenter, zoneSize);
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireCube(zoneCenter, zoneSize);
        }
    }
}
