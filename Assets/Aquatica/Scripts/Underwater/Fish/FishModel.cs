using UnityEngine;

namespace Aquatica.Underwater.Fish
{
    public enum FishAnimation
    {
        Swim,
        Idle,
        Scuttle,
        Feed,
    }

    /// <summary>
    /// The animated fish model (prefab root, facing +Z). Plays the Swim / Idle / Scuttle / Feed states of its
    /// Animator Controller and desynchronises the cycle so neighbours of the same species don't move in lockstep.
    /// Works on its own (a decorative fish just swims in place) or driven by a <see cref="FishAgent"/>.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FishModel : MonoBehaviour
    {
        static readonly int SwimSpeedId = Animator.StringToHash("SwimSpeed");
        static readonly int CycleOffsetId = Animator.StringToHash("CycleOffset");
        static readonly int[] StateIds =
        {
            Animator.StringToHash("Swim"), Animator.StringToHash("Idle"),
            Animator.StringToHash("Scuttle"), Animator.StringToHash("Feed"),
        };

        [Tooltip("Nose-to-tail length (m) of the model at scale 1 (measured by the prefab builder).")]
        [SerializeField] float length = 0.4f;
        [SerializeField] float crossFade = 0.35f;

        Animator animator;
        FishAnimation current = FishAnimation.Swim;

        public float Length => length;
        public FishAnimation Current => current;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.SetFloat(CycleOffsetId, Random.value);
            animator.SetFloat(SwimSpeedId, Random.Range(0.9f, 1.1f));
        }

        /// <summary>Cross-fades to a behaviour's animation and sets its playback rate (1 = as authored).</summary>
        public void Play(FishAnimation anim, float speed)
        {
            if (anim != current)
            {
                current = anim;
                animator.CrossFadeInFixedTime(StateIds[(int)anim], crossFade);
            }
            animator.SetFloat(SwimSpeedId, speed);
        }

        /// <summary>Back to the default state with a new random phase (fish are reused from a pool).</summary>
        public void ResetAnimation()
        {
            current = FishAnimation.Swim;
            animator.SetFloat(CycleOffsetId, Random.value);
            animator.Play(StateIds[0], 0, 0f);
        }
    }
}
