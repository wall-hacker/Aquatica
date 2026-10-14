using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Handwheel valve on a flanged base. Drag around the wheel to spin it. Fault: a leak that is sealed
    /// by turning the wheel clockwise a number of times; a needle on the base plate sweeps from the red
    /// arc to the green arc as the valve closes.
    /// </summary>
    public class ValveStation : CockpitDevice, ICockpitInteractable
    {
        [SerializeField] Transform wheel;
        [SerializeField] Transform needle;
        [SerializeField] float turnsToSeal = 3f;
        [SerializeField] float needleRedAngle = 180f;
        [SerializeField] float needleGreenAngle = 0f;
        [SerializeField] float needleSpeed = 6f;

        /// <summary>Degrees turned this drag step; positive = clockwise as seen by the player.</summary>
        public event Action<float> Turned;

        /// <summary>0 = leaking (needle on red), 1 = sealed (needle on green).</summary>
        public float SealProgress { get; private set; } = 1f;

        float wheelAngle;
        float lastPointerAngle;
        bool dragging;

        void Update()
        {
            wheel.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);
            float target = Mathf.Lerp(needleRedAngle, needleGreenAngle, SealProgress);
            float current = needle.localEulerAngles.z;
            needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(current, target, 1f - Mathf.Exp(-needleSpeed * Time.deltaTime)));
        }

        protected override void OnFaultBegan() => SealProgress = 0f;

        protected override void OnRepaired() => SealProgress = 1f;

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            dragging = PointerAngle(ray, out lastPointerAngle);
        }

        public void OnPointerDrag(Ray ray)
        {
            if (!PointerAngle(ray, out float angle))
                return;
            if (!dragging)
            {
                dragging = true;
                lastPointerAngle = angle;
                return;
            }

            // The wheel follows the finger; angles grow counter-clockwise as the player sees it
            float delta = Mathf.DeltaAngle(lastPointerAngle, angle);
            lastPointerAngle = angle;
            wheelAngle += delta;

            float clockwise = -delta;
            Turned?.Invoke(clockwise);
            if (IsFaulty)
            {
                SealProgress = Mathf.Clamp01(SealProgress + clockwise / (360f * turnsToSeal));
                if (SealProgress >= 1f)
                    CompleteRepair();
            }
        }

        public void OnPointerUp() => dragging = false;

        /// <summary>Angle of the pointer around the wheel axis, in the station's plane (x right, y up as the player sees it).</summary>
        bool PointerAngle(Ray ray, out float angle)
        {
            angle = 0f;
            var plane = new Plane(-wheel.forward, wheel.position);
            if (!plane.Raycast(ray, out float distance))
                return false;
            var local = transform.InverseTransformPoint(ray.GetPoint(distance)) - transform.InverseTransformPoint(wheel.position);
            if (new Vector2(local.x, local.y).sqrMagnitude < 1e-4f)
                return false;
            angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            return true;
        }
    }
}
