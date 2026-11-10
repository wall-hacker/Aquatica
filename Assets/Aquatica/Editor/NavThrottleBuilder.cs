using System.Collections.Generic;
using System.IO;
using Aquatica.Cockpit;
using Aquatica.Navigation;
using UnityEditor;
using UnityEngine;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Builds the floating navigation throttle: procedural meshes (base housing, quadrant plate, guide cheeks,
    /// arm, rubber boot, knob, status light, bolts), painted textures and Aquatica/ViewmodelLit materials, saved
    /// as a prefab. Re-run Aquatica > Build Nav Throttle after changing anything here.
    /// </summary>
    public static class NavThrottleBuilder
    {
        const string MeshPath = "Assets/Aquatica/Meshes/NavThrottle.asset";
        const string TextureFolder = "Assets/Aquatica/Textures/NavThrottle";
        const string MaterialFolder = "Assets/Aquatica/Materials/NavThrottle";
        const string PrefabPath = "Assets/Aquatica/Prefabs/Cockpit/NavThrottle.prefab";
        const string HazardTexturePath = "Assets/Aquatica/Textures/Cockpit/HazardStripes.png";

        // Dimensions (metres)
        const float BaseHalfX = 0.05f, BaseHalfZ = 0.07f, BaseHeight = 0.04f;
        const float PlateY = BaseHeight;
        const float PivotY = PlateY + 0.004f;
        const float ArmLength = 0.095f;

        [MenuItem("Aquatica/Build Nav Throttle")]
        public static void Build()
        {
            Directory.CreateDirectory(TextureFolder);
            Directory.CreateDirectory(MaterialFolder);

            var bodyTex = SaveTexture(PaintBody(), "NavThrottle_Body.png");
            var plateTex = SaveTexture(PaintPlate(), "NavThrottle_Plate.png");
            var hazardTex = AssetDatabase.LoadAssetAtPath<Texture2D>(HazardTexturePath);

            var body = SaveMaterial("NavThrottle_Body", new Color(1f, 1f, 1f), bodyTex, 0.35f, 0.5f, true);
            var plate = SaveMaterial("NavThrottle_Plate", Color.white, plateTex, 0.75f, 0.6f, true);
            var steel = SaveMaterial("NavThrottle_Steel", new Color(0.74f, 0.76f, 0.79f), null, 0.95f, 0.72f);
            var rubber = SaveMaterial("NavThrottle_Rubber", new Color(0.045f, 0.045f, 0.05f), null, 0f, 0.28f);
            var knob = SaveMaterial("NavThrottle_Knob", new Color(0.72f, 0.07f, 0.04f), null, 0f, 0.86f);
            var brass = SaveMaterial("NavThrottle_Brass", new Color(0.86f, 0.66f, 0.3f), null, 1f, 0.62f);
            var hazard = SaveMaterial("NavThrottle_Hazard", Color.white, hazardTex, 0.1f, 0.45f);
            var led = SaveMaterial("NavThrottle_Led", new Color(0.08f, 0.08f, 0.08f), null, 0f, 0.92f);

            var meshes = new List<Mesh>();
            var names = new Dictionary<string, int>();
            Mesh Keep(Mesh m, string meshName)
            {
                // Sub-assets need unique names for a stable import
                names[meshName] = names.TryGetValue(meshName, out int n) ? n + 1 : 0;
                m.name = names[meshName] == 0 ? meshName : $"{meshName}{names[meshName]}";
                m.RecalculateBounds();
                meshes.Add(m);
                return m;
            }

            var root = new GameObject("NavThrottle");
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);

            // Base: tapered housing with a chamfered top edge; the back face (towards the player) carries the label
            Part(model, "Housing", Keep(Loft(new[]
            {
                new Section(0.000f, BaseHalfX, BaseHalfZ),
                new Section(0.032f, BaseHalfX - 0.004f, BaseHalfZ - 0.004f),
                new Section(BaseHeight, BaseHalfX - 0.010f, BaseHalfZ - 0.010f),
            }, true, capBottom: true), "NavThrottle_Housing"), body);

            Part(model, "HazardSkirt", Keep(Loft(new[]
            {
                new Section(-0.001f, BaseHalfX + 0.003f, BaseHalfZ + 0.003f),
                new Section(0.009f, BaseHalfX + 0.0026f, BaseHalfZ + 0.0026f),
            }, false, 6f), "NavThrottle_HazardSkirt"), hazard);

            // Quadrant plate on top
            Part(model, "Plate", Keep(Box(new Vector3(0f, PlateY + 0.002f, 0f), new Vector3(0.07f, 0.004f, 0.11f), true), "NavThrottle_Plate"), plate);
            Part(model, "Slot", Keep(Box(new Vector3(0f, PlateY + 0.0042f, 0.002f), new Vector3(0.011f, 0.0006f, 0.085f)), "NavThrottle_Slot"), rubber);

            // Guide cheeks either side of the slot
            foreach (float x in new[] { -0.012f, 0.012f })
                Part(model, "Cheek", Keep(HalfDisc(new Vector3(x, PivotY, 0f), 0.034f, 0.003f, 20), "NavThrottle_Cheek"), steel);

            // Bolts in the plate corners
            foreach (var c in new[] { new Vector2(-0.028f, -0.046f), new Vector2(0.028f, -0.046f), new Vector2(-0.028f, 0.046f), new Vector2(0.028f, 0.046f) })
                Part(model, "Bolt", Keep(Cylinder(new Vector3(c.x, PlateY + 0.004f, c.y), 0.0035f, 0.0022f, 10), "NavThrottle_Bolt"), brass);

            // Status light on the plate, near the player
            var light = Part(model, "StatusLight", Keep(Dome(new Vector3(-0.024f, PlateY + 0.004f, -0.03f), 0.0055f, 12, 6), "NavThrottle_Led"), led);
            Part(model, "StatusRing", Keep(Cylinder(new Vector3(-0.024f, PlateY + 0.004f, -0.03f), 0.0075f, 0.0016f, 16), "NavThrottle_LedRing"), rubber);

            // Arm: rotates around the pivot axis (local X)
            var arm = new GameObject("Arm").transform;
            arm.SetParent(model, false);
            arm.localPosition = new Vector3(0f, PivotY, 0f);
            Part(arm, "Hub", Keep(Cylinder(Vector3.zero, 0.0075f, 0.03f, 16, Vector3.right), "NavThrottle_Hub"), steel);
            Part(arm, "Boot", Keep(Frustum(Vector3.zero, 0.0085f, 0.005f, 0.018f, 14), "NavThrottle_Boot"), rubber);
            Part(arm, "Rod", Keep(Cylinder(new Vector3(0f, ArmLength * 0.5f, 0f), 0.0042f, ArmLength, 12), "NavThrottle_Rod"), steel);
            Part(arm, "Collar", Keep(Cylinder(new Vector3(0f, ArmLength - 0.004f, 0f), 0.0072f, 0.005f, 16), "NavThrottle_Collar"), brass);
            Part(arm, "Knob", Keep(Sphere(new Vector3(0f, ArmLength + 0.012f, 0f), 0.0165f, 20, 14), "NavThrottle_Knob"), knob);

            // Generous trigger for touch: covers the whole lever and some margin
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.065f, 0f);
            box.size = new Vector3(0.14f, 0.16f, 0.18f);

            var lever = root.AddComponent<ThrottleLever>();
            var so = new SerializedObject(lever);
            so.FindProperty("arm").objectReferenceValue = arm;
            so.FindProperty("statusLight").objectReferenceValue = light.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveMeshes(meshes);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Aquatica] Built {PrefabPath}");
        }

        static GameObject Part(Transform parent, string partName, Mesh mesh, Material material)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static void SaveMeshes(List<Mesh> meshes)
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(MeshPath);
            if (existing.Length > 0)
                AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(meshes[0], MeshPath);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
            AssetDatabase.SaveAssets();
        }

        static Material SaveMaterial(string materialName, Color color, Texture2D texture, float metallic, float smoothness, bool smoothnessFromMap = false)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Aquatica/ViewmodelLit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Aquatica/ViewmodelLit");
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessFromMap", smoothnessFromMap ? 1f : 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D SaveTexture(Texture2D texture, string fileName)
        {
            string path = $"{TextureFolder}/{fileName}";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- Textures

        /// <summary>
        /// Worn teal paint. Top half: generic panel (sides, top). Bottom half: the face towards the player with a
        /// stencilled THRUST label. Alpha holds smoothness (bare metal at worn edges is shinier).
        /// </summary>
        static Texture2D PaintBody()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var paint = new Color(0.30f, 0.40f, 0.42f);
            var metal = new Color(0.62f, 0.63f, 0.62f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                bool lower = y < size / 2;
                float v = lower ? (y + 0.5f) / (size / 2f) : (y - size / 2 + 0.5f) / (size / 2f);

                float mottle = Fbm(x * 0.045f, y * 0.045f, 4) * 0.12f - 0.06f;
                float grime = Mathf.SmoothStep(0.55f, 0.8f, Fbm(x * 0.02f + 31f, y * 0.06f + 7f, 3)) * 0.18f;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float chips = Fbm(x * 0.11f + 5f, y * 0.11f + 9f, 3);
                float wear = Mathf.Clamp01((0.06f - edge) / 0.06f) * Mathf.SmoothStep(0.35f, 0.65f, chips)
                           + Mathf.SmoothStep(0.78f, 0.84f, chips) * 0.8f;
                wear = Mathf.Clamp01(wear);
                float scratch = Mathf.SmoothStep(0.985f, 1f, Mathf.Abs(Mathf.Sin(x * 0.9f + y * 0.35f + Fbm(x * 0.05f, y * 0.05f, 2) * 20f))) * 0.25f;

                var c = paint * (1f + mottle) * (1f - grime);
                c = Color.Lerp(c, metal, Mathf.Max(wear, scratch));
                c.a = Mathf.Lerp(0.55f, 0.9f, Mathf.Max(wear, scratch)) * (1f - grime);
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);

            // Stencil label on the back face (lower half). Text rows run top-down in font space.
            StencilText(tex, "THRUST", size / 2, size / 4 + 4, 4, new Color(0.93f, 0.88f, 0.72f), 0.88f);
            // Hazard tick marks either side of the label
            for (int i = 0; i < 4; i++)
            {
                FillRect(tex, 14 + i * 9, 40, 5, 48, new Color(0.9f, 0.68f, 0.1f), 0.85f, 0.5f);
                FillRect(tex, size - 19 - i * 9, 40, 5, 48, new Color(0.9f, 0.68f, 0.1f), 0.85f, 0.5f);
            }
            tex.Apply(true);
            return tex;
        }

        /// <summary>Dark steel quadrant plate: AHEAD towards the front (+V), STOP towards the player, ticks between.</summary>
        static Texture2D PaintPlate()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float brushed = Fbm(x * 0.02f, y * 0.6f, 3) * 0.08f;
                float smudge = Fbm(x * 0.03f + 11f, y * 0.03f, 3) * 0.1f;
                var c = new Color(0.2f, 0.21f, 0.22f) * (0.9f + brushed - smudge);
                c.a = 0.7f - smudge;
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);

            var ink = new Color(0.92f, 0.9f, 0.82f);
            var red = new Color(0.85f, 0.15f, 0.08f);
            var green = new Color(0.25f, 0.8f, 0.35f);
            StencilText(tex, "AHEAD", size / 2, size - 36, 4, green, 0.9f);
            StencilText(tex, "STOP", size / 2, 34, 4, red, 0.9f);
            // Tick marks along the slot, either side
            for (int i = 0; i <= 8; i++)
            {
                int y = 68 + i * 15;
                int length = i % 4 == 0 ? 34 : 20;
                FillRect(tex, size / 2 - 30 - length, y, length, 3, ink, 0.6f, 1f);
                FillRect(tex, size / 2 + 30, y, length, 3, ink, 0.6f, 1f);
            }
            // Rim
            FillRect(tex, 4, 4, size - 8, 3, ink, 0.4f, 1f);
            FillRect(tex, 4, size - 7, size - 8, 3, ink, 0.4f, 1f);
            FillRect(tex, 4, 4, 3, size - 8, ink, 0.4f, 1f);
            FillRect(tex, size - 7, 4, 3, size - 8, ink, 0.4f, 1f);
            tex.Apply(true);
            return tex;
        }

        static void StencilText(Texture2D tex, string text, int centerX, int centerY, int scale, Color color, float opacity)
        {
            int width = PixelFont.Measure(text, scale);
            int x0 = centerX - width / 2;
            int top = centerY + PixelFont.GlyphHeight * scale / 2; // texture y grows upwards
            for (int i = 0; i < text.Length; i++)
            for (int column = 0; column < PixelFont.GlyphWidth; column++)
            for (int row = 0; row < PixelFont.GlyphHeight; row++)
            {
                if (!PixelFont.IsSet(text[i], column, row))
                    continue;
                int px = x0 + (i * PixelFont.Advance + column) * scale;
                int py = top - (row + 1) * scale;
                // Stencil bridges: leave a thin gap in each cell so it reads as sprayed paint
                FillRect(tex, px, py, scale - 1, scale - 1, color, opacity, 0.5f);
            }
        }

        static void FillRect(Texture2D tex, int x, int y, int w, int h, Color color, float opacity, float smoothness)
        {
            for (int py = Mathf.Max(0, y); py < Mathf.Min(tex.height, y + h); py++)
            for (int px = Mathf.Max(0, x); px < Mathf.Min(tex.width, x + w); px++)
            {
                var under = tex.GetPixel(px, py);
                // Worn paint: let the surface show through in places
                float k = opacity * Mathf.SmoothStep(0.2f, 0.5f, Fbm(px * 0.2f + 3f, py * 0.2f, 2) + 0.15f);
                var c = Color.Lerp(under, color, k);
                c.a = Mathf.Lerp(under.a, smoothness, k);
                tex.SetPixel(px, py, c);
            }
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, y) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f + 9.7f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        // ---------------------------------------------------------------- Meshes

        readonly struct Section
        {
            public readonly float Y, HalfX, HalfZ;
            public Section(float y, float halfX, float halfZ) { Y = y; HalfX = halfX; HalfZ = halfZ; }
        }

        /// <summary>
        /// Flat-shaded loft through rectangular sections (bottom to top), optionally capped on top. Side faces use the
        /// upper half of the body texture; the -Z face (towards the player) the lower half with the label.
        /// The lever floats in view, so anything seen from below needs a bottom cap (the shader culls back faces).
        /// </summary>
        static Mesh Loft(Section[] sections, bool capTop, float uvRepeat = 1f, bool capBottom = false)
        {
            var b = new MeshBuilder();
            float bottom = sections[0].Y, top = sections[^1].Y;
            for (int s = 0; s < sections.Length - 1; s++)
            {
                var lo = sections[s];
                var hi = sections[s + 1];
                float v0 = Mathf.InverseLerp(bottom, top, lo.Y), v1 = Mathf.InverseLerp(bottom, top, hi.Y);
                // Corners counter-clockwise seen from above: (-x,-z) (+x,-z) (+x,+z) (-x,+z)
                Vector3 L(int i, Section sec) => new(i == 0 || i == 3 ? -sec.HalfX : sec.HalfX, sec.Y, i < 2 ? -sec.HalfZ : sec.HalfZ);
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    bool label = i == 0; // the -Z face
                    var uvLo = label ? new Rect(0f, 0f, 1f, 0.5f) : new Rect(0f, 0.5f, 1f, 0.5f);
                    if (uvRepeat != 1f)
                        uvLo = new Rect(0f, 0f, uvRepeat, 1f);
                    // Outward-facing quad: lo i, hi i, hi j, lo j
                    b.Quad(L(i, lo), L(i, hi), L(j, hi), L(j, lo),
                        new Vector2(uvLo.xMin, uvLo.yMin + v0 * uvLo.height), new Vector2(uvLo.xMin, uvLo.yMin + v1 * uvLo.height),
                        new Vector2(uvLo.xMax, uvLo.yMin + v1 * uvLo.height), new Vector2(uvLo.xMax, uvLo.yMin + v0 * uvLo.height));
                }
            }
            if (capTop)
            {
                var t = sections[^1];
                b.Quad(new Vector3(-t.HalfX, t.Y, -t.HalfZ), new Vector3(-t.HalfX, t.Y, t.HalfZ), new Vector3(t.HalfX, t.Y, t.HalfZ), new Vector3(t.HalfX, t.Y, -t.HalfZ),
                    new Vector2(0.05f, 0.55f), new Vector2(0.05f, 0.95f), new Vector2(0.95f, 0.95f), new Vector2(0.95f, 0.55f));
            }
            if (capBottom)
            {
                var o = sections[0];
                b.Quad(new Vector3(-o.HalfX, o.Y, -o.HalfZ), new Vector3(o.HalfX, o.Y, -o.HalfZ), new Vector3(o.HalfX, o.Y, o.HalfZ), new Vector3(-o.HalfX, o.Y, o.HalfZ),
                    new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.55f), new Vector2(0.95f, 0.95f), new Vector2(0.05f, 0.95f));
            }
            return b.ToMesh();
        }

        /// <summary>Axis-aligned box. With topUVs, the top face maps the whole texture (+V towards +Z).</summary>
        static Mesh Box(Vector3 center, Vector3 size, bool topUVs = false)
        {
            var b = new MeshBuilder();
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + new Vector3(x * h.x, y * h.y, z * h.z);
            var u00 = new Vector2(0, 0);
            var u10 = new Vector2(1, 0);
            var u11 = new Vector2(1, 1);
            var u01 = new Vector2(0, 1);
            var side = topUVs ? new Vector2(0.02f, 0.02f) : u00;
            // top (+Y)
            b.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), u00, u01, u11, u10);
            // bottom
            b.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), side, side, side, side);
            // -Z, +X, +Z, -X
            b.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), side, side, side, side);
            b.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), side, side, side, side);
            b.Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), side, side, side, side);
            b.Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), side, side, side, side);
            return b.ToMesh();
        }

        static Mesh Cylinder(Vector3 center, float radius, float height, int segments, Vector3? axis = null) =>
            Frustum(center - Orient(axis) * Vector3.up * (height * 0.5f), radius, radius, height, segments, axis);

        /// <summary>Capped cone section standing on <paramref name="baseCenter"/> along the axis (default up).</summary>
        static Mesh Frustum(Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int segments, Vector3? axis = null)
        {
            var b = new MeshBuilder();
            var rot = Orient(axis);
            Vector3 P(float r, float a, float y) => baseCenter + rot * new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            float slope = (radiusBottom - radiusTop) / height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var n0 = rot * new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized;
                var n1 = rot * new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized;
                float u0 = (float)i / segments, u1 = (float)(i + 1) / segments;
                b.SmoothQuad(P(radiusBottom, a0, 0f), P(radiusTop, a0, height), P(radiusTop, a1, height), P(radiusBottom, a1, 0f),
                    n0, n0, n1, n1, new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0));
                // caps
                b.Triangle(baseCenter + rot * new Vector3(0, height, 0), P(radiusTop, a1, height), P(radiusTop, a0, height), new Vector2(0.5f, 0.5f));
                b.Triangle(baseCenter, P(radiusBottom, a0, 0f), P(radiusBottom, a1, 0f), new Vector2(0.5f, 0.5f));
            }
            return b.ToMesh();
        }

        static Mesh Sphere(Vector3 center, float radius, int segments, int rings) => SphereBand(center, radius, segments, rings, 0f);

        static Mesh Dome(Vector3 center, float radius, int segments, int rings) => SphereBand(center, radius, segments, rings, 0.5f);

        static Mesh SphereBand(Vector3 center, float radius, int segments, int rings, float startLatitude01)
        {
            var b = new MeshBuilder();
            for (int r = 0; r < rings; r++)
            {
                float t0 = Mathf.Lerp(startLatitude01, 1f, (float)r / rings), t1 = Mathf.Lerp(startLatitude01, 1f, (float)(r + 1) / rings);
                float lat0 = (t0 - 0.5f) * Mathf.PI, lat1 = (t1 - 0.5f) * Mathf.PI;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
                    Vector3 N(float lat, float lon) => new(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    var n00 = N(lat0, a0);
                    var n01 = N(lat1, a0);
                    var n11 = N(lat1, a1);
                    var n10 = N(lat0, a1);
                    b.SmoothQuad(center + n00 * radius, center + n01 * radius, center + n11 * radius, center + n10 * radius, n00, n01, n11, n10,
                        new Vector2((float)s / segments, t0), new Vector2((float)s / segments, t1), new Vector2((float)(s + 1) / segments, t1), new Vector2((float)(s + 1) / segments, t0));
                }
            }
            return b.ToMesh();
        }

        /// <summary>Upper half-disc plate in the YZ plane (thickness along X), centred on the pivot.</summary>
        static Mesh HalfDisc(Vector3 center, float radius, float thickness, int segments)
        {
            var b = new MeshBuilder();
            float hx = thickness * 0.5f;
            Vector3 P(float a, float x) => center + new Vector3(x, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius);
            var uv = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI / segments, a1 = (i + 1) * Mathf.PI / segments;
                b.Triangle(center + new Vector3(hx, 0f, 0f), P(a1, hx), P(a0, hx), uv);
                b.Triangle(center + new Vector3(-hx, 0f, 0f), P(a0, -hx), P(a1, -hx), uv);
                b.Quad(P(a0, -hx), P(a0, hx), P(a1, hx), P(a1, -hx), uv, uv, uv, uv);
            }
            return b.ToMesh();
        }

        static Quaternion Orient(Vector3? axis) => axis.HasValue ? Quaternion.FromToRotation(Vector3.up, axis.Value) : Quaternion.identity;

        class MeshBuilder
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector3> normals = new();
            readonly List<Vector2> uvs = new();
            readonly List<int> triangles = new();

            /// <summary>Flat quad; corners in clockwise order seen from the outside (Unity front faces).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                SmoothQuad(a, b, c, d, n, n, n, n, ua, ub, uc, ud);
            }

            public void SmoothQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { na, nb, nc, nd });
                uvs.AddRange(new[] { ua, ub, uc, ud });
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
            {
                int i = vertices.Count;
                var n = Vector3.Cross(b - a, c - a).normalized;
                vertices.AddRange(new[] { a, b, c });
                normals.AddRange(new[] { n, n, n });
                uvs.AddRange(new[] { uv, uv, uv });
                triangles.AddRange(new[] { i, i + 1, i + 2 });
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                return mesh;
            }
        }
    }
}
