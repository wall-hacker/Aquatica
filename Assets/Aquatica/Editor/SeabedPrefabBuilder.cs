using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquatica.Underwater.Seabed;
using UnityEditor;
using UnityEngine;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Builds the seabed props from Models/Seabed: seaweed in several types (each a colour of seagrass or kelp, in a
    /// few clump shapes, so fields of one type look varied but read as one species) and shells / starfish, with
    /// Aquatica/Seabed materials, a <see cref="SeabedCatalog"/> and the <see cref="SeabedField"/> on the underwater
    /// world. Re-run Aquatica > Build Seabed Prefabs after changing anything here; prefabs and materials are updated
    /// in place and catalog weights already tuned in the inspector are kept (new pieces are appended).
    /// </summary>
    public static class SeabedPrefabBuilder
    {
        const string ModelFolder = "Assets/Aquatica/Models/Seabed";
        const string MeshFolder = ModelFolder + "/Meshes";
        const string TextureFolder = ModelFolder + "/Textures";
        const string MaterialFolder = "Assets/Aquatica/Materials/Seabed";
        const string PrefabFolder = "Assets/Aquatica/Prefabs/Seabed";
        const string CatalogPath = PrefabFolder + "/SeabedCatalog.asset";
        const string WorldPrefabPath = "Assets/Aquatica/Prefabs/UnderwaterWorld.prefab";

        // Seaweed shapes and how much of a field each makes up (declared first: static fields initialise in order)
        static readonly string[] Seagrass = { "Seagrass_Clump_Dense", "Seagrass_Clump_Medium", "Seagrass_Clump_Sparse" };
        static readonly float[] SeagrassShare = { 0.45f, 0.35f, 0.2f };
        static readonly string[] Kelp = { "Kelp_A", "Kelp_B" };
        static readonly float[] KelpShare = { 0.6f, 0.4f };

        // Seaweed types: fields of one type grow together. (name, colour, shapes, height range m, how common)
        static readonly (string type, Color tint, string[] meshes, Vector2 height, float weight)[] SeaweedTypes =
        {
            ("SeagrassGreen", new Color(0.38f, 0.72f, 0.32f), Seagrass, new Vector2(1.2f, 2.4f), 3f),
            ("SeagrassEmerald", new Color(0.22f, 0.62f, 0.46f), Seagrass, new Vector2(1.2f, 2.4f), 2f),
            ("SeagrassOlive", new Color(0.6f, 0.58f, 0.26f), Seagrass, new Vector2(1.2f, 2.4f), 2f),
            ("SeagrassRed", new Color(0.74f, 0.28f, 0.3f), Seagrass, new Vector2(1.0f, 2.0f), 1.2f),
            ("KelpOlive", new Color(0.55f, 0.55f, 0.25f), Kelp, new Vector2(3.0f, 6.5f), 1.4f),
            ("KelpBrown", new Color(0.52f, 0.38f, 0.2f), Kelp, new Vector2(3.0f, 6.5f), 1.2f),
            ("KelpGolden", new Color(0.78f, 0.64f, 0.3f), Kelp, new Vector2(3.0f, 6.0f), 0.8f),
        };

        static readonly Dictionary<string, Color> ShellTints = new()
        {
            ["Ivory"] = new Color(1f, 0.97f, 0.9f),
            ["Sand"] = new Color(0.95f, 0.8f, 0.62f),
            ["Pink"] = new Color(1f, 0.7f, 0.6f),
            ["Blue"] = new Color(0.55f, 0.75f, 1f),
            ["Orange"] = new Color(1f, 0.55f, 0.25f),
            ["Red"] = new Color(0.92f, 0.3f, 0.25f),
            ["Purple"] = new Color(0.68f, 0.45f, 0.88f),
        };

        // Shells: (mesh, tint, size range m, ground, blocks the submarine, weight)
        static readonly (string mesh, string tint, Vector2 size, SeabedGround ground, bool blocks, float weight)[] Shells =
        {
            ("Clam_Giant_A", "Ivory", new Vector2(1.2f, 2.2f), SeabedGround.Sand, true, 0.5f),
            ("Clam_Giant_B", "Ivory", new Vector2(1.2f, 2.0f), SeabedGround.Sand, true, 0.5f),
            ("Clam_Giant_C", "Ivory", new Vector2(1.2f, 2.0f), SeabedGround.Sand, true, 0.5f),
            ("Clam_GiantOpen_A", "Blue", new Vector2(1.2f, 2.0f), SeabedGround.Sand, true, 0.4f),
            ("Clam_GiantOpen_B", "Blue", new Vector2(1.2f, 2.0f), SeabedGround.Sand, true, 0.4f),
            ("Starfish_Spiny", "Orange", new Vector2(0.5f, 0.9f), SeabedGround.Sand | SeabedGround.Rock, false, 1.2f),
            ("Starfish_Slender", "Red", new Vector2(0.5f, 0.9f), SeabedGround.Sand | SeabedGround.Rock, false, 1.2f),
            ("Starfish_Knobby", "Orange", new Vector2(0.5f, 0.9f), SeabedGround.Sand | SeabedGround.Rock, false, 1.2f),
            ("Starfish_Thin", "Purple", new Vector2(0.5f, 0.9f), SeabedGround.Sand | SeabedGround.Rock, false, 1.2f),
            ("Shell_Murex", "Sand", new Vector2(0.5f, 0.9f), SeabedGround.Sand, false, 1f),
            ("Shell_Auger", "Sand", new Vector2(0.5f, 1.0f), SeabedGround.Sand, false, 1f),
            ("Shell_Cone", "Pink", new Vector2(0.4f, 0.8f), SeabedGround.Sand, false, 1f),
            ("Shell_Scallop_A", "Pink", new Vector2(0.4f, 0.8f), SeabedGround.Sand, false, 1f),
            ("Shell_Scallop_B", "Ivory", new Vector2(0.4f, 0.8f), SeabedGround.Sand, false, 1f),
            ("Shell_Scallop_C", "Sand", new Vector2(0.4f, 0.8f), SeabedGround.Sand, false, 1f),
            ("SeaUrchin_Shell", "Ivory", new Vector2(0.3f, 0.6f), SeabedGround.Sand, false, 0.8f),
        };

        [MenuItem("Aquatica/Build Seabed Prefabs")]
        public static void Build()
        {
            ConfigureImporters();
            var shader = Shader.Find("Aquatica/Seabed");
            var pieces = new List<(SeabedPiece piece, float weight)>();

            foreach (var t in SeaweedTypes)
            {
                var material = SaveMaterial($"Seaweed_{t.type}", shader, "Seaweed_Albedo", "Seaweed_Normal", t.tint, seaweed: true);
                var shares = t.meshes == Kelp ? KelpShare : SeagrassShare;
                for (int i = 0; i < t.meshes.Length; i++)
                {
                    string shape = t.meshes[i].Replace("Seagrass_Clump_", "").Replace("Kelp_", "");
                    string family = t.type.StartsWith("Kelp") ? "Kelp" : "Seagrass";
                    string colour = t.type.Substring(family.Length);
                    var piece = SavePrefab($"{family}_{colour}_{shape}", "Seaweed", t.meshes[i], material,
                        SeabedKind.Seaweed, t.type, SeabedGround.Sand | SeabedGround.Rock, t.height, false);
                    pieces.Add((piece, t.weight * shares[i]));
                }
            }

            var shellMaterials = ShellTints.ToDictionary(kv => kv.Key,
                kv => SaveMaterial($"Shell_{kv.Key}", shader, "Shell_Atlas_Albedo", "Shell_Atlas_Normal", kv.Value, seaweed: false));
            foreach (var s in Shells)
            {
                string mesh = s.mesh == "SeaUrchin_Shell" ? "SeaUrchin_Shell_B" : s.mesh;
                var piece = SavePrefab(s.mesh, "Shells", mesh, shellMaterials[s.tint], SeabedKind.Shell, "", s.ground, s.size, s.blocks);
                pieces.Add((piece, s.weight));
            }

            AssetDatabase.SaveAssets();
            var catalog = UpdateCatalog(pieces);
            HookIntoWorld(catalog);
            Debug.Log($"[SeabedPrefabBuilder] Built {pieces.Count} seabed prefabs in {PrefabFolder}.");
        }

        static void ConfigureImporters()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in Directory.GetFiles(MeshFolder, "*.obj").Select(p => p.Replace('\\', '/')))
                {
                    if (AssetImporter.GetAtPath(path) is not ModelImporter mi)
                        continue;
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.importNormals = ModelImporterNormals.Import;
                    mi.importTangents = ModelImporterTangents.CalculateMikk;
                    mi.importAnimation = false;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.isReadable = false;
                    mi.globalScale = 1f;
                    mi.SaveAndReimport();
                }
                foreach (var path in Directory.GetFiles(TextureFolder, "*.png").Select(p => p.Replace('\\', '/')))
                {
                    if (AssetImporter.GetAtPath(path) is not TextureImporter ti)
                        continue;
                    bool normal = path.EndsWith("_Normal.png");
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.alphaIsTransparency = path.EndsWith("Seaweed_Albedo.png");
                    ti.mipmapEnabled = true;
                    ti.maxTextureSize = 2048;
                    ti.anisoLevel = 2;
                    ti.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static Material SaveMaterial(string name, Shader shader, string albedo, string normal, Color tint, bool seaweed)
        {
            Directory.CreateDirectory(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{albedo}.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{normal}.png"));
            mat.SetColor("_BaseColor", tint);
            SetKeyword(mat, "_NORMALMAP", "_UseNormalMap", true);
            SetKeyword(mat, "_ALPHATEST_ON", "_AlphaClip", seaweed);
            SetKeyword(mat, "_SWAY", "_UseSway", seaweed);
            mat.SetFloat("_Cull", seaweed ? 0f : 2f);
            mat.SetFloat("_Wrap", seaweed ? 0.5f : 0f);
            mat.SetFloat("_SwayAmount", 0.1f);
            mat.SetFloat("_FlutterAmount", 0.04f);
            mat.renderQueue = seaweed ? 2450 : 2000;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static SeabedPiece SavePrefab(string name, string folder, string meshName, Material material, SeabedKind kind,
            string fieldType, SeabedGround ground, Vector2 size, bool blocks)
        {
            var mesh = AssetDatabase.LoadAllAssetsAtPath($"{MeshFolder}/{meshName}.obj").OfType<Mesh>().FirstOrDefault();
            if (mesh == null)
            {
                Debug.LogWarning($"[SeabedPrefabBuilder] Missing mesh {meshName} for {name}.");
                return null;
            }
            string dir = $"{PrefabFolder}/{folder}";
            Directory.CreateDirectory(dir);

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var piece = go.AddComponent<SeabedPiece>();
            var so = new SerializedObject(piece);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("fieldType").stringValue = fieldType;
            so.FindProperty("ground").intValue = (int)ground;
            so.FindProperty("size").vector2Value = size;
            so.FindProperty("blocksSubmarine").boolValue = blocks;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A prefab dropped into a scene by hand comes out at a typical size
            float typical = (size.x + size.y) * 0.5f;
            float extent = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            go.transform.localScale = Vector3.one * (kind == SeabedKind.Seaweed ? typical : typical / Mathf.Max(0.01f, extent));

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<SeabedPiece>();
        }

        static SeabedCatalog UpdateCatalog(List<(SeabedPiece piece, float weight)> pieces)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SeabedCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SeabedCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.entries.RemoveAll(e => e.prefab == null);
            var known = new HashSet<SeabedPiece>(catalog.entries.Select(e => e.prefab));
            foreach (var (piece, weight) in pieces)
                if (piece != null && !known.Contains(piece))
                    catalog.entries.Add(new SeabedCatalogEntry { prefab = piece, weight = weight });
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static void HookIntoWorld(SeabedCatalog catalog)
        {
            var root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                // Drops components whose scripts were deleted (the old coral field)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                var field = root.GetComponent<SeabedField>();
                if (field == null)
                    field = root.AddComponent<SeabedField>();
                var so = new SerializedObject(field);
                so.FindProperty("catalog").objectReferenceValue = catalog;
                so.FindProperty("motor").objectReferenceValue = root.GetComponentInChildren<Navigation.SubmarineMotor>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetKeyword(Material mat, string keyword, string toggle, bool on)
        {
            mat.SetFloat(toggle, on ? 1f : 0f);
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }
    }
}
