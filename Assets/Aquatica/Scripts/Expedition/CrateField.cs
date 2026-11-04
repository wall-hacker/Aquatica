using System.Collections.Generic;
using Aquatica.Navigation;
using Aquatica.Underwater;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// Buries the expedition's crates (and reserves the gateway's site) once the world has been generated, laid out
    /// by <see cref="ExpeditionLayout"/> from the world seed. Other systems read the layout through
    /// <see cref="TryGetLayout"/> (e.g. so seaweed doesn't grow over the crates) and the crates through
    /// <see cref="Crates"/>.
    /// </summary>
    public class CrateField : MonoBehaviour
    {
        [SerializeField] Crate cratePrefab;
        [SerializeField] ExpeditionLayoutSettings settings = new();
        [Tooltip("Found in the world if left empty.")]
        [SerializeField] SubmarineMotor motor;

        UnderwaterWorld world;
        ExpeditionLayout layout;
        readonly List<Crate> crates = new();
        bool spawned;

        public Crate CratePrefab => cratePrefab;
        public ExpeditionLayoutSettings Settings => settings;
        public IReadOnlyList<Crate> Crates => crates;
        public bool IsReady => spawned;

        /// <summary>The layout, generated on first use once the world and the submarine are ready.</summary>
        public bool TryGetLayout(out ExpeditionLayout result)
        {
            result = layout;
            if (layout != null)
                return true;
            if (cratePrefab == null)
                return false;
            if (world == null)
                world = GetComponentInParent<UnderwaterWorld>();
            if (world == null || world.Seafloor == null || world.SeafloorTransform == null)
                return false;
            if (motor == null)
                motor = world.GetComponentInChildren<SubmarineMotor>();
            if (motor == null || !motor.IsReady)
                return false;

            var start = world.SeafloorTransform.InverseTransformPoint(motor.transform.position);

            layout = ExpeditionLayout.Generate(world, motor.AreaRadius, start, cratePrefab.LocalBounds(), settings, world.Seed,
                MaxLidY(world, motor));
            Debug.Log($"[Aquatica] Expedition: {layout.Crates.Count} crates, gateway {(layout.HasGate ? "placed" : "missing")} (seed {world.Seed})");
            result = layout;
            return true;
        }

        /// <summary>
        /// Highest a crate's lid may be (seafloor-local): the magnet needs the submarine at least its shortest cable
        /// above a lid, and that has to stay below the surface current.
        /// </summary>
        public float MaxLidY(UnderwaterWorld world, SubmarineMotor motor)
        {
            var magnet = GetComponent<MagnetRig>();
            if (magnet == null || !magnet.TryGetHangHeight(out float hang))
                return float.MaxValue;
            float highestHang = motor.CruiseCeiling + hang - 1f;
            return world.SeafloorTransform.InverseTransformPoint(new Vector3(0f, highestHang, 0f)).y - magnet.MinDrop;
        }

        void Update()
        {
            if (spawned || !TryGetLayout(out var l))
                return;
            spawned = true;

            int terrainLayer = LayerMask.NameToLayer(UnderwaterWorld.TerrainLayerName);
            var root = new GameObject("Crates").transform;
            root.SetParent(world.SeafloorTransform, false);
            for (int i = 0; i < l.Crates.Count; i++)
            {
                var crate = Instantiate(cratePrefab, root);
                crate.Init(i, l.Crates[i], terrainLayer);
                crates.Add(crate);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (layout == null || world == null || world.SeafloorTransform == null)
                return;
            var t = world.SeafloorTransform;
            Gizmos.color = Color.yellow;
            foreach (var c in layout.Crates)
                Gizmos.DrawWireSphere(t.TransformPoint(c.position), 3f);
            if (layout.HasGate)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(t.TransformPoint(layout.GatePosition), 12f);
            }
        }
    }
}
