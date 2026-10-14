using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Something in the cockpit that reacts to a finger (or the mouse in the editor).</summary>
    public interface ICockpitInteractable
    {
        /// <summary>A pointer pressed on this object's collider.</summary>
        void OnPointerDown(Ray ray, RaycastHit hit);

        /// <summary>The same pointer moved while still held (called every frame it is held).</summary>
        void OnPointerDrag(Ray ray);

        /// <summary>The pointer was lifted or cancelled.</summary>
        void OnPointerUp();
    }
}
