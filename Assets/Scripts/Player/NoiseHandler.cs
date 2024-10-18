using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class NoiseHandler : MonoBehaviour
{
    private Shooting shootingScript;
    private Slap slapScript;

    public CinemachineCamera virtualCamera; // Assign your Cinemachine camera in the Inspector
    private CinemachineBasicMultiChannelPerlin noise;
    private float defaultAmplitude;
    private float defaultFrequency;
    private NoiseSettings defaultNoiseSettings;
    public NoiseSettings shootingNoise;

    private void OnEnable() 
    {
        noise = virtualCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();

        // Store the default noise values
        defaultAmplitude = noise.AmplitudeGain;
        defaultFrequency = noise.FrequencyGain;
        defaultNoiseSettings = noise.NoiseProfile;

        shootingScript = GetComponent<Shooting>();
        slapScript = GetComponent<Slap>();

        shootingScript.OnGunShot += GunShotNoise;
        slapScript.OnSlap += SlapNoise;
        slapScript.OnSlapRecived += GettingSlapedNoise;

    }
    private void OnDisable() {
        shootingScript.OnGunShot -= GunShotNoise;
        slapScript.OnSlap -= SlapNoise;
        slapScript.OnSlapRecived -= GettingSlapedNoise;
    }

    private void GunShotNoise()
    {
        StartCoroutine(CameraShakeRoutine(.5f, 1.8f, shootingNoise, 0, .1f));
    }

    private void SlapNoise()
    {
        StartCoroutine(CameraShakeRoutine(.5f, 5f, shootingNoise, .2f, .1f));
    }

    private void GettingSlapedNoise()
    {
        StartCoroutine(CameraShakeRoutine(1f, 3f, shootingNoise, .2f ,.15f));
    }

    private IEnumerator CameraShakeRoutine(float amplitude, float frequency, NoiseSettings noiseProfile, float time1,float time2)
    {
        yield return new WaitForSeconds(time1);
        // Increase noise for shake
        noise.NoiseProfile = noiseProfile;
        noise.AmplitudeGain = amplitude;  // Customize intensity
        noise.FrequencyGain = frequency;  // Customize frequency

        // Shake duration
        yield return new WaitForSeconds(time2);

        // Reset to default values
        noise.NoiseProfile = defaultNoiseSettings;
        noise.AmplitudeGain = defaultAmplitude;
        noise.FrequencyGain = defaultFrequency;
    }
}