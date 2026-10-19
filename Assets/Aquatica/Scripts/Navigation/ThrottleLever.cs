using System;
using Aquatica.Cockpit;
using UnityEngine;

namespace Aquatica.Navigation
{
    /// <summary>
    /// The floating thrust lever shown in navigation mode. It rides along with the camera (so it is always on
    /// screen) but stands upright by gravity, in the lower right of the screen as the player holds the phone: the
    /// app's camera stays in portrait, so the corner and the up direction come from gravity, not the camera, and the
    /// lever glides around when the phone is turned. It slides in and out when shown or hidden. Holding a finger on
    /// it pushes the arm forward against a heavy spring; letting go lets it swing back to STOP. Uses the
    /// Aquatica/ViewmodelLit shader so walls never cover it.
    /// </summary>
    public class ThrottleLever : MonoBehaviour, ICockpitInteractable
    {
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [Header("Parts")]
        [Tooltip("Rotates around its local X axis; +angle pushes the grip away from the player.")]
        [SerializeField] Transform arm;
        [SerializeField] Renderer statusLight;

        [Header("Placement (screen as the player holds it: x right, y up, -1..1 from the centre)")]
        [SerializeField] Vector2 screenPosition = new(0.5f, -0.6f);
        [Tooltip("Rough height of the model (m), used to keep its on-screen size steady across screen shapes.")]
        [SerializeField] float modelSize = 0.15f;
        [Tooltip("On-screen size as a fraction of the screen's shorter side.")]
        [SerializeField] float screenFraction = 0.3f;
        [Tooltip("Turn of the lever around the vertical (degrees), relative to the view direction.")]
        [SerializeField] float yaw = -20f;
        [Tooltip("Where the lever drops to when hidden, relative to its spot (same units as screenPosition).")]
        [SerializeField] Vector2 hiddenOffset = new(0.08f, -0.8f);
        [Tooltip("How quickly (s) the lever follows when the phone is turned.")]
        [SerializeField] float orientationResponseTime = 0.15f;
        [SerializeField] float showTime = 0.6f;

        [Header("Arm")]
        [SerializeField] float stopAngle = -28f;
        [SerializeField] float aheadAngle = 30f;
        [Tooltip("Spring stiffness and damping of the arm: low and soft for a heavy, underwater feel.")]
        [SerializeField] float armStiffness = 60f;
        [SerializeField] float armDamping = 9f;

        [Header("Floating")]
        [SerializeField] float bobHeight = 0.004f;
        [SerializeField] float bobPeriod = 3.2f;

        [Header("Status light")]
        [SerializeField, ColorUsage(false, true)] Color idleColor = new(1.6f, 0.45f, 0.05f);
        [SerializeField, ColorUsage(false, true)] Color thrustColor = new(0.2f, 2.4f, 0.5f);

        public bool IsHeld { get; private set; }
        public bool Visible { get; private set; }

        /// <summary>Raised when the lever is grabbed (true) or released (false).</summary>
        public event Action<bool> HeldChanged;

        Camera viewCamera;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        float show;
        float armAngle;
        float armVelocity;
        float screenUpAngle; // direction of gravity-up on the screen, degrees from the camera's +Y
        bool hasOrientation;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
            armAngle = stopAngle;
            ApplyAppearance();
            SetRenderersEnabled(false);
        }

        /// <summary>Carries the lever with this camera.</summary>
        public void Attach(Camera cam)
        {
            viewCamera = cam;
            transform.SetParent(cam.transform, false);
            Place();
        }

        public void SetVisible(bool visible)
        {
            if (Visible == visible)
                return;
            Visible = visible;
            if (!visible)
                Release();
            if (visible)
                SetRenderersEnabled(true);
        }

        void LateUpdate()
        {
            if (viewCamera == null)
                return;
            float dt = Time.deltaTime;

            show = Mathf.MoveTowards(show, Visible ? 1f : 0f, dt / Mathf.Max(0.01f, showTime));
            if (show <= 0f && !Visible)
            {
                SetRenderersEnabled(false);
                return;
            }

            // Heavy spring on the arm
            float target = IsHeld ? aheadAngle : stopAngle;
            float accel = (target - armAngle) * armStiffness - armVelocity * armDamping;
            armVelocity += accel * dt;
            armAngle += armVelocity * dt;
            arm.localRotation = Quaternion.Euler(armAngle, 0f, 0f);

            Place();
            ApplyAppearance();
        }

        void Place()
        {
            var cam = viewCamera;
            var camTransform = cam.transform;
            float dt = Time.deltaTime;

            // Which way is "up" on the physical screen: world up seen from the camera, in the image plane
            var upInCamera = camTransform.InverseTransformDirection(Vector3.up);
            if (new Vector2(upInCamera.x, upInCamera.y).sqrMagnitude > 0.01f)
            {
                float target = Mathf.Atan2(-upInCamera.x, upInCamera.y) * Mathf.Rad2Deg;
                screenUpAngle = hasOrientation
                    ? Mathf.LerpAngle(screenUpAngle, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, orientationResponseTime)))
                    : target;
                hasOrientation = true;
            }
            float a = screenUpAngle * Mathf.Deg2Rad;
            var up = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            var right = new Vector2(up.y, -up.x);

            // Same on-screen size in portrait and landscape: distance from the shorter side's field of view
            var projection = cam.projectionMatrix;
            float tanX = 1f / Mathf.Abs(projection.m00), tanY = 1f / Mathf.Abs(projection.m11);
            float distance = modelSize / (Mathf.Max(0.05f, screenFraction) * 2f * Mathf.Min(tanX, tanY));

            // Lower right as the phone is held, dropping out of view when hidden
            var spot = screenPosition + hiddenOffset * (1f - EaseOutBack(show));
            var ndc = right * spot.x + up * spot.y;
            var local = new Vector3(ndc.x * tanX * distance, ndc.y * tanY * distance, distance);
            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, bobPeriod)) * bobHeight;
            transform.position = camTransform.TransformPoint(local) + Vector3.up * bob;

            // Upright by gravity, facing along the view
            var facing = Vector3.ProjectOnPlane(camTransform.forward, Vector3.up);
            if (facing.sqrMagnitude < 0.01f)
                facing = Vector3.ProjectOnPlane(camTransform.TransformDirection(up.x, up.y, 0f), Vector3.up);
            transform.rotation = Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
        }

        void ApplyAppearance()
        {
            float opacity = Mathf.Clamp01(show * 1.6f);
            float thrust = Mathf.InverseLerp(stopAngle, aheadAngle, armAngle);
            var led = Color.Lerp(idleColor, thrustColor, Mathf.Clamp01(thrust * 1.5f));
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(block);
                block.SetFloat(OpacityId, opacity);
                if (r == statusLight)
                    block.SetColor(EmissionId, led);
                r.SetPropertyBlock(block);
            }
        }

        void SetRenderersEnabled(bool enabled)
        {
            foreach (var r in renderers)
                r.enabled = enabled;
            foreach (var c in GetComponentsInChildren<Collider>(true))
                c.enabled = enabled;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.4f, c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        void Release()
        {
            if (!IsHeld)
                return;
            IsHeld = false;
            HeldChanged?.Invoke(false);
        }

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            if (!Visible || IsHeld)
                return;
            IsHeld = true;
            HeldChanged?.Invoke(true);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() => Release();
    }
}
