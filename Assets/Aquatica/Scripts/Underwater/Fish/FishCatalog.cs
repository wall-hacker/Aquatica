using System.Collections.Generic;
using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    /// <summary>All fish species that can populate the underwater world.</summary>
    [CreateAssetMenu(menuName = "Aquatica/Fish Catalog", fileName = "FishCatalog")]
    public class FishCatalog : ScriptableObject
    {
        public List<FishSpecies> species = new();
    }
}
