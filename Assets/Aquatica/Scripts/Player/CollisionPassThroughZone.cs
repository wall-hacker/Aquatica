using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Player
{
    /// <summary>
    /// A box the player may walk through even though there is geometry in it (e.g. seats you "sit down" into).
    /// The box is this object's transform: a unit cube scaled by <see cref="size"/> around <see cref="center"/>.
    /// <see cref="SolidGeometry"/> leaves triangles inside zones out of its baked colliders, and
    /// <see cref="CameraWallBlocker"/> ignores any other collider hits that land inside a zone.
    /// </summary>
    public class CollisionPassThroughZone : MonoBehaviour
    {
        [SerializeField] Vector3 center;
        [SerializeField] Vector3 size = Vector3.one;

        static readonly List<CollisionPassThroughZone> active = new();

        public static IReadOnlyList<CollisionPassThroughZone> Active => active;

        void OnEnable() => active.Add(this);

        void OnDisable() => active.Remove(this);

        public bool Contains(Vector3 worldPoint)
        {
            var p = transform.InverseTransformPoint(worldPoint) - center;
            var h = size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        public static bool AnyContains(Vector3 worldPoint)
        {
            foreach (var zone in active)
                if (zone.Contains(worldPoint))
                    return true;
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.15f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.8f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
