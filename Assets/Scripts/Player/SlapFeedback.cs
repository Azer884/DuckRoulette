using UnityEngine;

// Local-only "you got slapped" screen feedback: reddens the screen (HitFlash, a URP Vignette
// punch) and points SlapDirectionHUD's arrow back at whoever hit you. Same
// subscribe-to-co-located-Slap pattern as SFXHandler, but gated to the owner - OnSlapRecivedFrom
// fires on every client's copy of the victim (see Slap.SlapImpactClientRpc), and only the actual
// victim's own client has a screen worth reddening.
public class SlapFeedback : MonoBehaviour
{
    [SerializeField] private Color flashColor = new(0.6f, 0f, 0f, 1f);
    [SerializeField] private float flashIntensity = 0.45f;
    [SerializeField] private float flashDuration = 0.5f;

    private Slap slapComponent;

    private void Awake()
    {
        slapComponent = GetComponent<Slap>();
    }

    private void OnEnable()
    {
        slapComponent.OnSlapRecivedFrom += HandleSlapRecivedFrom;
    }

    private void OnDisable()
    {
        slapComponent.OnSlapRecivedFrom -= HandleSlapRecivedFrom;
    }

    private void HandleSlapRecivedFrom(Vector3 attackerPosition)
    {
        if (!slapComponent.IsOwner)
        {
            return;
        }

        HitFlash.GetOrAdd(gameObject).Flash(flashColor, flashIntensity, flashDuration);
        SlapDirectionHUD.Show(attackerPosition);
    }
}
