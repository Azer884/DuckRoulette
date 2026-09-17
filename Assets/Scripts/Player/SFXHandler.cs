using UnityEngine;

public class SFXHandler : MonoBehaviour
{
    private Slap slapComponent;

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

        // Spawn its own 3D one-shot on the SFX group instead of the shared 2D voice source
        // (H2): pain sounds no longer follow the Voice slider or get ducked by talking.
        SFXManager.Instance.PlayAt(clip, transform.position);
    }
    void OnDisable()
    {
        slapComponent.OnSlapRecived -= PainSound;
    }
}
