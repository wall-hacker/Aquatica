using Aquatica.Underwater;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;

namespace Aquatica.AR
{
    /// <summary>
    /// Once the viewer has walked through the gateway and nearly reached the end of the corridor, plays a
    /// full-screen transition and, while the screen is black, swaps the AR scene for the underwater world
    /// with the viewer standing in the submarine's cockpit.
    /// </summary>
    [RequireComponent(typeof(PortalTraversal))]
    public class PortalDive : MonoBehaviour
    {
        [SerializeField] UnderwaterWorld underwaterWorldPrefab;
        [SerializeField] Material transitionMaterial;

        [Tooltip("How close to the end of the corridor (m) the viewer has to get.")]
        [SerializeField] float triggerDistance = 0.6f;
        [SerializeField] float closeDuration = 0.8f;
        [SerializeField] float holdDuration = 0.4f;
        [SerializeField] float openDuration = 1.2f;

        [SerializeField] UnityEvent<UnderwaterWorld> onDived;

        PortalTraversal traversal;
        Transform viewer;
        bool diving;

        void Awake() => traversal = GetComponent<PortalTraversal>();

        void Update()
        {
            if (diving || !traversal.IsInside)
                return;

            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null)
                    return;
                viewer = cam.transform;
            }

            var local = transform.InverseTransformPoint(viewer.position);
            if (local.z >= traversal.CorridorLength - triggerDistance)
                Dive();
        }

        void Dive()
        {
            diving = true;
            Debug.Log("[Aquatica] Diving through the portal");
            ScreenTransition.Play(transitionMaterial, closeDuration, holdDuration, openDuration, OnScreenCovered);
        }

        void OnScreenCovered()
        {
            var cam = Camera.main;
            var world = Instantiate(underwaterWorldPrefab);
            world.Enter(cam, transform.position.y, transform.forward);

            HideAugmentedReality();
            onDived.Invoke(world);
            gameObject.SetActive(false);
        }

        static void HideAugmentedReality()
        {
            var planeController = FindAnyObjectByType<ARPlaneDetectionController>();
            if (planeController != null)
                planeController.SetDetectionEnabled(false);
            else
            {
                var planeManager = FindAnyObjectByType<ARPlaneManager>();
                if (planeManager != null)
                {
                    planeManager.enabled = false;
                    foreach (var plane in planeManager.trackables)
                        plane.gameObject.SetActive(false);
                }
            }

            var placementArea = FindAnyObjectByType<PlacementArea>();
            if (placementArea != null)
                placementArea.HideImmediate();
        }
    }
}
