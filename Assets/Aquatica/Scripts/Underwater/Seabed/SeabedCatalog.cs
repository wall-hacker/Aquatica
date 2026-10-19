using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Underwater.Seabed
{
    [Serializable]
    public class SeabedCatalogEntry
    {
        public SeabedPiece prefab;
        [Tooltip("Relative chance among pieces of its kind. For seaweed, a field type's total weight is how often fields of that type appear.")]
        [Min(0f)] public float weight = 1f;
    }

    /// <summary>All seaweed and shell pieces <see cref="SeabedField"/> scatters across the seafloor.</summary>
    [CreateAssetMenu(menuName = "Aquatica/Seabed Catalog", fileName = "SeabedCatalog")]
    public class SeabedCatalog : ScriptableObject
    {
        public List<SeabedCatalogEntry> entries = new();
    }
}
