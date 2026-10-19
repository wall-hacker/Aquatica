using System;
using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    public enum FishSocial
    {
        Solo,
        Pair,
        School,
    }

    /// <summary>How one species looks, groups and behaves. Lengths and speeds are in metres (per second).</summary>
    [Serializable]
    public class FishSpecies
    {
        public string name = "Fish";
        [Tooltip("Prefabs a group is drawn from (several = mixed colour morphs, e.g. discus).")]
        public GameObject[] prefabs = Array.Empty<GameObject>();
        [Tooltip("Relative chance of this species being picked when spawning.")]
        [Min(0f)] public float spawnWeight = 1f;

        [Header("Group")]
        public FishSocial social = FishSocial.Solo;
        [Tooltip("Members per group (used for schools).")]
        public Vector2Int groupSize = new(1, 1);
        [Tooltip("How far (m) school members spread around the group.")]
        public float spread = 1.5f;

        [Header("Body")]
        [Tooltip("Adult body length range (m), nose to tail.")]
        public Vector2 length = new(0.15f, 0.2f);
        [Tooltip("Preferred height above the seafloor (m).")]
        public Vector2 heightAboveFloor = new(0.5f, 4f);

        [Header("Speed (m/s)")]
        public float cruiseSpeed = 0.5f;
        public float fleeSpeed = 2.5f;

        [Header("Temperament (0-1)")]
        [Tooltip("Chance to spend time grazing / pecking at the seafloor.")]
        [Range(0f, 1f)] public float feeding = 0.5f;
        [Tooltip("Chance to come and look at the submarine window when it is still.")]
        [Range(0f, 1f)] public float curiosity = 0.3f;
        [Tooltip("Chance to hide next to a rock after being scared (solo fish and pairs).")]
        [Range(0f, 1f)] public float shelter = 0.5f;
        [Tooltip("Scales the distance at which the submarine scares this fish.")]
        [Min(0.1f)] public float skittishness = 1f;

        public bool IsValid => prefabs != null && Array.Exists(prefabs, p => p != null);

        public int RollGroupSize() => social switch
        {
            FishSocial.Solo => 1,
            FishSocial.Pair => 2,
            _ => UnityEngine.Random.Range(Mathf.Max(1, groupSize.x), Mathf.Max(groupSize.x, groupSize.y) + 1),
        };
    }
}
