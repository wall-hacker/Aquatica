using UnityEngine;
using UnityEngine.InputSystem;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The phone as a tilt board, held flat with the camera pointing at the floor (screen up). <see cref="Read"/>
    /// gives how far it leans, in screen axes: x = right edge dipped (+) or raised (-), y = top edge dipped (+) or
    /// raised (-), reaching 1 at <see cref="fullTilt"/> degrees. While the phone isn't held roughly flat,
    /// <see cref="IsFlat"/> is false and the lean reads zero. Uses the gravity sensor (or the accelerometer); in the
    /// editor or without sensors, the arrow keys / WASD or a mouse drag stand in.
    /// </summary>
    public class TiltInput
    {
        const float fullTilt = 30f;
        const float deadZone = 2.5f;
        const float flatLimit = 40f; // degrees from flat that still count as holding it flat

        Vector2 dragOrigin;

        public bool HasSensor => GravitySensor.current != null || Accelerometer.current != null;

        /// <summary>The phone is held roughly flat, screen up (always true without a sensor).</summary>
        public bool IsFlat { get; private set; } = true;

        /// <summary>Raw lean of the phone (-1..1 per axis), for a level indicator; zero without a sensor.</summary>
        public Vector2 Lean { get; private set; }

        /// <summary>Switches the sensors on (they report nothing for a frame or two after that).</summary>
        public void Enable()
        {
            if (GravitySensor.current != null && !GravitySensor.current.enabled)
                InputSystem.EnableDevice(GravitySensor.current);
            if (Accelerometer.current != null && !Accelerometer.current.enabled)
                InputSystem.EnableDevice(Accelerometer.current);
            var mouse = Mouse.current;
            if (mouse != null)
                dragOrigin = mouse.position.ReadValue();
        }

        public Vector2 Read()
        {
            var tilt = Vector2.zero;
            var g = ReadGravity();
            if (g.HasValue)
            {
                // Down is -z when the screen faces up
                var down = g.Value;
                float fromFlat = Mathf.Acos(Mathf.Clamp(-down.z, -1f, 1f)) * Mathf.Rad2Deg;
                IsFlat = fromFlat < flatLimit;
                var lean = new Vector2(down.x, down.y);
                float angle = Mathf.Asin(Mathf.Clamp01(lean.magnitude)) * Mathf.Rad2Deg;
                float amount = Mathf.Clamp01((angle - deadZone) / (fullTilt - deadZone));
                Lean = lean.sqrMagnitude > 1e-6f ? lean.normalized * amount : Vector2.zero;
                if (IsFlat)
                    tilt = Lean;
            }
            else
            {
                IsFlat = true;
                Lean = Vector2.zero;
            }

            // Stand-ins: keys and mouse drag
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) tilt.x -= 0.6f;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) tilt.x += 0.6f;
                if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) tilt.y -= 0.6f;
                if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) tilt.y += 0.6f;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    dragOrigin = mouse.position.ReadValue();
                if (mouse.leftButton.isPressed)
                    tilt += (mouse.position.ReadValue() - dragOrigin) / Mathf.Max(1f, Screen.height * 0.25f);
            }
            return Vector2.ClampMagnitude(tilt, 1f);
        }

        /// <summary>Unit "down" in screen axes (x right, y up, z out of the screen), or null without a reading.</summary>
        static Vector3? ReadGravity()
        {
            Vector3 v;
            if (GravitySensor.current != null && GravitySensor.current.enabled)
                v = GravitySensor.current.gravity.ReadValue();
            else if (Accelerometer.current != null && Accelerometer.current.enabled)
                v = Accelerometer.current.acceleration.ReadValue();
            else
                return null;
            if (v.sqrMagnitude < 0.01f)
                return null; // not reporting yet
            v.Normalize();
            if (!InputSystem.settings.compensateForScreenOrientation)
                v = ToScreen(v);
            return v;
        }

        /// <summary>Device axes (natural portrait) to the current screen orientation's axes.</summary>
        static Vector3 ToScreen(Vector3 v) => Screen.orientation switch
        {
            ScreenOrientation.LandscapeLeft => new Vector3(-v.y, v.x, v.z),
            ScreenOrientation.LandscapeRight => new Vector3(v.y, -v.x, v.z),
            ScreenOrientation.PortraitUpsideDown => new Vector3(-v.x, -v.y, v.z),
            _ => v,
        };
    }
}
