using UnityEngine;

namespace Aquatica.Player
{
    /// <summary>
    /// Marks a model whose meshes the player cannot walk through. Use Aquatica > Bake Solid Colliders (or the
    /// context menu) in the editor to generate double-sided mesh colliders for every mesh below this object;
    /// triangles inside any <see cref="CollisionPassThroughZone"/> in the same hierarchy are left out.
    /// Re-bake after changing the model or the zones.
    /// </summary>
    public class SolidGeometry : MonoBehaviour
    {
        [Tooltip("Where the baked collision meshes are stored (one asset per SolidGeometry).")]
        [SerializeField] string colliderAssetPath = "Assets/Aquatica/Meshes/Colliders/Solid.asset";

        public string ColliderAssetPath => colliderAssetPath;
    }
}
