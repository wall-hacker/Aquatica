using UnityEngine;

namespace Aquatica.Player
{
    /// <summary>
    /// Keeps a tracked AR/VR camera out of solid geometry. The real player can always walk further than the
    /// virtual space allows, so instead of moving the camera (tracking owns it) this shifts the whole camera
    /// rig back by however far the camera tried to go into a wall. The camera is swept as a small sphere from
    /// its last valid position, so it stops at walls and slides along them instead of tunnelling through.
    /// Hits inside a <see cref="CollisionPassThroughZone"/> are ignored.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CameraWallBlocker : MonoBehaviour
    {
        [Tooltip("The tracked camera.")]
        [SerializeField] Transform viewer;
        [Tooltip("What gets shifted to keep the camera out of walls (the XR Origin). Defaults to the camera's root.")]
        [SerializeField] Transform rig;
        [Tooltip("Size of the player's head.")]
        [SerializeField] float radius = 0.15f;
        [SerializeField] float skin = 0.01f;
        [SerializeField] LayerMask solidLayers = ~0;
        [SerializeField] int maxSlideIterations = 3;

        readonly RaycastHit[] hits = new RaycastHit[32];
        Vector3 lastPosition;
        bool hasLast;

        /// <summary>Total distance the rig has been pushed back so far (for debugging or effects).</summary>
        public Vector3 AccumulatedCorrection { get; private set; }

        /// <summary>Returns the blocker driving this camera, adding one to the camera rig's root if needed.</summary>
        public static CameraWallBlocker Ensure(Camera camera)
        {
            var existing = FindAnyObjectByType<CameraWallBlocker>();
            if (existing != null)
                return existing;
            var blocker = camera.transform.root.gameObject.AddComponent<CameraWallBlocker>();
            blocker.viewer = camera.transform;
            // Terrain outside the hull is the submarine's business, not the player's head
            blocker.solidLayers &= ~LayerMask.GetMask(Underwater.UnderwaterWorld.TerrainLayerName);
            return blocker;
        }

        /// <summary>Forget the last position, e.g. after deliberately teleporting the rig.</summary>
        public void ResetTracking() => hasLast = false;

        /// <summary>
        /// The rig was carried along by something moving (e.g. the submarine) together with the walls around it:
        /// move the last valid position along too, so the carried motion isn't swept against the walls.
        /// </summary>
        public void Carry(Matrix4x4 delta)
        {
            if (hasLast)
                lastPosition = delta.MultiplyPoint3x4(lastPosition);
        }

        void Awake()
        {
            if (viewer == null && Camera.main != null)
                viewer = Camera.main.transform;
        }

        void OnEnable()
        {
            hasLast = false;
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void LateUpdate() => Resolve();

        // Tracking also updates the camera right before rendering; correct again afterwards
        [BeforeRenderOrder(1000)]
        void OnBeforeRender() => Resolve();

        void Resolve()
        {
            if (viewer == null)
                return;
            if (rig == null)
                rig = viewer.root;

            var desired = viewer.position;
            if (!hasLast)
            {
                lastPosition = desired;
                hasLast = true;
                return;
            }

            var position = lastPosition;
            var move = desired - position;
            for (int i = 0; i < maxSlideIterations; i++)
            {
                float distance = move.magnitude;
                if (distance < 1e-5f)
                    break;
                var direction = move / distance;
                if (!Sweep(position, direction, distance + skin, out var hit))
                {
                    position += move;
                    move = Vector3.zero;
                    break;
                }

                float travel = Mathf.Max(0f, hit.distance - skin);
                position += direction * travel;
                // Slide with whatever movement is left. Against walls only the horizontal push is removed, so
                // curved walls don't drag the camera away from the real head height; floors/ceilings slide fully.
                var normal = hit.normal;
                var horizontal = new Vector3(normal.x, 0f, normal.z);
                if (Mathf.Abs(normal.y) < 0.7f && horizontal.sqrMagnitude > 1e-6f)
                    normal = horizontal.normalized;
                move = Vector3.ProjectOnPlane(move - direction * travel, normal);
            }

            var correction = position - desired;
            if (correction.sqrMagnitude > 1e-10f)
            {
                rig.position += correction;
                AccumulatedCorrection += correction;
            }
            lastPosition = viewer.position;
        }

        bool Sweep(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, distance, solidLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                // Overlaps at the start (distance 0), the rig's own colliders and pass-through zones don't block
                if (hit.distance <= 0f || hit.collider.transform.IsChildOf(rig))
                    continue;
                if (CollisionPassThroughZone.AnyContains(hit.point))
                    continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return best < float.MaxValue;
        }
    }
}
