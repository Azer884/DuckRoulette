using UnityEngine;
using UnityEngine.Audio;

public class SFXWithAnimation : MonoBehaviour
{
    public bool isShawdow;
    public AudioSource audioSource;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

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

        if (sfxMixerGroup != null)
        {
            audioSource.outputAudioMixerGroup = sfxMixerGroup;
        }
    }

    public void PlaySoundWithAnimation(AudioClip clip)
    {
        if (isShawdow || clip == null || audioSource == null) return;

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            return;
        }

        audioSource.PlayOneShot(clip);
    }
}
