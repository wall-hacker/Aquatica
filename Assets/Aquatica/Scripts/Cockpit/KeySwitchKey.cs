using Aquatica.Audio;
using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>Press target on a key switch's key: turns it while held.</summary>
    public class KeySwitchKey : MonoBehaviour, ICockpitInteractable
    {
        KeySwitch owner;

        void Awake() => owner = GetComponentInParent<KeySwitch>();

        public void OnPointerDown(Ray ray, RaycastHit hit)
        {
            if (owner.IsCoverOpen)
                Sfx.PlayAt(SoundBank.KeyTurn, transform.position, 0.75f);
            owner.PressKey();
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp()
        {
            if (owner.IsCoverOpen)
                Sfx.PlayAt(SoundBank.ClickUp, transform.position, 0.45f);
            owner.ReleaseKey();
        }
    }
}
