using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace Aquatica.AR
{
    /// <summary>
    /// One-way portal logic. The interior only renders where the portal mask stamped the stencil, and
    /// the mask is single-sided, so the interior is only visible from the front (-Z) of the gateway.
    /// Stepping through the opening from the front switches to "inside": the interior (a huge ball around
    /// the doorway) then renders everywhere except the doorway, which keeps showing the real world. Crossing
    /// from any other side never enters, so walking around or behind the gateway there is nothing to bump into.
    /// </summary>
    public class PortalTraversal : MonoBehaviour
    {
        static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        [SerializeField] Renderer portalMask;
        [SerializeField] Transform interiorRoot;

        [Header("Opening and interior (gateway local space, metres)")]
        [SerializeField] float openingHalfWidth = 0.5f;
        [SerializeField] float openingHeight = 1.98f;
        [Tooltip("How far in the dive destination is (used by PortalDive).")]
        [SerializeField] float corridorLength = 3.35f;
        [SerializeField] Vector3 interiorCentre = new(0f, 1f, 0f);
        [SerializeField] float interiorRadius = 6f;
        [Tooltip("Slack around the opening when deciding whether the viewer stepped through it.")]
        [SerializeField] float boundsMargin = 0.35f;
        [Tooltip("Switch slightly before the camera reaches the portal plane so the near clip plane never cuts through it.")]
        [SerializeField] float planeOffset = 0.12f;

        [SerializeField] UnityEvent onEntered;
        [SerializeField] UnityEvent onExited;

        readonly List<Material> interiorMaterials = new();
        Material maskMaterial;
        Transform viewer;
        float lastDepth;
        bool hasLast;

        public bool IsInside { get; private set; }
        public float CorridorLength => corridorLength;

        void Awake()
        {
            maskMaterial = portalMask.material;
            foreach (var r in interiorRoot.GetComponentsInChildren<Renderer>(true))
                interiorMaterials.AddRange(r.materials);
            ApplyState();
        }

        void OnDestroy()
        {
            Destroy(maskMaterial);
            foreach (var material in interiorMaterials)
                Destroy(material);
        }

        void Update()
        {
            // InverseTransformPoint is meaningless while the spawn animation starts from zero scale
            if (transform.lossyScale.x < 0.01f)
            {
                hasLast = false;
                return;
            }

            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null)
                    return;
                viewer = cam.transform;
            }

            var local = transform.InverseTransformPoint(viewer.position);
            float depth = local.z + planeOffset; // > 0: at or past the portal plane
            if (hasLast)
            {
                if (!IsInside && lastDepth <= 0f && depth > 0f && WithinOpening(local))
                    SetInside(true);
                else if (IsInside && (depth <= 0f || LeftInterior(local)))
                    SetInside(false);
            }

            lastDepth = depth;
            hasLast = true;
        }

        bool WithinOpening(Vector3 local) =>
            Mathf.Abs(local.x) < openingHalfWidth && local.y > -boundsMargin && local.y < openingHeight + boundsMargin;

        bool LeftInterior(Vector3 local) => (local - interiorCentre).magnitude > interiorRadius;

        void SetInside(bool inside)
        {
            if (IsInside == inside)
                return;

            IsInside = inside;
            ApplyState();
            Debug.Log(inside ? "[Aquatica] Entered the gateway" : "[Aquatica] Left the gateway");
            (inside ? onEntered : onExited).Invoke();
        }

        void ApplyState()
        {
            // Outside: interior only through the front of the doorway.
            // Inside: interior everywhere except the doorway, which now shows the real world from either side.
            var comp = IsInside ? CompareFunction.NotEqual : CompareFunction.Equal;
            foreach (var material in interiorMaterials)
                material.SetFloat(StencilCompId, (float)comp);
            maskMaterial.SetFloat(CullId, (float)(IsInside ? CullMode.Off : CullMode.Back));
        }
    }
}
