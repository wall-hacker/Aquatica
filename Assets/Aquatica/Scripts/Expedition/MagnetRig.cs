using System;
using System.Collections;
using Aquatica.Cockpit;
using Aquatica.Navigation;
using Aquatica.Underwater;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The submarine's salvage magnet. Engaged over a crate, it drops on its cable and the player watches through
    /// the magnet camera, shown full screen as if looking at a cockpit CRT. The player holds the phone flat, camera
    /// to the floor, like the magnet itself: the magnet swings in the current, and tipping the phone pushes it
    /// towards the side that dips (top edge = the bow). The ring around the crate is a fixed size on the picture, so
    /// as the camera nears the crate the same aim error looks (and counts) bigger; on top of that the ring tightens
    /// while the magnet stays inside it and eases out again when it drifts off. The last stretch the magnet snaps
    /// onto the lid by itself. It only lowers while it hangs over the crate (and winds back up a little while it
    /// doesn't), so the player has to steady it all the way down. There is no failing: after a while the swing
    /// eases off, and later the magnet starts finding the crate by itself. Touching down locks on; the crate is torn
    /// out of the sand and winched up into the hull.
    /// </summary>
    public class MagnetRig : MonoBehaviour
    {
        enum Phase { Idle, Lowering, Locked, Recovering }

        [SerializeField] SubmarineMotor motor;
        [SerializeField] Shader crtShader;
        [SerializeField] Material cableMaterial;
        [SerializeField] Material siltMaterial;

        [Header("Reach")]
        [Tooltip("Furthest (m, horizontally) the crate may be from the point under the magnet.")]
        [SerializeField] float engageRadius = 5f;
        [Tooltip("Cable length range (m) from the hull to the crate's lid: the submarine has to hover this high.")]
        [SerializeField] Vector2 dropRange = new(6f, 16f);
        [Tooltip("Fastest the submarine may be moving (m/s) to engage.")]
        [SerializeField] float engageSpeed = 0.8f;

        [Header("Swing")]
        [Tooltip("Pull back towards hanging straight down (1/s²).")]
        [SerializeField] float restoring = 0.9f;
        [SerializeField] float damping = 0.7f;
        [Tooltip("Strength of the current's push (m/s²).")]
        [SerializeField] float currentPush = 1.6f;
        [SerializeField] float currentFrequency = 0.3f;
        [Tooltip("Push (m/s²) at full tilt.")]
        [SerializeField] float tiltPush = 7f;
        [SerializeField] bool invertTilt;
        [Tooltip("Furthest the magnet swings out (m).")]
        [SerializeField] float maxSwing = 6f;

        [Header("Lowering")]
        [Tooltip("Ring radius as a fraction of the picture's height: at first, and smallest.")]
        [SerializeField] Vector2 ringSize = new(0.16f, 0.055f);
        [Tooltip("How fast the ring tightens while the magnet is inside it, and loosens while it's off (fraction/s).")]
        [SerializeField] Vector2 ringRates = new(0.012f, 0.04f);
        [Tooltip("Never stricter than this (m), however close the camera gets.")]
        [SerializeField] float minTolerance = 0.22f;
        [Tooltip("Within this distance (m) of the lid the magnet snaps on by itself.")]
        [SerializeField] float snapDistance = 1.5f;
        [SerializeField] float lowerSpeed = 0.9f;
        [Tooltip("Seconds the whole drop takes at most when aimed perfectly (long drops lower a little faster).")]
        [SerializeField] float maxDropTime = 22f;
        [SerializeField] float windBackSpeed = 0.5f;
        [SerializeField] float recoverSpeed = 3f;

        [Header("Help")]
        [Tooltip("After this long (s) the current eases off...")]
        [SerializeField] float easeAfter = 25f;
        [Tooltip("...and after this long the magnet steers itself onto the crate.")]
        [SerializeField] float autoAfter = 60f;

        [Header("View")]
        [SerializeField] float cameraFov = 62f;
        [Tooltip("Feed resolution relative to the screen.")]
        [Range(0.25f, 1f)] [SerializeField] float feedScale = 0.5f;

        public bool Busy => phase != Phase.Idle;

        /// <summary>Shortest cable (m) the magnet works with: the submarine hovers at least this high over a lid.</summary>
        public float MinDrop => dropRange.x;

        /// <summary>Height (m) of the point the magnet hangs from, relative to the submarine's origin.</summary>
        public bool TryGetHangHeight(out float height)
        {
            height = 0f;
            if (!TryFindMagnet())
                return false;
            height = HangPoint.y - motor.transform.position.y;
            return true;
        }

        /// <summary>The crate is up and stowed.</summary>
        public event Action<Crate> Captured;

        Phase phase;
        Crate target;
        Renderer originalMagnet;
        Transform magnet;
        Vector3 localDiscBottom, localPoleTop;
        Quaternion baseRotation; // the magnet's rotation relative to the submarine's heading
        Vector3 hangLocal;      // under the hull, in the submarine's space
        float rodLength;        // the magnet's rod reaches this far up into the hull: the swing pivots at its top
        float drop, dropTarget, elapsed;
        float ringFraction, tolerance;
        Vector2 swing, swingVelocity; // sub-space horizontal offset (x = starboard, y = bow)
        Vector2 noiseSeed;
        LineRenderer cable;
        Camera feedCamera;
        RenderTexture feed;
        Light lamp;
        CrtOverlay overlay;
        readonly TiltInput tilt = new();
        int savedCullingMask;
        Camera viewCamera;
        CockpitInteractor interactor;
        float lockedTime;
        string status = "";

        bool TryFindMagnet()
        {
            if (originalMagnet != null)
                return true;
            if (motor == null)
                motor = GetComponentInParent<UnderwaterWorld>()?.GetComponentInChildren<SubmarineMotor>();
            if (motor == null)
                return false;
            foreach (var r in motor.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.Contains("Magnet"))
                    originalMagnet = r;
            if (originalMagnet == null)
                return false;
            var b = originalMagnet.bounds;
            var sub = motor.transform;
            hangLocal = sub.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            rodLength = b.size.y;
            return true;
        }

        /// <summary>World point the magnet hangs from (the underside of the hull, where the disc sits).</summary>
        Vector3 HangPoint => motor.transform.TransformPoint(hangLocal);

        Quaternion SubYaw => Quaternion.Euler(0f, motor.transform.eulerAngles.y, 0f);

        /// <summary>Can the magnet reach this crate from where the submarine is now? hint says what's missing.</summary>
        public bool CanReach(Crate crate, out string hint)
        {
            hint = "";
            if (crate == null || crate.Captured || !TryFindMagnet())
                return false;
            var hang = HangPoint;
            var top = crate.LidCentre;
            float horizontal = new Vector2(top.x - hang.x, top.z - hang.z).magnitude;
            float height = hang.y - top.y;
            if (horizontal > engageRadius)
                hint = $"CLOSER {horizontal - engageRadius:0} M";
            else if (height > dropRange.y)
                hint = $"DESCEND {height - dropRange.y:0} M";
            else if (height < dropRange.x)
                hint = $"CLIMB {dropRange.x - height:0} M";
            else if (motor.Speed > engageSpeed)
                hint = "SLOW DOWN";
            return hint.Length == 0;
        }

        public void Engage(Crate crate)
        {
            if (Busy || !CanReach(crate, out _))
                return;
            target = crate;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            motor.Anchored = true;
            BuildRig();
            OpenView();
            phase = Phase.Lowering;
            drop = 0f;
            ringFraction = ringSize.x;
            elapsed = 0f;
            swing = Vector2.zero;
            swingVelocity = UnityEngine.Random.insideUnitCircle * 0.6f;
            noiseSeed = new Vector2(UnityEngine.Random.value * 100f, UnityEngine.Random.value * 100f);
            tilt.Enable();

            // Lowering: the minigame
            while (phase == Phase.Lowering)
            {
                Simulate(Time.deltaTime);
                yield return null;
            }

            // Locked on: the magnet snaps down onto the lid, then tears the crate out of the sand
            lockedTime = 0f;
            bool silt = false;
            while (lockedTime < 1.2f)
            {
                float dt = Time.deltaTime;
                lockedTime += dt;
                drop = Mathf.MoveTowards(drop, dropTarget, snapDistance / 0.3f * dt);
                swing = Vector2.Lerp(swing, Goal(), 1f - Mathf.Exp(-12f * dt));
                if (!silt && drop >= dropTarget)
                {
                    silt = true;
                    Silt(target.transform.position);
                }
                PoseRig(dt);
                yield return null;
            }

            phase = Phase.Recovering;
            target.MarkCaptured();
            target.transform.SetParent(magnet, true);
            while (drop > 0f)
            {
                drop = Mathf.Max(0f, drop - recoverSpeed * Time.deltaTime);
                swing = Vector2.Lerp(swing, Vector2.zero, 1f - Mathf.Exp(-2f * Time.deltaTime));
                PoseRig(Time.deltaTime);
                yield return null;
            }

            // Signal cut: static, then back to the cockpit
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                overlay.SetStatic(t / 0.5f);
                DrawHud();
                yield return null;
            }
            target.gameObject.SetActive(false);
            CloseView();
            DestroyRig();
            motor.Anchored = false;
            phase = Phase.Idle;
            var captured = target;
            target = null;
            Captured?.Invoke(captured);
        }

        // Sensors take a moment to start reporting: switch them on before they're needed
        void Start() => tilt.Enable();

        void Simulate(float dt)
        {
            if (dt <= 0f)
                return;
            elapsed += dt;
            var goal = Goal();

            // Current: smooth noise, easing off after a while
            float ease = Mathf.Lerp(1f, 0.3f, Mathf.InverseLerp(easeAfter, easeAfter + 10f, elapsed));
            float time = Time.time * currentFrequency;
            var push = new Vector2(Mathf.PerlinNoise(noiseSeed.x, time) - 0.5f, Mathf.PerlinNoise(noiseSeed.y, time + 31f) - 0.5f) * (2f * currentPush * ease);
            var input = tilt.Read() * (invertTilt ? -1f : 1f);
            float auto = Mathf.InverseLerp(autoAfter, autoAfter + 8f, elapsed);

            var accel = -restoring * swing - damping * swingVelocity + push + input * tiltPush;
            accel += (goal - swing) * (2.5f * auto) - swingVelocity * (1.5f * auto);
            swingVelocity += accel * dt;
            swing += swingVelocity * dt;
            if (swing.magnitude > maxSwing)
            {
                swing = swing.normalized * maxSwing;
                swingVelocity *= 0.5f;
            }

            // The ring is a share of what the camera sees at the crate's depth
            tolerance = Mathf.Max(minTolerance, ringFraction * ViewHeightAtLid());
            bool over = Vector2.Distance(swing, goal) < tolerance;
            ringFraction = over
                ? Mathf.Max(ringSize.y, ringFraction - ringRates.x * dt)
                : Mathf.Min(ringSize.x, ringFraction + ringRates.y * dt);
            float speed = Mathf.Max(lowerSpeed, dropTarget / maxDropTime);
            float snapAt = Mathf.Max(0f, dropTarget - snapDistance);
            drop = over ? Mathf.Min(snapAt, drop + speed * dt) : Mathf.Max(0f, drop - windBackSpeed * dt);
            status = over ? (drop >= snapAt ? "MAGNET LOCKED" : "LOWERING") : "ALIGN OVER TARGET";
            if (over && drop >= snapAt)
                phase = Phase.Locked;

            PoseRig(dt);
        }

        /// <summary>Where the magnet has to be (sub-space horizontal offset from the hang point); also updates dropTarget.</summary>
        Vector2 Goal()
        {
            var toCrate = Quaternion.Inverse(SubYaw) * (target.LidCentre - HangPoint);
            dropTarget = Mathf.Max(0.5f, -toCrate.y - 0.05f);
            return new Vector2(toCrate.x, toCrate.z);
        }

        /// <summary>Height (m) of the picture at the lid's distance from the camera.</summary>
        float ViewHeightAtLid()
        {
            float distance = Mathf.Max(0.9f, dropTarget - drop + 0.35f);
            return 2f * distance * Mathf.Tan(cameraFov * 0.5f * Mathf.Deg2Rad);
        }

        void PoseRig(float dt)
        {
            var hang = HangPoint;
            var yaw = SubYaw;
            var offset = yaw * new Vector3(swing.x, 0f, swing.y);
            var discBottom = hang + offset + Vector3.down * drop;

            // Hang like a pendulum from the top of the rod (inside the hull)
            var pivot = hang + motor.transform.up * rodLength;
            var up = (pivot - discBottom).normalized;
            magnet.rotation = Quaternion.FromToRotation(Vector3.up, up) * yaw * baseRotation;
            magnet.position += discBottom - magnet.TransformPoint(localDiscBottom);

            if (phase == Phase.Locked)
                magnet.position += UnityEngine.Random.insideUnitSphere * 0.04f;

            // Cable from the hull to the top of the magnet's rod once the rod has left the hull
            var rodTop = magnet.TransformPoint(localPoleTop);
            cable.enabled = rodTop.y < hang.y;
            cable.SetPosition(0, hang);
            cable.SetPosition(1, rodTop);

            // Under the magnet on the way down (never inside the crate); from under the hull while it winches up
            Vector3 eye;
            if (phase == Phase.Recovering)
            {
                eye = hang + Vector3.down * 0.6f;
            }
            else
            {
                eye = discBottom + Vector3.down * 0.35f;
                eye.y = Mathf.Max(eye.y, target.LidCentre.y + 0.9f);
            }
            feedCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.down, yaw * Vector3.forward));
            DrawHud();
        }

        void BuildRig()
        {
            magnet = Instantiate(originalMagnet.gameObject, originalMagnet.transform.parent).transform;
            magnet.name = "MagnetRig";
            magnet.SetParent(motor.transform.parent, true);
            baseRotation = Quaternion.Inverse(SubYaw) * magnet.rotation;
            var b = originalMagnet.bounds;
            localDiscBottom = magnet.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            localPoleTop = magnet.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            originalMagnet.enabled = false;

            cable = magnet.gameObject.AddComponent<LineRenderer>();
            cable.sharedMaterial = cableMaterial;
            cable.widthMultiplier = 0.08f;
            cable.positionCount = 2;
            cable.useWorldSpace = true;
            cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            lamp = new GameObject("MagnetLamp").AddComponent<Light>();
            lamp.transform.SetParent(magnet, false);
            lamp.transform.position = magnet.TransformPoint(localDiscBottom);
            lamp.transform.rotation = Quaternion.LookRotation(Vector3.down);
            lamp.type = LightType.Spot;
            lamp.spotAngle = 80f;
            lamp.range = 30f;
            lamp.intensity = 4f;
            lamp.color = new Color(0.85f, 0.95f, 1f);
        }

        void DestroyRig()
        {
            if (magnet != null)
                Destroy(magnet.gameObject);
            if (originalMagnet != null)
                originalMagnet.enabled = true;
        }

        void OpenView()
        {
            viewCamera = Camera.main;
            int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * feedScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * feedScale));
            feed = new RenderTexture(w, h, 24) { name = "MagnetFeed" };
            feedCamera = new GameObject("MagnetCamera").AddComponent<Camera>();
            feedCamera.transform.SetParent(transform, false);
            feedCamera.targetTexture = feed;
            feedCamera.fieldOfView = cameraFov;
            feedCamera.nearClipPlane = 0.05f;
            feedCamera.farClipPlane = 80f;
            feedCamera.clearFlags = CameraClearFlags.SolidColor;
            feedCamera.backgroundColor = RenderSettings.fogColor;

            overlay = CrtOverlay.Create(crtShader);
            overlay.SetFeed(feed);
            overlay.SetStatic(1f);
            StartCoroutine(FadeStatic(1f, 0f, 0.6f));

            // The cockpit is hidden behind the overlay: stop drawing it and stop taps reaching it
            if (viewCamera != null)
            {
                savedCullingMask = viewCamera.cullingMask;
                viewCamera.cullingMask = 0;
            }
            interactor = FindAnyObjectByType<CockpitInteractor>();
            if (interactor != null)
                interactor.enabled = false;
        }

        IEnumerator FadeStatic(float from, float to, float duration)
        {
            for (float t = 0f; t < duration && overlay != null; t += Time.deltaTime)
            {
                overlay.SetStatic(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            if (overlay != null)
                overlay.SetStatic(to);
        }

        void CloseView()
        {
            if (viewCamera != null)
                viewCamera.cullingMask = savedCullingMask;
            if (interactor != null)
                interactor.enabled = true;
            if (overlay != null)
                Destroy(overlay.gameObject);
            if (feedCamera != null)
                Destroy(feedCamera.gameObject);
            if (feed != null)
                Destroy(feed);
        }

        void DrawHud()
        {
            if (overlay == null)
                return;
            var s = overlay.Hud;
            s.Clear();
            int w = s.Width, h = s.Height;
            bool blink = Mathf.Repeat(Time.time, 1f) < 0.6f;

            s.Text("MAGNET CAM 01", 4, 4, TextAlign.Left, 1, 0.8f);
            if (blink)
                s.FillRect(w - 10, 5, 5, 5, 0.9f);
            s.Text("REC", w - 13, 4, TextAlign.Right, 1, 0.7f);
            s.Text($"CABLE {drop:00.0} M", 4, 14, TextAlign.Left, 1, 0.6f);

            // Magnet: crosshair in the middle of the picture
            int cx = w / 2, cy = h / 2;
            s.Line(cx - 6, cy, cx - 2, cy, 0.8f);
            s.Line(cx + 2, cy, cx + 6, cy, 0.8f);
            s.Line(cx, cy - 6, cx, cy - 2, 0.8f);
            s.Line(cx, cy + 2, cx, cy + 6, 0.8f);

            // Target: ring around the crate where the camera sees it
            if (target != null && feedCamera != null && phase != Phase.Recovering)
            {
                var top = target.LidCentre;
                var vp = feedCamera.WorldToViewportPoint(top);
                if (vp.z > 0f)
                {
                    float viewHeight = 2f * vp.z * Mathf.Tan(feedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    int r = Mathf.Clamp(Mathf.RoundToInt(tolerance / viewHeight * h), 3, h / 2);
                    int tx = Mathf.RoundToInt(vp.x * w), ty = Mathf.RoundToInt((1f - vp.y) * h);
                    bool over = status != "ALIGN OVER TARGET";
                    s.Circle(tx, ty, r, over ? 1f : 0.6f, over ? 0 : 3);
                    s.Circle(tx, ty, r + 2, over && blink ? 0.6f : 0.2f);
                }
            }

            // Depth gauge on the right
            int gx = w - 8, gy = 28, gh = h - 60;
            s.Rect(gx, gy, 4, gh, 0.5f);
            float fill = dropTarget > 0f ? Mathf.Clamp01(drop / dropTarget) : 0f;
            int fh = Mathf.RoundToInt((gh - 2) * fill);
            s.FillRect(gx + 1, gy + 1, 2, fh, 0.9f);
            s.Text($"{Mathf.Max(0f, dropTarget - drop):0.0}", gx - 2, gy + gh + 3, TextAlign.Right, 1, 0.6f);

            string line = phase == Phase.Recovering ? "RECOVERING CRATE" : status;
            bool alert = phase == Phase.Lowering && line == "ALIGN OVER TARGET";
            if (!alert || blink)
                s.Text(line, cx, h - 22, TextAlign.Center, 1, 1f);
            if (phase == Phase.Lowering)
            {
                if (!tilt.IsFlat)
                {
                    // Not steering yet: ask for the pose
                    if (blink)
                        s.Text("HOLD PHONE FLAT", cx, cy - 30, TextAlign.Center, 1, 1f);
                    s.Text("CAMERA TO THE FLOOR", cx, cy - 20, TextAlign.Center, 1, 0.8f);
                }
                else if (elapsed < 8f)
                {
                    s.Text(tilt.HasSensor ? "TIP PHONE TO SWING" : "ARROWS / DRAG TO SWING", cx, h - 12, TextAlign.Center, 1, 0.6f);
                }
                DrawLevel(s, 6, h - 34);
            }
            overlay.Present();
        }

        /// <summary>A bubble level: where the phone leans (a dot in a ring), dimmed while it isn't held flat.</summary>
        void DrawLevel(PixelCanvas s, int x, int y)
        {
            const int r = 11;
            int cx = x + r, cy = y + r;
            float k = tilt.IsFlat ? 0.7f : 0.3f;
            s.Circle(cx, cy, r, k);
            s.Line(cx - 2, cy, cx + 2, cy, k * 0.6f);
            s.Line(cx, cy - 2, cx, cy + 2, k * 0.6f);
            var lean = tilt.Lean;
            int dx = Mathf.RoundToInt(lean.x * (r - 3)), dy = Mathf.RoundToInt(-lean.y * (r - 3));
            s.FillRect(cx + dx - 1, cy + dy - 1, 3, 3, tilt.IsFlat ? 1f : 0.4f);
        }

        void Silt(Vector3 at)
        {
            var go = new GameObject("SiltBurst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startColor = new Color(0.55f, 0.58f, 0.5f, 0.55f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.2f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 1.5f;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = siltMaterial;
            ps.Play();
        }

        void OnDestroy()
        {
            if (Busy)
            {
                CloseView();
                DestroyRig();
            }
        }
    }
}
