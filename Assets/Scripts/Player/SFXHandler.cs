using UnityEngine;

public class SFXHandler : MonoBehaviour
{
    private Slap slapComponent;

    [SerializeField]private AudioSource source;

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
        if (SFXManager.Instance == null) return;

        AudioClip clip = SFXManager.Instance.RandomSlapPain();
        if (clip == null) return;

        source.PlayOneShot(clip);
    }
    void OnDisable()
    {
        slapComponent.OnSlapRecived -= PainSound;
    }
}
