using Aquatica.Audio;
using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Momentary push button: sinks in while held and fires <see cref="Pressed"/> on press.</summary>
    public class PushButton : MonoBehaviour, ICockpitInteractable
    {
        [Tooltip("The moving cap (local -Z points out of the panel).")]
        [SerializeField] Transform cap;
        [SerializeField] float travel = 0.012f;
        [SerializeField] float speed = 25f;

        public event Action<PushButton> Pressed;
        public bool IsHeld { get; private set; }

        Vector3 restPosition;

        void Awake() => restPosition = cap.localPosition;

        void Update()
        {
            var target = restPosition + (IsHeld ? Vector3.forward * travel : Vector3.zero);
            cap.localPosition = Vector3.Lerp(cap.localPosition, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        }

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            IsHeld = true;
            Sfx.PlayAt(SoundBank.Click, transform.position, 0.7f);
            Pressed?.Invoke(this);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp()
        {
            if (IsHeld)
                Sfx.PlayAt(SoundBank.ClickUp, transform.position, 0.5f);
            IsHeld = false;
        }
    }
}
