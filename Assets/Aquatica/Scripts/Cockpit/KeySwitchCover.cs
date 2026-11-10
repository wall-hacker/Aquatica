using Aquatica.Audio;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Tap target on a key switch's safety cover: flips it open or closed.</summary>
    public class KeySwitchCover : MonoBehaviour, ICockpitInteractable
    {
        KeySwitch owner;

        void Awake() => owner = GetComponentInParent<KeySwitch>();

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            bool wasOpen = owner.IsCoverOpen;
            owner.SetCoverOpen(!wasOpen);
            // A locked cover only rattles
            if (owner.IsCoverOpen != wasOpen)
                Sfx.PlayAt(SoundBank.CoverClack, transform.position, 0.75f);
            else
                Sfx.PlayAt(SoundBank.ClickUp, transform.position, 0.6f, 0.1f);
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp() { }
    }
}
