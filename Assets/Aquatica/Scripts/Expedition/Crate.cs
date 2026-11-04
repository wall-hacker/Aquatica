using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// One of the expedition's buried supply crates (prefab root). Placed by <see cref="CrateField"/>; what it
    /// holds is only decided when the magnet brings it up.
    /// </summary>
    public class Crate : MonoBehaviour
    {
        [Tooltip("Collider that blocks the submarine (on the terrain layer).")]
        [SerializeField] BoxCollider blocker;

        public int Index { get; private set; }
        public CrateSite Site { get; private set; }
        public bool Captured { get; private set; }

        /// <summary>Middle of the lid, in world space (where the magnet has to land).</summary>
        public Vector3 LidCentre => transform.TransformPoint(lidLocal);

        Vector3 lidLocal;

        /// <summary>The model's bounds in the prefab root's space (unscaled).</summary>
        public Bounds LocalBounds()
        {
            var bounds = new Bounds();
            bool first = true;
            var toRoot = transform.worldToLocalMatrix;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var m = toRoot * filter.transform.localToWorldMatrix;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one));
                    if (first)
                        bounds = new Bounds(corner, Vector3.zero);
                    else
                        bounds.Encapsulate(corner);
                    first = false;
                }
            }
            return bounds;
        }

        public void Init(int index, CrateSite site, int terrainLayer)
        {
            Index = index;
            Site = site;
            name = $"Crate_{index:00}";
            transform.SetLocalPositionAndRotation(site.position, site.rotation);
            transform.localScale = Vector3.one * site.scale;
            var b = LocalBounds();
            lidLocal = b.center + Vector3.up * b.extents.y;
            if (blocker != null && terrainLayer >= 0)
                blocker.gameObject.layer = terrainLayer;
        }

        public void MarkCaptured()
        {
            Captured = true;
            if (blocker != null)
                blocker.enabled = false;
        }
    }
}
