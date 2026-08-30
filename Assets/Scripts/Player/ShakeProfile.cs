using Unity.Cinemachine;
using UnityEngine;

// Data-only camera shake definition. Designers plug in new shakes by creating an asset here
// (right click > Create > DuckRoulette > Camera Shake Profile) and dragging it onto whichever
// gameplay script triggers it (Movement, FootStepScript, Shooting, Slap, ...) - no code changes.
[CreateAssetMenu(fileName = "NewShakeProfile", menuName = "DuckRoulette/Camera Shake Profile")]
public class ShakeProfile : ScriptableObject
{
    [Tooltip("Peak Perlin noise amplitude gain.")]
    public float amplitude = 0.3f;

    [Tooltip("Perlin noise frequency gain (higher = faster jitter).")]
    public float frequency = 2f;

    [Tooltip("How long the shake lasts, in seconds.")]
    public float duration = 0.15f;

    [Tooltip("Delay before the shake starts, in seconds - e.g. to line it up with a muzzle flash or animation hit frame instead of the trigger call itself.")]
    public float delay = 0f;

    [Tooltip("Amplitude multiplier over the shake's lifetime, x = normalized time (0-1), y = multiplier (0-1). Shape the attack/decay here.")]
    public AnimationCurve envelope = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("Optional: overrides the camera's default Cinemachine noise shape for this shake.")]
    public NoiseSettings noiseShape;
}
