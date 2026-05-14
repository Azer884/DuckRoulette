using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class NoiseHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera virtualCamera;

    [Header("Movement Shake")]
    [SerializeField] private float walkShakeAmplitude = 0.05f;
    [SerializeField] private float walkShakeFrequency = 0.8f;
    [SerializeField] private float runShakeAmplitude = 0.12f;
    [SerializeField] private float runShakeFrequency = 1.6f;
    [SerializeField] private float movementSpeedThreshold = 1.2f;
    [SerializeField] private float movementSmoothing = 10f;

    [Header("Burst Shake")]
    [SerializeField] private float jumpShakeAmplitude = 0.35f;
    [SerializeField] private float jumpShakeFrequency = 2.0f;
    [SerializeField] private float jumpShakeDuration = 0.12f;
    [SerializeField] private float shootShakeAmplitude = 0.45f;
    [SerializeField] private float shootShakeFrequency = 1.8f;
    [SerializeField] private float shootShakeDuration = 0.1f;
    [SerializeField] private float slapShakeAmplitude = 0.55f;
    [SerializeField] private float slapShakeFrequency = 5.0f;
    [SerializeField] private float slapShakeDuration = 0.12f;
    [SerializeField] private float hitShakeAmplitude = 0.9f;
    [SerializeField] private float hitShakeFrequency = 3.0f;
    [SerializeField] private float hitShakeDuration = 0.15f;

    private Shooting shootingScript;
    private Slap slapScript;
    private Movement movementScript;

    private CinemachineBasicMultiChannelPerlin noise;
    private float defaultAmplitude;
    private float defaultFrequency;
    private NoiseSettings defaultNoiseSettings;
    public NoiseSettings shootingNoise;

    private Coroutine _activeBurstRoutine;
    private float _targetAmplitude;
    private float _targetFrequency;
    private NoiseSettings _targetNoiseProfile;
    private bool _isBurstActive;

    private void OnEnable() 
    {
        if (virtualCamera == null)
        {
            virtualCamera = GetComponentInChildren<CinemachineCamera>();
        }

        movementScript = GetComponent<Movement>();
        shootingScript = GetComponent<Shooting>();
        slapScript = GetComponent<Slap>();

        if (virtualCamera == null)
        {
            Debug.LogWarning($"{nameof(NoiseHandler)} on '{name}' has no CinemachineCamera assigned.");
            return;
        }

        noise = virtualCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise == null)
        {
            Debug.LogWarning($"{nameof(NoiseHandler)} on '{name}' has no CinemachineBasicMultiChannelPerlin component.");
            return;
        }

        // Store the default noise values
        defaultAmplitude = noise.AmplitudeGain;
        defaultFrequency = noise.FrequencyGain;
        defaultNoiseSettings = noise.NoiseProfile;

        _targetAmplitude = defaultAmplitude;
        _targetFrequency = defaultFrequency;
        _targetNoiseProfile = defaultNoiseSettings;

        if (shootingScript != null)
        {
            shootingScript.OnGunShot += GunShotNoise;
        }

        if (slapScript != null)
        {
            slapScript.OnSlap += SlapNoise;
            slapScript.OnSlapRecived += GettingSlapedNoise;
        }

    }
    private void OnDisable() {
        if (shootingScript != null)
        {
            shootingScript.OnGunShot -= GunShotNoise;
        }

        if (slapScript != null)
        {
            slapScript.OnSlap -= SlapNoise;
            slapScript.OnSlapRecived -= GettingSlapedNoise;
        }

        if (_activeBurstRoutine != null)
        {
            StopCoroutine(_activeBurstRoutine);
            _activeBurstRoutine = null;
        }

        if (noise != null)
        {
            noise.NoiseProfile = defaultNoiseSettings;
            noise.AmplitudeGain = defaultAmplitude;
            noise.FrequencyGain = defaultFrequency;
        }
    }

    private void Update()
    {
        if (noise == null)
        {
            return;
        }

        if (!_isBurstActive)
        {
            UpdateMovementShake();
        }

        noise.NoiseProfile = _targetNoiseProfile != null ? _targetNoiseProfile : defaultNoiseSettings;
        noise.AmplitudeGain = Mathf.Lerp(noise.AmplitudeGain, _targetAmplitude, movementSmoothing * Time.deltaTime);
        noise.FrequencyGain = Mathf.Lerp(noise.FrequencyGain, _targetFrequency, movementSmoothing * Time.deltaTime);
    }

    public void TriggerJumpShake()
    {
        StartBurst(jumpShakeAmplitude, jumpShakeFrequency, defaultNoiseSettings, jumpShakeDuration);
    }

    public void TriggerStepShake(bool isRunning)
    {
        float amplitude = isRunning ? runShakeAmplitude : walkShakeAmplitude;
        float frequency = isRunning ? runShakeFrequency : walkShakeFrequency;
        StartBurst(amplitude, frequency, defaultNoiseSettings, 0.08f);
    }

    public void TriggerGunShotShake()
    {
        StartBurst(shootShakeAmplitude, shootShakeFrequency, shootingNoise != null ? shootingNoise : defaultNoiseSettings, shootShakeDuration);
    }

    public void TriggerSlapShake()
    {
        StartBurst(slapShakeAmplitude, slapShakeFrequency, defaultNoiseSettings, slapShakeDuration);
    }

    public void TriggerSlapReceivedShake()
    {
        StartBurst(hitShakeAmplitude, hitShakeFrequency, defaultNoiseSettings, hitShakeDuration);
    }

    private void GunShotNoise()
    {
        TriggerGunShotShake();
    }

    private void SlapNoise()
    {
        TriggerSlapShake();
    }

    private void GettingSlapedNoise()
    {
        TriggerSlapReceivedShake();
    }

    private void UpdateMovementShake()
    {
        if (movementScript == null)
        {
            _targetAmplitude = defaultAmplitude;
            _targetFrequency = defaultFrequency;
            _targetNoiseProfile = defaultNoiseSettings;
            return;
        }

        float speed = movementScript.realMovementSpeed;
        if (speed < movementSpeedThreshold)
        {
            _targetAmplitude = defaultAmplitude;
            _targetFrequency = defaultFrequency;
            _targetNoiseProfile = defaultNoiseSettings;
            return;
        }

        float t = Mathf.InverseLerp(movementSpeedThreshold, movementSpeedThreshold * 2f, speed);
        _targetAmplitude = Mathf.Lerp(walkShakeAmplitude, runShakeAmplitude, t);
        _targetFrequency = Mathf.Lerp(walkShakeFrequency, runShakeFrequency, t);
        _targetNoiseProfile = defaultNoiseSettings;
    }

    private void StartBurst(float amplitude, float frequency, NoiseSettings noiseProfile, float duration)
    {
        if (noise == null)
        {
            return;
        }

        if (_activeBurstRoutine != null)
        {
            StopCoroutine(_activeBurstRoutine);
        }

        _activeBurstRoutine = StartCoroutine(CameraShakeRoutine(amplitude, frequency, noiseProfile, duration));
    }

    private IEnumerator CameraShakeRoutine(float amplitude, float frequency, NoiseSettings noiseProfile, float duration)
    {
        _isBurstActive = true;
        _targetNoiseProfile = noiseProfile != null ? noiseProfile : defaultNoiseSettings;
        _targetAmplitude = amplitude;
        _targetFrequency = frequency;

        yield return new WaitForSeconds(duration);

        _isBurstActive = false;
        _targetNoiseProfile = defaultNoiseSettings;
        _targetAmplitude = defaultAmplitude;
        _targetFrequency = defaultFrequency;

        _activeBurstRoutine = null;
    }
}