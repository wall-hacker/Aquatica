using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Aquatica.AR
{
    /// <summary>
    /// Tracks detected AR planes and exposes simple controls (toggle detection / visibility).
    /// Logs plane lifecycle events so on-device detection can be verified via logcat.
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    public class ARPlaneDetectionController : MonoBehaviour
    {
        [SerializeField] PlaneDetectionMode detectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        [SerializeField] bool logPlaneEvents = true;

        ARPlaneManager planeManager;

        public int TrackedPlaneCount => planeManager != null ? planeManager.trackables.count : 0;
        public bool IsDetecting => planeManager != null && planeManager.enabled;

        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = detectionMode;
        }

        void OnEnable() => planeManager.trackablesChanged.AddListener(OnTrackablesChanged);

        void OnDisable() => planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (!logPlaneEvents)
                return;

            foreach (var plane in args.added)
                Debug.Log($"[Aquatica] Plane added {plane.trackableId} ({plane.alignment}, {plane.size.x:F2}x{plane.size.y:F2}m)");

            foreach (var removed in args.removed)
                Debug.Log($"[Aquatica] Plane removed {removed.Key}");
        }

        public void SetDetectionEnabled(bool enabled)
        {
            planeManager.enabled = enabled;
            SetPlanesVisible(enabled);
        }

        public void ToggleDetection() => SetDetectionEnabled(!planeManager.enabled);

        public void SetPlanesVisible(bool visible)
        {
            foreach (var plane in planeManager.trackables)
                plane.gameObject.SetActive(visible);
        }
    }
}
