using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquatica.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Bakes the colliders for <see cref="SolidGeometry"/>: one double-sided collision mesh per mesh below it
    /// (so walls block from either side), leaving out triangles inside any <see cref="CollisionPassThroughZone"/>
    /// in the same hierarchy. Collision meshes are saved as assets so they work with non-readable models.
    /// </summary>
    public static class SolidColliderBaker
    {
        [MenuItem("Aquatica/Bake Solid Colliders")]
        static void BakeAllPrefabs()
        {
            int baked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<SolidGeometry>(true) == null)
                    continue;
                BakePrefab(path);
                baked++;
            }
            Debug.Log($"[Aquatica] Baked solid colliders in {baked} prefab(s)");
        }

        [MenuItem("CONTEXT/SolidGeometry/Bake Colliders")]
        static void BakeFromContext(MenuCommand command) => Bake((SolidGeometry)command.context);

        public static void BakePrefab(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var solid in root.GetComponentsInChildren<SolidGeometry>(true))
                    Bake(solid);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void Bake(SolidGeometry solid)
        {
            string assetPath = solid.ColliderAssetPath;
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            var zones = solid.transform.root.GetComponentsInChildren<CollisionPassThroughZone>(true);
            int removed = 0, kept = 0;
            foreach (var filter in solid.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh;
                if (source == null)
                    continue;

                var vertices = source.vertices;
                var triangles = source.triangles;
                var toWorld = filter.transform.localToWorldMatrix;
                var collision = new List<int>(triangles.Length * 2);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var centroid = toWorld.MultiplyPoint3x4((vertices[a] + vertices[b] + vertices[c]) / 3f);
                    if (zones.Any(z => z.Contains(centroid)))
                    {
                        removed++;
                        continue;
                    }
                    kept++;
                    // Both windings, so the wall blocks from inside and outside
                    collision.Add(a); collision.Add(b); collision.Add(c);
                    collision.Add(a); collision.Add(c); collision.Add(b);
                }

                var mesh = new Mesh { name = $"{filter.name}_Collision" };
                mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = vertices;
                mesh.SetTriangles(collision, 0);
                mesh.RecalculateBounds();
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                    AssetDatabase.CreateAsset(mesh, assetPath);
                else
                    AssetDatabase.AddObjectToAsset(mesh, assetPath);

                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null)
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.convex = false;
                collider.sharedMesh = mesh;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Aquatica] {solid.name}: baked {kept} solid triangles, {removed} left out by pass-through zones");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
