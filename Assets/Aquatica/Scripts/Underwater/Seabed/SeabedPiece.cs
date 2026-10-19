using System;
using UnityEngine;

namespace Aquatica.Underwater.Seabed
{
    public enum SeabedKind
    {
        Seaweed,  // seagrass and kelp: grow in fields of one type, sway and bend
        Shell,    // shells, clams, starfish: scattered
    }

    /// <summary>Ground a piece can lie or grow on.</summary>
    [Flags]
    public enum SeabedGround
    {
        None = 0,
        Sand = 1,
        Rock = 2,
    }

    /// <summary>
    /// One seabed prop (prefab root): a seaweed clump or a shell. Holds what <see cref="SeabedField"/> needs to
    /// scatter it. Seaweed meshes are 1 m tall (the shader uses object-space height as the bend weight), so their
    /// scale is their height.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SeabedPiece : MonoBehaviour
    {
        [SerializeField] SeabedKind kind = SeabedKind.Seaweed;
        [Tooltip("Seaweed of the same type grows together in its own fields (e.g. 'SeagrassGreen', 'KelpBrown').")]
        [SerializeField] string fieldType = "";
        [SerializeField] SeabedGround ground = SeabedGround.Sand;
        [Tooltip("Size range (m): height for seaweed, otherwise the largest dimension.")]
        [SerializeField] Vector2 size = new(1f, 2f);
        [Tooltip("Blocks the submarine like rock (only near the submarine).")]
        [SerializeField] bool blocksSubmarine;

        public SeabedKind Kind => kind;
        public string FieldType => fieldType;
        public SeabedGround Ground => ground;
        public bool Sways => kind == SeabedKind.Seaweed;
        public Vector2 Size => size;
        public bool BlocksSubmarine => blocksSubmarine;
    }
}
