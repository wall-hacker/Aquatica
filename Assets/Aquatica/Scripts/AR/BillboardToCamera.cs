using UnityEngine;

namespace Aquatica.AR
{
    /// <summary>Keeps this transform facing the camera so world-space text stays readable.</summary>
    public class BillboardToCamera : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;
        [Tooltip("Rotate only around the world up axis so the text stays upright.")]
        [SerializeField] bool keepUpright = true;

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null)
                    return;
            }

            // TMP text reads correctly when its forward points away from the viewer
            var forward = transform.position - targetCamera.transform.position;
            if (keepUpright)
                forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                return;

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
