using System.Collections.Generic;
using Aquatica.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace Aquatica.Underwater
{
    /// <summary>
    /// The world on the other side of the portal: a procedurally generated seafloor (seeded) with rock arches,
    /// the water surface far overhead and the submarine the viewer stands in. <see cref="Enter"/> lines the cockpit up
    /// with the viewer, turns off the AR camera feed and switches lighting, fog and reflections to underwater.
    /// </summary>
    public class UnderwaterWorld : MonoBehaviour
    {
        static readonly int SurfaceHeightId = Shader.PropertyToID("_AquaticaSurfaceHeight");

        /// <summary>Layer of solid terrain features (rock arches) the submarine collides with.</summary>
        public const string TerrainLayerName = "Terrain";

        [Header("Generation")]
        [SerializeField] int seed = 1337;
        [Tooltip("Pick a new seed every time the world is entered.")]
        [SerializeField] bool randomizeSeed;
        [SerializeField] SeafloorSettings seafloorSettings = new();
        [SerializeField] Material seafloorMaterial;
        [SerializeField] Material waterSurfaceMaterial;
        [SerializeField] RockArchSettings archSettings = new();

        [Header("Layout (relative to the cockpit floor)")]
        [Tooltip("Where the viewer stands: on the cabin floor, facing the front window.")]
        [SerializeField] Transform cockpitAnchor;
        [SerializeField] float seafloorDepth = 5f;
        [SerializeField] float surfaceHeight = 70f;
        [Tooltip("Ground under the submarine is levelled within this radius.")]
        [SerializeField] float clearRadius = 18f;
        [SerializeField] float surfaceSize = 600f;

        [Header("Atmosphere")]
        [SerializeField] Color fogColor = new(0.015f, 0.085f, 0.12f);
        [SerializeField] float fogDensity = 0.038f;
        [SerializeField] Color ambientTop = new(0.15f, 0.32f, 0.38f);
        [SerializeField] Color ambientBottom = new(0.01f, 0.035f, 0.05f);
        [SerializeField] Light sun;
        [SerializeField] float cameraFarClip = 150f;

        [Header("Player")]
        [Tooltip("Stop the camera at the submarine walls (the AR rig is pushed back when the real player keeps walking).")]
        [SerializeField] bool blockCameraAtWalls = true;

        Seafloor seafloor;
        Transform seafloorTransform;
        Transform surfaceTransform;
        readonly List<Behaviour> disabledBehaviours = new();
        readonly List<Mesh> generatedMeshes = new();
        Cubemap reflectionCube;
        EnvironmentState savedEnvironment;

        public int Seed => seed;
        public Seafloor Seafloor => seafloor;

        /// <summary>The seafloor's transform: <see cref="Seafloor"/> coordinates are local to it.</summary>
        public Transform SeafloorTransform => seafloorTransform;

        /// <summary>World-space height of the water surface.</summary>
        public float SurfaceHeight => surfaceTransform != null ? surfaceTransform.position.y : transform.position.y + surfaceHeight;

        /// <summary>World-space seafloor height below a point (useful for placing rocks, corals and fish later).</summary>
        public float SampleSeafloorHeight(Vector3 worldPosition)
        {
            var local = seafloorTransform.InverseTransformPoint(worldPosition);
            return seafloorTransform.TransformPoint(new Vector3(local.x, seafloor.SampleLocal(local.x, local.z), local.z)).y;
        }

        /// <summary>Generates the world and places it so the cockpit floor is at floorHeight under the camera.</summary>
        public void Enter(Camera viewer, float floorHeight, Vector3 forward)
        {
            if (randomizeSeed)
                seed = Random.Range(int.MinValue, int.MaxValue);

            AlignCockpit(viewer.transform.position, floorHeight, forward);
            Generate();
            ApplyEnvironment(viewer);
            if (blockCameraAtWalls)
                CameraWallBlocker.Ensure(viewer).ResetTracking();
            Debug.Log($"[Aquatica] Entered the underwater world (seed {seed})");
        }

        void AlignCockpit(Vector3 viewerPosition, float floorHeight, Vector3 forward)
        {
            // Anchor pose relative to this root, then solve for the root pose that puts it at the target
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anchorPosition = cockpitAnchor.position;
            var anchorYaw = Quaternion.Euler(0f, cockpitAnchor.eulerAngles.y, 0f);

            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            var targetRotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            var rotation = targetRotation * Quaternion.Inverse(anchorYaw);
            var target = new Vector3(viewerPosition.x, floorHeight, viewerPosition.z);
            transform.SetPositionAndRotation(target - rotation * anchorPosition, rotation);
        }

        void Generate()
        {
            // Seafloor centred under the cockpit, in this root's space, built as tiles so only visible ones draw
            var anchorLocal = transform.InverseTransformPoint(cockpitAnchor.position);
            seafloor = new Seafloor(seafloorSettings, seed, clearRadius, anchorLocal.y - seafloorDepth);
            seafloorTransform = new GameObject("Seafloor").transform;
            seafloorTransform.SetParent(transform, false);
            seafloorTransform.localPosition = new Vector3(anchorLocal.x, 0f, anchorLocal.z);

            // Arches first: they heap rubble mounds into the ground the tiles are built from
            BuildArches();
            foreach (var tile in seafloor.BuildTiles())
                CreatePart(tile.name, tile, seafloorMaterial, Vector3.zero, true, seafloorTransform);

            surfaceTransform = CreatePart("WaterSurface", BuildSurfaceMesh(surfaceSize, 64), waterSurfaceMaterial,
                new Vector3(anchorLocal.x, anchorLocal.y + surfaceHeight, anchorLocal.z), false);

            // Caustics fade with depth below the surface
            Shader.SetGlobalFloat(SurfaceHeightId, surfaceTransform.position.y);
        }

        /// <summary>Rock arches standing on the seafloor, with convex collider chunks on the terrain layer.</summary>
        void BuildArches()
        {
            int layer = LayerMask.NameToLayer(TerrainLayerName);
            var root = new GameObject("RockArches").transform;
            root.SetParent(seafloorTransform, false);
            foreach (var arch in RockArches.Generate(seafloor, archSettings, seed))
            {
                var part = CreatePart("RockArch", arch.Mesh, seafloorMaterial, Vector3.zero, true, root);
                // Colliders on a child without a MeshFilter, so they don't pick up (and cook) the render mesh
                var colliders = new GameObject("Collision");
                colliders.transform.SetParent(part, false);
                if (layer >= 0)
                    colliders.layer = layer;
                foreach (var chunk in arch.ColliderChunks)
                {
                    generatedMeshes.Add(chunk);
                    var collider = colliders.AddComponent<MeshCollider>();
                    collider.sharedMesh = chunk;
                    collider.convex = true;
                }
            }
        }

        Transform CreatePart(string partName, Mesh mesh, Material material, Vector3 localPosition, bool receiveShadows, Transform parent = null)
        {
            generatedMeshes.Add(mesh);
            var go = new GameObject(partName);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = receiveShadows;
            return go.transform;
        }

        static Mesh BuildSurfaceMesh(float size, int segments)
        {
            int n = segments + 1;
            var vertices = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float u = (float)x / segments, v = (float)z / segments;
                vertices[z * n + x] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                uvs[z * n + x] = new Vector2(u, v);
            }

            var triangles = new int[segments * segments * 6];
            int t = 0;
            for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int i = z * n + x;
                // Wound to face down: the viewer is always below the surface
                triangles[t++] = i; triangles[t++] = i + 1; triangles[t++] = i + n;
                triangles[t++] = i + 1; triangles[t++] = i + n + 1; triangles[t++] = i + n;
            }

            var mesh = new Mesh { name = "WaterSurface", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 4f, size));
            return mesh;
        }

        void ApplyEnvironment(Camera viewer)
        {
            savedEnvironment = EnvironmentState.Capture();

            // The real world disappears: no camera feed, other lights off
            var background = viewer.GetComponent<ARCameraBackground>();
            Disable(background);
            viewer.clearFlags = CameraClearFlags.SolidColor;
            viewer.backgroundColor = fogColor;
            viewer.farClipPlane = Mathf.Max(viewer.farClipPlane, cameraFarClip);

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != sun && l.type == LightType.Directional && !l.transform.IsChildOf(transform))
                    Disable(l);

            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = GradientAmbient(ambientTop, ambientBottom);

            // Glossy surfaces should reflect the murky water (brighter towards the surface), not the default sky
            reflectionCube = GradientCubemap(ambientTop * 1.5f, Color.Lerp(fogColor, ambientTop, 0.5f), ambientBottom);
            var probe = new GameObject("UnderwaterReflections").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(transform, false);
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = reflectionCube;
            probe.size = Vector3.one * 2000f;
            probe.importance = 100;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflectionCube;
            DynamicGI.UpdateEnvironment();
        }

        void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled)
                return;
            behaviour.enabled = false;
            disabledBehaviours.Add(behaviour);
        }

        void OnDestroy()
        {
            foreach (var b in disabledBehaviours)
                if (b != null)
                    b.enabled = true;
            savedEnvironment?.Restore();
            foreach (var mesh in generatedMeshes)
                Destroy(mesh);
            if (reflectionCube != null)
                Destroy(reflectionCube);
        }

        /// <summary>Ambient light that is brighter from above, solved so it evaluates to top/bottom at the poles.</summary>
        static SphericalHarmonicsL2 GradientAmbient(Color top, Color bottom)
        {
            var unit = new SphericalHarmonicsL2();
            unit.AddDirectionalLight(Vector3.up, Color.white, 1f);
            var poles = new Color[2];
            unit.Evaluate(new[] { Vector3.up, Vector3.down }, poles);
            float a = poles[0].r, b = poles[1].r;

            var sh = new SphericalHarmonicsL2();
            float k = Mathf.Abs(a - b) > 1e-5f ? 1f / (a - b) : 0f;
            var directional = (top - bottom) * k;
            sh.AddDirectionalLight(Vector3.up, directional, 1f);
            sh.AddAmbientLight(top - directional * a);
            return sh;
        }

        static Cubemap GradientCubemap(Color up, Color side, Color down)
        {
            const int size = 16;
            var cube = new Cubemap(size, TextureFormat.RGBA32, true) { name = "UnderwaterReflection" };
            var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            var pixels = new Color[size * size];
            foreach (var face in faces)
            {
                var c = face == CubemapFace.PositiveY ? up : face == CubemapFace.NegativeY ? down : side;
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = c;
                cube.SetPixels(pixels, face);
            }
            cube.Apply(true);
            return cube;
        }

        /// <summary>Lighting/fog state of the AR scene, restored if the world is destroyed.</summary>
        class EnvironmentState
        {
            bool fog;
            FogMode fogMode;
            Color fogColor;
            float fogDensity;
            AmbientMode ambientMode;
            SphericalHarmonicsL2 ambientProbe;
            DefaultReflectionMode reflectionMode;
            Texture customReflection;
            Light sun;

            public static EnvironmentState Capture() => new()
            {
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                ambientMode = RenderSettings.ambientMode,
                ambientProbe = RenderSettings.ambientProbe,
                reflectionMode = RenderSettings.defaultReflectionMode,
                customReflection = RenderSettings.customReflectionTexture,
                sun = RenderSettings.sun,
            };

            public void Restore()
            {
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflectionTexture = customReflection;
                RenderSettings.sun = sun;
            }
        }
    }
}
