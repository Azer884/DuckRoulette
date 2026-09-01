using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Self-installing (GetOrAdd, same pattern as HitFlash/DeathVignette/CameraShaker) local-only
// "you got knocked out" screen treatment: the world swims and smears while the player is down
// from a slap stun, then settles as they get back up. Like DeathVignette this is a held state
// rather than HitFlash's punch-decay, but unlike either of those it also ANIMATES while held -
// the whole point of reading as dizzy is that nothing sits still.
//
// Owns its own runtime Volume + overrides so no scene or prefab has to be wired for it; global
// Volume on the Default layer, matching every camera's default volumeLayerMask.
public class DizzinessEffect : MonoBehaviour
{
    [SerializeField] private float rampInDuration = 0.25f;
    [SerializeField] private float rampOutDuration = 0.8f;

    // Lens distortion does the heavy lifting: a barrel/pincushion breathe plus a slow drift of
    // the distortion centre, which makes the whole image sway around instead of just pulsing in
    // place. Kept well under the parameter's -1..1 range - past roughly 0.5 the edges of the
    // screen smear badly enough to be genuinely unpleasant rather than readable.
    [SerializeField] private float lensDistortionAmount = 0.32f;
    [SerializeField] private float lensDistortionFrequency = 0.85f;
    [SerializeField] private float lensSwayRadius = 0.055f;
    [SerializeField] private float lensSwayFrequency = 0.32f;

    // Chromatic aberration pulses on its own, slightly faster beat than the distortion so the two
    // never lock into a single obvious rhythm.
    [SerializeField] private float chromaticAberrationAmount = 0.45f;
    [SerializeField] private float chromaticAberrationFrequency = 1.25f;

    // A soft dark edge that breathes - narrows the usable view the way a real blackout does,
    // without the hard red punch HitFlash uses for damage.
    [SerializeField] private float vignetteBaseIntensity = 0.28f;
    [SerializeField] private float vignettePulseAmount = 0.14f;
    [SerializeField] private float vignettePulseFrequency = 1.05f;
    [SerializeField] private Color vignetteColor = new(0.04f, 0.02f, 0.07f, 1f);

    // A gentle colour wobble on top. Small numbers on purpose: hue shift reads as "wrong" very
    // fast, and this is meant to be a nuisance the player plays through, not a screen wipe.
    [SerializeField] private float hueShiftAmount = 7f;
    [SerializeField] private float saturationWobbleAmount = 14f;
    [SerializeField] private float colorWobbleFrequency = 0.65f;

    private Volume volume;
    private LensDistortion lensDistortion;
    private ChromaticAberration chromaticAberration;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;

    private float targetWeight;
    private float currentWeight;
    private float phase;

    public static DizzinessEffect GetOrAdd(GameObject target)
    {
        DizzinessEffect dizziness = target.GetComponent<DizzinessEffect>();
        if (dizziness == null)
        {
            dizziness = target.AddComponent<DizzinessEffect>();
        }
        return dizziness;
    }

    private void EnsureVolume()
    {
        if (volume != null)
        {
            return;
        }

        GameObject volumeObject = new GameObject("DizzinessVolume");
        volumeObject.transform.SetParent(transform, false);

        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        // Bottom of the local screen-effect stack: HitFlash 100 > DeathVignette 90 > this. All
        // three override Vignette, and the higher-priority one wins outright while it has weight -
        // getting slapped or dying should always be able to talk over an ongoing daze rather than
        // fight it, and this is the one of the three that is safe to lose.
        volume.priority = 85f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        lensDistortion = profile.Add<LensDistortion>(true);
        chromaticAberration = profile.Add<ChromaticAberration>(true);
        vignette = profile.Add<Vignette>(true);
        colorAdjustments = profile.Add<ColorAdjustments>(true);

        lensDistortion.intensity.overrideState = true;
        lensDistortion.center.overrideState = true;
        lensDistortion.scale.overrideState = true;
        lensDistortion.intensity.value = 0f;
        lensDistortion.center.value = new Vector2(0.5f, 0.5f);
        lensDistortion.scale.value = 1f;

        chromaticAberration.intensity.overrideState = true;
        chromaticAberration.intensity.value = 0f;

        vignette.intensity.overrideState = true;
        vignette.color.overrideState = true;
        vignette.intensity.value = 0f;
        vignette.color.value = vignetteColor;

        colorAdjustments.hueShift.overrideState = true;
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.hueShift.value = 0f;
        colorAdjustments.saturation.value = 0f;

        volume.profile = profile;
        volume.enabled = false;
    }

    /// <summary>Ramps the daze in when `isDizzy` turns true and back out when it turns false -
    /// call from the knockout/wake-up transitions in Ragdoll. Only ever call this for the player
    /// this client actually controls; the ragdoll state machine runs on every peer's copy of every
    /// player, so an ungated call would daze the local screen every time anyone got slapped
    /// down.</summary>
    public void SetDizzy(bool isDizzy)
    {
        EnsureVolume();

        // Restart the oscillator on each fresh knockout so the effect always opens from the same
        // neutral point. Left free-running, a knockout could start on the peak of the sway and
        // snap the view sideways the instant the ramp-in began.
        if (isDizzy && targetWeight <= 0f)
        {
            phase = 0f;
        }

        targetWeight = isDizzy ? 1f : 0f;
        enabled = true;
    }

    private void Update()
    {
        if (volume == null)
        {
            return;
        }

        float duration = targetWeight > currentWeight ? rampInDuration : rampOutDuration;
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, Time.deltaTime / Mathf.Max(duration, 0.01f));

        if (currentWeight <= 0f)
        {
            // Fully settled: park the volume so it stops contributing to the stack at all (it
            // overrides Vignette, which DeathVignette and HitFlash also use) and stop ticking
            // until the next knockout wakes us back up.
            volume.enabled = false;
            enabled = false;
            return;
        }

        volume.enabled = true;
        phase += Time.deltaTime;

        // The master ramp rides on volume.weight rather than being multiplied into every value by
        // hand the way DeathVignette does it. Several of the parameters here (the distortion
        // centre, the scale) have a non-zero neutral value, so scaling them toward zero would ramp
        // them the wrong way; blending the whole volume in lets URP interpolate each one from its
        // own neutral instead.
        volume.weight = currentWeight;

        float distortionWave = Mathf.Sin(phase * lensDistortionFrequency * Mathf.PI * 2f);
        lensDistortion.intensity.value = lensDistortionAmount * distortionWave;
        // Slight counter-zoom against the barrel breathe, so the edges of the screen do not pull
        // away from the frame while the distortion swings negative.
        lensDistortion.scale.value = 1f + 0.05f * Mathf.Abs(distortionWave);

        // Centre drifts around a small circle - x and y off the same phase a quarter turn apart.
        float swayAngle = phase * lensSwayFrequency * Mathf.PI * 2f;
        lensDistortion.center.value = new Vector2(
            0.5f + lensSwayRadius * Mathf.Cos(swayAngle),
            0.5f + lensSwayRadius * Mathf.Sin(swayAngle)
        );

        // Half-wave rectified so the fringing swells and releases once per cycle rather than
        // crossing zero twice; the parameter clamps negatives away anyway.
        chromaticAberration.intensity.value =
            chromaticAberrationAmount * Mathf.Abs(Mathf.Sin(phase * chromaticAberrationFrequency * Mathf.PI));

        vignette.color.value = vignetteColor;
        vignette.intensity.value =
            vignetteBaseIntensity + vignettePulseAmount * Mathf.Sin(phase * vignettePulseFrequency * Mathf.PI * 2f);

        float colorPhase = phase * colorWobbleFrequency * Mathf.PI * 2f;
        colorAdjustments.hueShift.value = hueShiftAmount * Mathf.Sin(colorPhase);
        // A quarter cycle behind the hue so the two never peak together and read as one flat shift.
        colorAdjustments.saturation.value =
            saturationWobbleAmount * Mathf.Sin(colorPhase + Mathf.PI * 0.5f);
    }
}
