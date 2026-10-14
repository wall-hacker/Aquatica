using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Tap target on a key switch's safety cover: flips it open or closed.</summary>
    public class KeySwitchCover : MonoBehaviour, ICockpitInteractable
    {
        KeySwitch owner;

        void Awake() => owner = GetComponentInParent<KeySwitch>();

        public void OnPointerDown(Ray ray, RaycastHit hit) => owner.SetCoverOpen(!owner.IsCoverOpen);

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() { }
    }
}
