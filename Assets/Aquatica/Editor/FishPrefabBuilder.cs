using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquatica.Underwater.Fish;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Turns the per-fish glTF models (split out of the Fish Pack 30 - Coral Bay glTF, each centred, facing +Z and
    /// carrying Swim / Idle / Scuttle / Feed clips) into prefab variants with their own Animator Controller, and
    /// creates the default <see cref="FishCatalog"/> if there isn't one yet (an existing catalog is left alone, so
    /// tweaks made in the inspector survive a rebuild).
    /// Re-run Aquatica > Build Fish Prefabs after re-importing the models; existing prefabs and controllers are
    /// updated in place so scene references survive.
    /// </summary>
    public static class FishPrefabBuilder
    {
        const string ModelFolder = "Assets/Aquatica/Models/Fish";
        const string PrefabFolder = "Assets/Aquatica/Prefabs/Fish";
        const string ControllerFolder = "Assets/Aquatica/Prefabs/Fish/Controllers";
        const string CatalogPath = "Assets/Aquatica/Prefabs/Fish/FishCatalog.asset";
        const int BoundsSamples = 24;

        // Playback rate of each state at SwimSpeed 1
        static readonly (string name, float speed)[] States = { ("Swim", 1f), ("Idle", 0.85f), ("Scuttle", 2.2f), ("Feed", 1f) };

        // glTF node name -> prefab name
        static readonly Dictionary<string, string> Names = new()
        {
            { "Bannerfish1", "Bannerfish" },
            { "Bicolor_Angelfish", "BicolorAngelfish" },
            { "BlueTang", "BlueTang" },
            { "Caranx", "Caranx" },
            { "ClownfishBlack", "ClownfishBlack" },
            { "ClownfishOrange", "ClownfishOrange" },
            { "Clown_Triggerfish", "ClownTriggerfish" },
            { "ConvictTang", "ConvictTang" },
            { "Copperband", "CopperbandButterflyfish" },
            { "Discus01", "Discus_AlbinoMillenniumGold" },
            { "Discus02", "Discus_RedMap" },
            { "Discus03", "Discus_YellowMarlboro" },
            { "Discus04", "Discus_HeckelCross" },
            { "Discus05", "Discus_LeopardRedSpotted" },
            { "Discus06", "Discus_AlbinoSnakeskin" },
            { "Discus07", "Discus_BrilliantTurquoise" },
            { "Discus08", "Discus_CheckerboardPigeon" },
            { "Discus09", "Discus_RedPassion" },
            { "DoubleSaddle", "DoubleSaddleButterflyfish" },
            { "EmperorAngelfish", "EmperorAngelfish" },
            { "FlameAngelfish", "FlameAngelfish" },
            { "Fusilier", "Fusilier" },
            { "Juvenile_Angelfish", "JuvenileAngelfish" },
            { "LinedSurgeon", "LinedSurgeonfish" },
            { "PowBTang", "PowderBlueTang" },
            { "PurpleTang", "PurpleTang" },
            { "RoyalAngelfish", "RoyalAngelfish" },
            { "Threadfin", "ThreadfinButterflyfish" },
            { "YellowTang1", "YellowTang" },
            { "Yellow_Longnose", "YellowLongnoseButterflyfish" },
        };

        [MenuItem("Aquatica/Build Fish Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(ControllerFolder);
            AssetDatabase.Refresh();

            int built = 0;
            foreach (var modelPath in Directory.GetFiles(ModelFolder, "*.gltf").Select(p => p.Replace('\\', '/')))
            {
                string key = Path.GetFileNameWithoutExtension(modelPath);
                string fishName = Names.TryGetValue(key, out var n) ? n : key;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToDictionary(c => c.name);
                if (model == null || States.Any(s => !clips.ContainsKey(s.name)))
                {
                    Debug.LogWarning($"[FishPrefabBuilder] Skipping {modelPath}: missing model or one of the {string.Join("/", States.Select(s => s.name))} clips.");
                    continue;
                }

                var controller = BuildController($"{ControllerFolder}/{fishName}.controller", clips);
                var bounds = AnimatedBounds(model, clips.Values, out float length);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = fishName;
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var fishModel = instance.GetComponent<FishModel>();
                if (fishModel == null) fishModel = instance.AddComponent<FishModel>();
                var so = new SerializedObject(fishModel);
                so.FindProperty("length").floatValue = length;
                so.ApplyModifiedPropertiesWithoutUndo();

                foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.updateWhenOffscreen = false;
                    // Bounds cover every clip so culling never pops the fish mid-animation
                    if (bounds.TryGetValue(AnimationUtility.CalculateTransformPath(smr.transform, instance.transform), out var b))
                        smr.localBounds = b;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabFolder}/{fishName}.prefab");
                Object.DestroyImmediate(instance);
                built++;
            }

            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<FishCatalog>(CatalogPath) == null)
                CreateDefaultCatalog();
            Debug.Log($"[FishPrefabBuilder] Built {built} fish prefabs in {PrefabFolder}.");
        }

        static AnimatorController BuildController(string path, Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }
            else
            {
                // Reset in place so the asset GUID (and every prefab reference) stays the same
                foreach (var p in controller.parameters) controller.RemoveParameter(p);
                var sm = controller.layers[0].stateMachine;
                foreach (var s in sm.states) sm.RemoveState(s.state);
            }

            controller.AddParameter(new AnimatorControllerParameter
                { name = "SwimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter(new AnimatorControllerParameter
                { name = "CycleOffset", type = AnimatorControllerParameterType.Float, defaultFloat = 0f });

            // States are switched from code (FishModel.Play cross-fades), so there are no transitions
            var machine = controller.layers[0].stateMachine;
            for (int i = 0; i < States.Length; i++)
            {
                var state = machine.AddState(States[i].name, new Vector3(300f, 60f * i, 0f));
                state.motion = clips[States[i].name];
                state.speed = States[i].speed;
                state.speedParameter = "SwimSpeed";
                state.speedParameterActive = true;
                state.cycleOffsetParameter = "CycleOffset";
                state.cycleOffsetParameterActive = true;
                state.writeDefaultValues = true;
                if (i == 0)
                    machine.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// Bounds of each skinned mesh over every clip, in its root bone's space, and the model's nose-to-tail
        /// length (along +Z at the start of the Swim clip).
        /// </summary>
        static Dictionary<string, Bounds> AnimatedBounds(GameObject model, IEnumerable<AnimationClip> clips, out float length)
        {
            var temp = Object.Instantiate(model);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var result = new Dictionary<string, Bounds>();
            var smrs = temp.GetComponentsInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var clip in clips.OrderBy(c => c.name == "Swim" ? 0 : 1))
            {
                for (int i = 0; i <= BoundsSamples; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / BoundsSamples);
                    foreach (var smr in smrs)
                    {
                        var space = smr.rootBone != null ? smr.rootBone : smr.transform;
                        smr.BakeMesh(baked, true);
                        var toSpace = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                        var toWorld = smr.transform.localToWorldMatrix;
                        string key = AnimationUtility.CalculateTransformPath(smr.transform, temp.transform);
                        bool measure = clip.name == "Swim" && i == 0;
                        foreach (var v in baked.vertices)
                        {
                            var p = toSpace.MultiplyPoint3x4(v);
                            if (result.TryGetValue(key, out var b)) { b.Encapsulate(p); result[key] = b; }
                            else result[key] = new Bounds(p, Vector3.zero);
                            if (measure)
                            {
                                float z = toWorld.MultiplyPoint3x4(v).z;
                                minZ = Mathf.Min(minZ, z);
                                maxZ = Mathf.Max(maxZ, z);
                            }
                        }
                    }
                }
            }
            length = maxZ > minZ ? maxZ - minZ : 0.4f;
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(temp);
            return result;
        }

        static GameObject[] Prefabs(params string[] names) =>
            names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{n}.prefab")).Where(p => p != null).ToArray();

        static FishSpecies Species(string name, FishSocial social, Vector2Int group, float spread, Vector2 length, Vector2 height,
            float cruise, float flee, float feeding, float curiosity, float shelter, float weight, params string[] prefabs) => new()
        {
            name = name, prefabs = Prefabs(prefabs), social = social, groupSize = group, spread = spread, length = length,
            heightAboveFloor = height, cruiseSpeed = cruise, fleeSpeed = flee, feeding = feeding, curiosity = curiosity,
            shelter = shelter, spawnWeight = weight, skittishness = social == FishSocial.School ? 1.2f : 1f,
        };

        /// <summary>Realistic adult sizes and habits; tweak the generated asset in the inspector.</summary>
        static void CreateDefaultCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<FishCatalog>();
            var low = new Vector2(0.3f, 2.2f);
            catalog.species = new List<FishSpecies>
            {
                // Banks
                Species("Fusilier", FishSocial.School, new(8, 14), 2.2f, new(0.4f, 0.55f), new(3f, 10f), 0.9f, 3.2f, 0f, 0.1f, 0f, 1.2f, "Fusilier"),
                Species("Bluefin Trevally", FishSocial.School, new(4, 7), 2.5f, new(0.8f, 1.15f), new(2f, 9f), 1.1f, 3.8f, 0f, 0.6f, 0f, 0.6f, "Caranx"),
                Species("Blue Tang", FishSocial.School, new(4, 8), 1.6f, new(0.35f, 0.5f), new(0.8f, 5f), 0.6f, 2.6f, 0.6f, 0.2f, 0.4f, 1f, "BlueTang"),
                Species("Yellow Tang", FishSocial.School, new(5, 10), 1.5f, new(0.25f, 0.35f), new(0.6f, 4f), 0.55f, 2.5f, 0.7f, 0.15f, 0.4f, 1f, "YellowTang"),
                Species("Powder Blue Tang", FishSocial.School, new(3, 7), 1.4f, new(0.3f, 0.4f), new(0.6f, 4f), 0.6f, 2.6f, 0.7f, 0.15f, 0.4f, 0.8f, "PowderBlueTang"),
                Species("Purple Tang", FishSocial.School, new(3, 6), 1.4f, new(0.3f, 0.4f), new(0.6f, 4f), 0.6f, 2.6f, 0.7f, 0.15f, 0.4f, 0.7f, "PurpleTang"),
                Species("Convict Tang", FishSocial.School, new(6, 12), 1.8f, new(0.25f, 0.35f), new(0.4f, 3f), 0.55f, 2.5f, 0.8f, 0.1f, 0.4f, 1f, "ConvictTang"),
                Species("Lined Surgeonfish", FishSocial.School, new(3, 5), 1.6f, new(0.5f, 0.65f), new(0.5f, 4f), 0.65f, 2.8f, 0.7f, 0.2f, 0.3f, 0.6f, "LinedSurgeonfish"),
                Species("Bannerfish", FishSocial.School, new(4, 8), 1.6f, new(0.3f, 0.4f), new(1f, 6f), 0.5f, 2.4f, 0.3f, 0.4f, 0.3f, 0.8f, "Bannerfish"),
                Species("Discus", FishSocial.School, new(4, 8), 1.4f, new(0.25f, 0.35f), new(0.6f, 4f), 0.45f, 2.2f, 0.6f, 0.3f, 0.4f, 0.9f,
                    "Discus_AlbinoMillenniumGold", "Discus_AlbinoSnakeskin", "Discus_BrilliantTurquoise", "Discus_CheckerboardPigeon",
                    "Discus_HeckelCross", "Discus_LeopardRedSpotted", "Discus_RedMap", "Discus_RedPassion", "Discus_YellowMarlboro"),
                // Pairs
                Species("Copperband Butterflyfish", FishSocial.Pair, new(2, 2), 0.5f, new(0.25f, 0.35f), low, 0.45f, 2.3f, 0.9f, 0.45f, 0.6f, 0.7f, "CopperbandButterflyfish"),
                Species("Threadfin Butterflyfish", FishSocial.Pair, new(2, 2), 0.5f, new(0.3f, 0.4f), low, 0.45f, 2.3f, 0.9f, 0.45f, 0.6f, 0.7f, "ThreadfinButterflyfish"),
                Species("Double-saddle Butterflyfish", FishSocial.Pair, new(2, 2), 0.5f, new(0.25f, 0.35f), low, 0.45f, 2.3f, 0.9f, 0.45f, 0.6f, 0.6f, "DoubleSaddleButterflyfish"),
                Species("Yellow Longnose Butterflyfish", FishSocial.Pair, new(2, 2), 0.5f, new(0.3f, 0.35f), low, 0.45f, 2.3f, 0.9f, 0.45f, 0.6f, 0.6f, "YellowLongnoseButterflyfish"),
                Species("Clownfish", FishSocial.Pair, new(2, 2), 0.4f, new(0.15f, 0.2f), new(0.2f, 1.5f), 0.3f, 1.8f, 0.4f, 0.7f, 0.9f, 0.7f, "ClownfishOrange"),
                Species("Black Clownfish", FishSocial.Pair, new(2, 2), 0.4f, new(0.15f, 0.2f), new(0.2f, 1.5f), 0.3f, 1.8f, 0.4f, 0.7f, 0.9f, 0.5f, "ClownfishBlack"),
                // Solo
                Species("Emperor Angelfish", FishSocial.Solo, new(1, 1), 0f, new(0.55f, 0.7f), new(0.5f, 3f), 0.45f, 2.4f, 0.7f, 0.6f, 0.6f, 0.5f, "EmperorAngelfish"),
                Species("Juvenile Emperor Angelfish", FishSocial.Solo, new(1, 1), 0f, new(0.2f, 0.25f), new(0.2f, 1.8f), 0.35f, 2f, 0.7f, 0.4f, 0.9f, 0.4f, "JuvenileAngelfish"),
                Species("Regal Angelfish", FishSocial.Solo, new(1, 1), 0f, new(0.35f, 0.45f), new(0.4f, 2.5f), 0.4f, 2.3f, 0.7f, 0.5f, 0.7f, 0.5f, "RoyalAngelfish"),
                Species("Bicolor Angelfish", FishSocial.Solo, new(1, 1), 0f, new(0.2f, 0.25f), new(0.2f, 2f), 0.35f, 2f, 0.7f, 0.3f, 0.9f, 0.5f, "BicolorAngelfish"),
                Species("Flame Angelfish", FishSocial.Solo, new(1, 1), 0f, new(0.15f, 0.2f), new(0.2f, 1.5f), 0.3f, 1.8f, 0.7f, 0.25f, 0.95f, 0.5f, "FlameAngelfish"),
                Species("Clown Triggerfish", FishSocial.Solo, new(1, 1), 0f, new(0.55f, 0.7f), new(0.4f, 3f), 0.5f, 2.6f, 0.8f, 0.5f, 0.5f, 0.4f, "ClownTriggerfish"),
            };
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FishPrefabBuilder] Created the default fish catalog at {CatalogPath}.");
        }
    }
}
