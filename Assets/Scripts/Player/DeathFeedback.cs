using UnityEngine;

// Local-only "you're dead" screen treatment - dims/desaturates via DeathVignette while isDead
// is true. Same co-located-component + IsOwner gate pattern as SlapFeedback: isDead is a
// NetworkVariable every client observes (that's how ragdoll/spectate replicate), but only the
// actual dying player's own screen should dim.
public class DeathFeedback : MonoBehaviour
{
    private Death death;

    private void Awake()
    {
        death = GetComponent<Death>();
    }

    private void Start()
    {
        death.isDead.OnValueChanged += HandleIsDeadChanged;
    }

    private void OnDestroy()
    {
        if (death != null)
        {
            death.isDead.OnValueChanged -= HandleIsDeadChanged;
        }
    }

    private void HandleIsDeadChanged(bool oldValue, bool newValue)
    {
        if (!death.IsOwner)
        {
            return;
        }

        DeathVignette.GetOrAdd(gameObject).SetDead(newValue);
    }
}
