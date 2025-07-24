using UnityEngine;

public class SFXHandler : MonoBehaviour
{
    private Slap slapComponent;

    [SerializeField]private AudioSource source;
    [SerializeField] private AudioClip[] slapsAudioClips;

    void Awake()
    {
        slapComponent = GetComponent<Slap>();
    }
    void OnEnable()
    {
        slapComponent.OnSlapRecived += PainSound;
    }
    public void PainSound()
    {
        int randomInt = Random.Range(0, slapsAudioClips.Length);

        source.PlayOneShot(slapsAudioClips[randomInt]);
    }
    void OnDisable()
    {
        slapComponent.OnSlapRecived -= PainSound;
    }
}
