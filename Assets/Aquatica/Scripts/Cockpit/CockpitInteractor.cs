using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Routes touches (several at once, so two key switches can be held together) and the mouse in the
    /// editor to <see cref="ICockpitInteractable"/> colliders under the pointer.
    /// </summary>
    public class CockpitInteractor : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] LayerMask layers = ~0;
        [SerializeField] float maxDistance = 6f;

        const int MouseId = -1;
        readonly Dictionary<int, ICockpitInteractable> active = new();
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Update()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                    return;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    Handle(i, touch.press.wasPressedThisFrame, touch.press.isPressed, touch.press.wasReleasedThisFrame, touch.position.ReadValue());
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var button = mouse.leftButton;
                Handle(MouseId, button.wasPressedThisFrame, button.isPressed, button.wasReleasedThisFrame, mouse.position.ReadValue());
            }
        }

        void Handle(int id, bool pressed, bool held, bool released, Vector2 screenPosition)
        {
            var ray = viewCamera.ScreenPointToRay(screenPosition);

            if (pressed)
            {
                Release(id);
                if (TryCarriedControl(ray, out var hit) || Physics.Raycast(ray, out hit, maxDistance, layers, QueryTriggerInteraction.Collide))
                {
                    var target = hit.collider.GetComponentInParent<ICockpitInteractable>();
                    if (target != null)
                    {
                        active[id] = target;
                        target.OnPointerDown(ray, hit);
                    }
                }
            }
            else if (held && active.TryGetValue(id, out var dragged))
            {
                dragged.OnPointerDrag(ray);
            }

            if (released || !held)
                Release(id);
        }

        /// <summary>
        /// Controls carried with the camera (e.g. the navigation throttle) always draw on top of the cockpit, so
        /// they win over any wall the ray would hit first.
        /// </summary>
        bool TryCarriedControl(Ray ray, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(ray, hits, maxDistance, layers, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance < best && hit.collider.transform.IsChildOf(viewCamera.transform))
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return best < float.MaxValue;
        }

        void Release(int id)
        {
            if (!active.Remove(id, out var target))
                return;
            if (target is Object unityObject && unityObject == null)
                return;
            target.OnPointerUp();
        }

        void OnDisable()
        {
            foreach (var target in active.Values)
                if (!(target is Object unityObject) || unityObject != null)
                    target.OnPointerUp();
            active.Clear();
        }
    }
}
