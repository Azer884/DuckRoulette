using UnityEngine;

// The single source for how fast the player walks, how the ice slide feels and how long it locks
// the player out of normal movement. Movement reads the asset at Resources/PlayerMovementTuning, so
// the networked Player prefab and the offline tutorial player always share the same numbers. Without
// that asset the defaults below are used, so a missing asset never leaves the player unable to move.
[CreateAssetMenu(menuName = "DuckRoulette/Player Movement Tuning", fileName = "PlayerMovementTuning")]
public class PlayerMovementTuning : ScriptableObject
{
    public const string ResourcePath = "PlayerMovementTuning";

    [Header("Walking")]
    [Tooltip("Base walk speed, in m/s.")]
    [Min(0f)] public float walkSpeed = 4f;
    [Tooltip("Walk speed multiplier while sprinting. Footstep audio treats anything above 1 as running.")]
    [Min(1f)] public float sprintMultiplier = 2f;

    [Header("Ice (walking)")]
    [Tooltip("How quickly velocity follows input on ice, per second. Lower is slipperier.")]
    [Min(0.01f)] public float iceFriction = 0.7f;
    [Tooltip("Top speed on ice relative to normal ground.")]
    [Min(0f)] public float iceSpeedMultiplier = 1.2f;

    [Header("Ice slide (jumping on ice)")]
    [Tooltip("Slide launch speed as a multiple of the player's horizontal speed when the slide starts.")]
    [Min(0f)] public float slideSpeedMultiplier = 2.75f;
    [Tooltip("Launch speed, in m/s, for a slide started while (almost) standing still.")]
    [Min(0f)] public float slideStandingSpeed = 5f;
    [Tooltip("How quickly slide speed bleeds off, per second. Higher stops sooner.")]
    [Min(0.01f)] public float slideDecayRate = 2f;
    [Tooltip("Below this speed, in m/s, the slide's momentum counts as spent.")]
    [Min(0f)] public float slideStopSpeed = 1.5f;
    [Tooltip("Hard cap, in seconds, on the momentum part of a slide.")]
    [Min(0f)] public float slideMaxDuration = 2f;
    [Tooltip("Seconds the player stays down after the momentum is spent, before normal movement returns.")]
    [Min(0f)] public float slideRecoveryDuration = 0.25f;

    [Header("Look (see LookInput)")]
    [Tooltip("Degrees turned per mouse count at sensitivity 1, independent of framerate. 1/30 " +
        "matches how the old frame-dependent mouse look felt at 60 fps.")]
    [Min(0f)] public float mouseLookDegreesPerCount = 1f / 30f;
    [Tooltip("Degrees per second a fully deflected stick turns at controller sensitivity 1.")]
    [Min(0f)] public float gamepadLookDegreesPerSecond = 180f;

    [Header("Camera")]
    [Tooltip("Blend time, in seconds, from the slide camera back to first person when getting up.")]
    [Min(0f)] public float slideExitCameraBlendTime = 0.35f;
    [Tooltip("Longest the first-person body stays hidden waiting for the camera to settle (after a " +
        "slide or leaving a hiding spot) before it is shown anyway.")]
    [Min(0f)] public float bodyRevealTimeout = 3f;

    private static PlayerMovementTuning active;

    public static PlayerMovementTuning Active
    {
        get
        {
            if (active == null)
            {
                active = Resources.Load<PlayerMovementTuning>(ResourcePath);
                if (active == null)
                {
                    active = CreateInstance<PlayerMovementTuning>();
                    active.hideFlags = HideFlags.DontSave;
                }
            }

            return active;
        }
    }
}
