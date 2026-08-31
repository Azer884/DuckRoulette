using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Self-installing (GetOrAdd, same pattern as CameraShaker) local-only screen punch for "you got
// hit" feedback. Owns its own runtime Volume + Vignette override instead of requiring one
// hand-authored per scene, so it works regardless of which scene/camera rig is active. Global
// Volume on the Default layer, matching every camera's default volumeLayerMask.
public class HitFlash : MonoBehaviour
{
    private Volume volume;
    private VolumeProfile profile;
    private Vignette vignette;

    private float baseIntensity;
    private Color peakColor;
    private float peakIntensity;
    private float duration;
    private float startTime = -1f;

    public static HitFlash GetOrAdd(GameObject target)
    {
        HitFlash flash = target.GetComponent<HitFlash>();
        if (flash == null)
        {
            flash = target.AddComponent<HitFlash>();
        }
        return flash;
    }

    private void EnsureVolume()
    {
        if (volume != null)
        {
            return;
        }

        GameObject volumeObject = new GameObject("HitFlashVolume");
        volumeObject.transform.SetParent(transform, false);

        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        vignette = profile.Add<Vignette>(true);
        baseIntensity = vignette.intensity.value;
        vignette.intensity.overrideState = true;
        vignette.color.overrideState = true;
        vignette.intensity.value = baseIntensity;

        volume.profile = profile;
    }

    /// <summary>Punches the vignette to `color`/`intensity` then decays back to its base value
    /// over `flashDuration` seconds. Safe to call repeatedly - a new call simply restarts the
    /// envelope from its current point.</summary>
    public void Flash(Color color, float intensity, float flashDuration)
    {
        EnsureVolume();

        peakColor = color;
        peakIntensity = intensity;
        duration = Mathf.Max(flashDuration, 0.01f);
        vignette.color.value = color;
        startTime = Time.time;
    }

    private void Update()
    {
        if (startTime < 0f || vignette == null)
        {
            return;
        }

        float t = (Time.time - startTime) / duration;
        if (t >= 1f)
        {
            vignette.intensity.value = baseIntensity;
            startTime = -1f;
            return;
        }

        // Sharp punch in, slower fade back out - matches ShakeProfile's usual attack/decay feel.
        const float attackFraction = 0.15f;
        float envelope = t < attackFraction
            ? t / attackFraction
            : 1f - (t - attackFraction) / (1f - attackFraction);

        vignette.intensity.value = Mathf.Lerp(baseIntensity, peakIntensity, envelope);
        vignette.color.value = peakColor;
    }
}
