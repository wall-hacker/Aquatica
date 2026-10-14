using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Aquatica.AR
{
    /// <summary>
    /// Projects a placement area from the centre of the camera view onto detected planes.
    /// The area glides along the planes as the device moves; a tap anywhere anchors it
    /// and spawns the gateway at that location.
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager), typeof(ARAnchorManager))]
    public class PortalPlacementController : MonoBehaviour
    {
        enum State { Searching, Placed }

        [SerializeField] Camera arCamera;
        [SerializeField] PlacementArea placementArea;
        [SerializeField] GameObject gatewayPrefab;

        [Tooltip("Only follow upward-facing horizontal planes (floors, tables).")]
        [SerializeField] bool horizontalPlanesOnly = true;
        [Tooltip("How quickly the area catches up with the projected point. Higher = snappier.")]
        [SerializeField] float followSharpness = 14f;

        [SerializeField] UnityEvent<ARAnchor> onGatewayPlaced;

        static readonly List<ARRaycastHit> Hits = new();

        ARRaycastManager raycastManager;
        ARAnchorManager anchorManager;
        State state = State.Searching;
        ARPlane targetPlane;
        Pose targetPose;
        bool hasTarget;

        void Awake()
        {
            raycastManager = GetComponent<ARRaycastManager>();
            anchorManager = GetComponent<ARAnchorManager>();
            if (arCamera == null)
                arCamera = Camera.main;
        }

        void Start() => placementArea.HideImmediate();

        void Update()
        {
            if (state != State.Searching)
                return;

            UpdateTarget();
            UpdateArea();

            if (hasTarget && WasTappedThisFrame())
                PlaceGateway();
        }

        void UpdateTarget()
        {
            hasTarget = false;
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(screenCenter, Hits, TrackableType.PlaneWithinPolygon))
                return;

            foreach (var hit in Hits)
            {
                if (hit.trackable is not ARPlane plane)
                    continue;
                if (horizontalPlanesOnly && plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                targetPlane = plane;
                targetPose = new Pose(hit.pose.position, FacingCameraRotation(hit.pose.position, hit.pose.up));
                hasTarget = true;
                return;
            }
        }

        void UpdateArea()
        {
            if (!hasTarget)
            {
                placementArea.Hide();
                return;
            }

            var areaTransform = placementArea.transform;
            if (!placementArea.IsVisible)
            {
                // Reappearing: snap instead of sweeping across the room from the last position
                areaTransform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
            }
            else
            {
                float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
                areaTransform.SetPositionAndRotation(
                    Vector3.Lerp(areaTransform.position, targetPose.position, t),
                    Quaternion.Slerp(areaTransform.rotation, targetPose.rotation, t));
            }

            placementArea.Show();
        }

        void PlaceGateway()
        {
            var areaTransform = placementArea.transform;
            var pose = new Pose(areaTransform.position, areaTransform.rotation);
            var anchor = anchorManager.AttachAnchor(targetPlane, pose);
            if (anchor == null)
            {
                Debug.LogWarning("[Aquatica] Failed to create anchor; tap again to retry.");
                return;
            }

            areaTransform.SetParent(anchor.transform, true);
            placementArea.SetPromptVisible(false);
            placementArea.Hide(); // the gateway model takes over

            if (gatewayPrefab != null)
                Instantiate(gatewayPrefab, pose.position, pose.rotation, anchor.transform);

            state = State.Placed;
            Debug.Log($"[Aquatica] Gateway anchored at {pose.position} on plane {targetPlane.trackableId}");
            onGatewayPlaced.Invoke(anchor);
        }

        /// <summary>Yaw the area so its forward axis points away from the camera, keeping it flat on the plane.</summary>
        Quaternion FacingCameraRotation(Vector3 position, Vector3 up)
        {
            var forward = Vector3.ProjectOnPlane(position - arCamera.transform.position, up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(arCamera.transform.forward, up);
            return Quaternion.LookRotation(forward, up);
        }

        static bool WasTappedThisFrame()
        {
            var pointer = Pointer.current;
            return pointer != null && pointer.press.wasPressedThisFrame;
        }
    }
}
