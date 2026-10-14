using System;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Rotary key switch under a flip-up safety cover. Tap the cover to open/close it; with the cover
    /// open, press the key to turn it. The key springs back shortly after it is released, so turning
    /// two of them "at once" means holding both together (or turning the second quickly enough).
    /// </summary>
    public class KeySwitch : MonoBehaviour
    {
        [SerializeField] Transform cover;
        [SerializeField] Transform key;
        [SerializeField] Collider coverCollider;
        [SerializeField] Collider keyCollider;
        [SerializeField] IndicatorLight indicator;
        [SerializeField] float coverOpenAngle = 105f;
        [SerializeField] float keyTurnAngle = -90f;
        [Tooltip("Seconds the key stays turned after it is released.")]
        [SerializeField] float springBackDelay = 1.2f;
        [SerializeField] float speed = 14f;

        public event Action<KeySwitch> CoverChanged;
        public event Action<KeySwitch> KeyChanged;

        public bool IsCoverOpen { get; private set; }
        public bool IsKeyTurned { get; private set; }
        public IndicatorLight Indicator => indicator;

        bool keyHeld;
        float releaseTime;

        void Awake()
        {
            keyCollider.enabled = false;
        }

        void Update()
        {
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            cover.localRotation = Quaternion.Slerp(cover.localRotation, Quaternion.Euler(IsCoverOpen ? coverOpenAngle : 0f, 0f, 0f), t);
            key.localRotation = Quaternion.Slerp(key.localRotation, Quaternion.Euler(0f, 0f, IsKeyTurned ? keyTurnAngle : 0f), t);

            if (IsKeyTurned && !keyHeld && Time.time - releaseTime > springBackDelay)
                SetKey(false);
        }

        public void SetCoverOpen(bool open)
        {
            if (IsCoverOpen == open)
                return;
            IsCoverOpen = open;
            keyCollider.enabled = open;
            if (!open)
                SetKey(false);
            CoverChanged?.Invoke(this);
        }

        void SetKey(bool turned)
        {
            if (IsKeyTurned == turned)
                return;
            IsKeyTurned = turned;
            KeyChanged?.Invoke(this);
        }

        internal void PressKey()
        {
            if (!IsCoverOpen)
                return;
            keyHeld = true;
            SetKey(true);
        }

        internal void ReleaseKey()
        {
            keyHeld = false;
            releaseTime = Time.time;
        }
    }
}
