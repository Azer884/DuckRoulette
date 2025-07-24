using UnityEngine;

public class SFXWithAnimation : MonoBehaviour
{
    public bool isShawdow;
    public AudioSource audioSource;
    public void PlaySoundWithAnimation(AudioClip clip)
    {
        if (isShawdow) return;
        audioSource.PlayOneShot(clip);
    }
}
