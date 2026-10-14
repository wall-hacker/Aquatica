using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Press target on a key switch's key: turns it while held.</summary>
    public class KeySwitchKey : MonoBehaviour, ICockpitInteractable
    {
        KeySwitch owner;

        void Awake() => owner = GetComponentInParent<KeySwitch>();

        public void OnPointerDown(Ray ray, RaycastHit hit) => owner.PressKey();

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() => owner.ReleaseKey();
    }
}
