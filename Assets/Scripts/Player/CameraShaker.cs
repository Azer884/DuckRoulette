using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// Drives the local player's CinemachineBasicMultiChannelPerlin from ShakeProfile assets.
// Self-installing: gameplay scripts call GetOrAdd(gameObject) instead of requiring this to be
// hand-wired on the prefab, so it can't be skipped by a missing inspector reference.
//
// Deliberately does not touch camHolder's transform - Movement.DoLooking() hard-sets
// camHolder.localRotation every Update, which would fight/overwrite any shake applied to the
// same transform. Cinemachine's Noise pipeline stage is summed onto the vcam's computed pose
// downstream of that, so it survives untouched.
public class CameraShaker : MonoBehaviour
{
    private class ActiveShake
    {
        public ShakeProfile profile;
        public float startTime;
    }

    [SerializeField] private CinemachineCamera virtualCamera;

    private CinemachineBasicMultiChannelPerlin noise;
    private NoiseSettings defaultNoiseShape;
    private readonly List<ActiveShake> activeShakes = new();

    public static CameraShaker GetOrAdd(GameObject target)
    {
        CameraShaker shaker = target.GetComponent<CameraShaker>();
        if (shaker == null)
        {
            shaker = target.AddComponent<CameraShaker>();
        }
        return shaker;
    }

    private void OnEnable()
    {
        ResolveCamera();
    }

    private void ResolveCamera()
    {
        if (virtualCamera == null)
        {
            // The player rig has several CinemachineCamera children (first-person, third-person,
            // sliding, ADS) but only the first-person one has a CinemachineBasicMultiChannelPerlin
            // extension. GetComponentInChildren<CinemachineCamera>() alone just returns whichever
            // one hierarchy order hands back first - if that's one of the noise-less ones, shake
            // is silently dead forever. Search for the one that actually has noise instead.
            // includeInactive: true - the first-person cam holder starts inactive for whichever
            // instance isn't the local owner yet at spawn time, so a strict active-only search
            // can permanently miss it and leave shake silently dead.
            foreach (CinemachineCamera candidate in GetComponentsInChildren<CinemachineCamera>(true))
            {
                if (candidate.GetComponent<CinemachineBasicMultiChannelPerlin>() != null)
                {
                    virtualCamera = candidate;
                    break;
                }
            }
        }

        if (virtualCamera == null)
        {
            Debug.LogWarning($"{nameof(CameraShaker)} on '{name}' found no CinemachineCamera with a CinemachineBasicMultiChannelPerlin extension.");
            return;
        }

        noise = virtualCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise == null)
        {
            Debug.LogWarning($"{nameof(CameraShaker)} on '{name}' has no CinemachineBasicMultiChannelPerlin on '{virtualCamera.name}'.");
            return;
        }

        defaultNoiseShape = noise.NoiseProfile;
        noise.AmplitudeGain = 0f;
        noise.FrequencyGain = 0f;
    }

    // Fire-and-forget burst. Overlapping shakes (e.g. a footstep landing mid-jump-shake) stack
    // additively instead of one cancelling the other, which is what the old coroutine-based
    // NoiseHandler did (StopCoroutine on every new trigger) and read as "the shake randomly not
    // playing" under normal, overlapping gameplay input.
    public void Shake(ShakeProfile profile)
    {
        if (profile == null)
        {
            return;
        }

        if (noise == null)
        {
            ResolveCamera();
            if (noise == null)
            {
                return;
            }
        }

        activeShakes.Add(new ActiveShake { profile = profile, startTime = Time.time });
    }

    private void Update()
    {
        if (noise == null)
        {
            return;
        }

        float amplitude = 0f;
        float frequency = 0f;
        NoiseSettings shape = defaultNoiseShape;

        for (int i = activeShakes.Count - 1; i >= 0; i--)
        {
            ActiveShake shake = activeShakes[i];
            float elapsed = Time.time - shake.startTime - shake.profile.delay;
            if (elapsed < 0f)
            {
                // Still waiting out the profile's delay - not started yet, but not expired either.
                continue;
            }

            float duration = Mathf.Max(shake.profile.duration, 0.0001f);
            float t = elapsed / duration;
            if (t >= 1f)
            {
                activeShakes.RemoveAt(i);
                continue;
            }

            float envelope = shake.profile.envelope.Evaluate(t);
            amplitude += shake.profile.amplitude * envelope;
            frequency = Mathf.Max(frequency, shake.profile.frequency);
            if (shake.profile.noiseShape != null)
            {
                shape = shake.profile.noiseShape;
            }
        }

        noise.NoiseProfile = shape;
        noise.AmplitudeGain = amplitude;
        noise.FrequencyGain = frequency;
    }
}
