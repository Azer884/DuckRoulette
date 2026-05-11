using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;

public class NetworkOneShotAudio : NetworkBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clip;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;
    [SerializeField] private bool destroyWithClipLength = true;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        if (sfxMixerGroup != null)
        {
            audioSource.outputAudioMixerGroup = sfxMixerGroup;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (clip == null && audioSource != null)
        {
            clip = audioSource.clip;
        }

        if (clip == null)
        {
            Debug.LogWarning($"{nameof(NetworkOneShotAudio)} on '{name}' has no clip assigned.");
            if (IsServer)
            {
                NetworkObject.Despawn();
            }
            return;
        }

        if (audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }

        if (IsServer && destroyWithClipLength)
        {
            StartCoroutine(DespawnAfterDelay(clip.length));
        }
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}

