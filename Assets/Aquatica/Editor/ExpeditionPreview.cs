using System.Linq;
using System.Text;
using Aquatica.Expedition;
using Aquatica.Navigation;
using Aquatica.Underwater;
using UnityEditor;
using UnityEngine;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Aquatica > Preview Expedition Layout: generates the underwater world (its configured seed) into the open scene
    /// with the crates buried where the game will put them and a marker for the gateway, and logs distances and a
    /// rough route length. The preview is never saved with the scene; Aquatica > Clear Expedition Preview removes it.
    /// </summary>
    public static class ExpeditionPreview
    {
        const string WorldPath = "Assets/Aquatica/Prefabs/UnderwaterWorld.prefab";
        const string PreviewName = "ExpeditionPreview";
        const float SubSpeed = 3f;

        [MenuItem("Aquatica/Preview Expedition Layout")]
        static void Preview()
        {
            Clear();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath);
            var go = (GameObject)Object.Instantiate(prefab);
            go.name = PreviewName;
            go.hideFlags = HideFlags.DontSave;
            // Only the terrain and the crates: nothing in the preview should run
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (b is not UnderwaterWorld)
                    b.enabled = false;

            var world = go.GetComponent<UnderwaterWorld>();
            world.Generate();
            var motor = go.GetComponentInChildren<SubmarineMotor>();
            var field = go.GetComponentInChildren<CrateField>();
            var floor = world.SeafloorTransform;
            var start = floor.InverseTransformPoint(motor.transform.position);
            var layout = ExpeditionLayout.Generate(world, motor.AreaRadius, start, field.CratePrefab.LocalBounds(), field.Settings, world.Seed,
                field.MaxLidY(world, motor));

            var root = new GameObject("Crates").transform;
            root.SetParent(floor, false);
            int terrainLayer = LayerMask.NameToLayer(UnderwaterWorld.TerrainLayerName);
            var report = new StringBuilder($"[Aquatica] Expedition preview, seed {world.Seed}: {layout.Crates.Count} crates\n");
            var start2 = new Vector2(start.x, start.z);
            for (int i = 0; i < layout.Crates.Count; i++)
            {
                var c = layout.Crates[i];
                Object.Instantiate(field.CratePrefab, root).Init(i, c, terrainLayer);
                var offset = new Vector2(c.position.x, c.position.z) - start2;
                string tier = c.tier == 0 ? "low" : c.tier == 2 ? "high" : "mid";
                report.AppendLine($"  Crate {i}: {tier}, lid at {c.lidY:F1} m, band {c.band}, {offset.magnitude:F0} m, bearing {Mathf.Repeat(Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg, 360f):F0}°, " +
                                  $"{c.burial:P0} buried, scale {c.scale:F2}");
            }

            var gate2 = new Vector2(layout.GatePosition.x, layout.GatePosition.z);
            if (layout.HasGate)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "GatewaySite";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(floor, false);
                marker.transform.SetLocalPositionAndRotation(layout.GatePosition + Vector3.up * 15f, layout.GateRotation);
                marker.transform.localScale = new Vector3(4f, 15f, 4f);
                report.AppendLine($"  Gateway: {(gate2 - start2).magnitude:F0} m from the start");
            }

            // Nearest-first route through every crate, then to the gateway
            var left = layout.Crates.Select(c => new Vector2(c.position.x, c.position.z)).ToList();
            var at = start2;
            float route = 0f;
            while (left.Count > 0)
            {
                var next = left.OrderBy(p => Vector2.Distance(p, at)).First();
                route += Vector2.Distance(next, at);
                at = next;
                left.Remove(next);
            }
            route += Vector2.Distance(at, gate2);
            report.AppendLine($"  Nearest-first route, all crates then the gateway: {route:F0} m (~{route / SubSpeed / 60f:F1} min of travel at full speed)");
            Debug.Log(report.ToString());
            Selection.activeGameObject = go;
        }

        [MenuItem("Aquatica/Clear Expedition Preview")]
        static void Clear()
        {
            // FindObjectsByType skips DontSave objects
            foreach (var t in Resources.FindObjectsOfTypeAll<UnderwaterWorld>())
                if (t.name == PreviewName && !EditorUtility.IsPersistent(t))
                    Object.DestroyImmediate(t.gameObject);
        }
    }
}
