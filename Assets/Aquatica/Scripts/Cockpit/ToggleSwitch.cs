using Aquatica.Audio;
using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Two-position toggle switch with an indicator light underneath. Tap to flip.</summary>
    public class ToggleSwitch : MonoBehaviour, ICockpitInteractable
    {
        [Tooltip("Lever pivot; tilts around its local X axis.")]
        [SerializeField] Transform lever;
        [SerializeField] IndicatorLight indicator;
        [SerializeField] float throwAngle = 28f;
        [SerializeField] float speed = 18f;
        [SerializeField] bool isOn;

        public event Action<ToggleSwitch> Toggled;
        public bool IsOn => isOn;
        public IndicatorLight Indicator => indicator;

        void Start() => lever.localRotation = TargetRotation();

        void Update()
        {
            lever.localRotation = Quaternion.Slerp(lever.localRotation, TargetRotation(), 1f - Mathf.Exp(-speed * Time.deltaTime));
        }

        Quaternion TargetRotation() => Quaternion.Euler(isOn ? throwAngle : -throwAngle, 0f, 0f);

        public void SetOn(bool on, bool notify = true)
        {
            if (isOn == on)
                return;
            isOn = on;
            if (notify)
                Toggled?.Invoke(this);
        }

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            Sfx.PlayAt(SoundBank.Clack, transform.position, 0.75f);
            SetOn(!isOn);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() { }
    }
}
