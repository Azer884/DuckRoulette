using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Self-installing (GetOrAdd, same pattern as HitFlash/CameraShaker) local-only "you're dead"
// screen treatment: darkens and desaturates while isDead is true, ramping in/out instead of a
// quick punch-decay like HitFlash - death is a held state (ragdoll -> dramatic delay ->
// spectate), not a momentary hit.
public class DeathVignette : MonoBehaviour
{
    [SerializeField] private float rampInDuration = 1.5f;
    [SerializeField] private float rampOutDuration = 0.5f;
    [SerializeField] private float peakVignetteIntensity = 0.55f;
    [SerializeField] private Color vignetteColor = new(0.15f, 0f, 0f, 1f);
    [SerializeField] private float peakSaturation = -100f;
    [SerializeField] private float peakExposure = -0.6f;

    private Volume volume;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;

    private float targetWeight;
    private float currentWeight;

    public static DeathVignette GetOrAdd(GameObject target)
    {
        DeathVignette vignette = target.GetComponent<DeathVignette>();
        if (vignette == null)
        {
            vignette = target.AddComponent<DeathVignette>();
        }
        return vignette;
    }

    private void EnsureVolume()
    {
        if (volume != null)
        {
            return;
        }

        GameObject volumeObject = new GameObject("DeathVignetteVolume");
        volumeObject.transform.SetParent(transform, false);

        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        // Below HitFlash's 100 - a slap landing while already dead (e.g. a stray hit before the
        // ragdoll/spectate delay finishes) should still read as a punch on top of the dim/desat.
        volume.priority = 90f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        vignette = profile.Add<Vignette>(true);
        colorAdjustments = profile.Add<ColorAdjustments>(true);

        vignette.intensity.overrideState = true;
        vignette.color.overrideState = true;
        vignette.intensity.value = 0f;

        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.postExposure.overrideState = true;
        colorAdjustments.saturation.value = 0f;
        colorAdjustments.postExposure.value = 0f;

        volume.profile = profile;
    }

    /// <summary>Ramps the dim/desaturate treatment in when `isDead` turns true, and back out
    /// (to nothing) when it turns false again - call from Death.isDead.OnValueChanged.</summary>
    public void SetDead(bool isDead)
    {
        EnsureVolume();
        targetWeight = isDead ? 1f : 0f;
        enabled = true;
    }

    private void Update()
    {
        if (vignette == null)
        {
            return;
        }

        float duration = targetWeight > currentWeight ? rampInDuration : rampOutDuration;
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, Time.deltaTime / Mathf.Max(duration, 0.01f));

        vignette.color.value = vignetteColor;
        vignette.intensity.value = peakVignetteIntensity * currentWeight;
        colorAdjustments.saturation.value = peakSaturation * currentWeight;
        colorAdjustments.postExposure.value = peakExposure * currentWeight;
    }
}
