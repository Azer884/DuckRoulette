using UnityEngine;
using Unity.Netcode;

public class Death : NetworkBehaviour
{
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    // This player has one DeathTrigger per hitbox collider (13 on the rig), all sharing this
    // Death component - a single bullet can overlap several hitboxes in the same physics step,
    // and isDead.Value hasn't round-tripped back from the server yet to gate the duplicates within
    // that same frame. Plain local (non-networked) flag: each client keeps its own copy of this
    // component, so this dedupes each client's own redundant hitbox triggers for one death event
    // without blocking other clients from independently reporting it too.
    private bool _deathReported;
    public bool TryReportDeath()
    {
        if (_deathReported)
        {
            return false;
        }

        _deathReported = true;
        return true;
    }

    // Death used to be reported via owner-gated ServerRpcs (RequireOwnership meant only the
    // victim's own client could call them). That was reliable-sounding but wasn't in practice:
    // the victim's own transform has zero interpolation lag (owner-authoritative), while the
    // incoming bullet is simulated from a slightly-stale snapshot of where they were, so the
    // victim's own OnTriggerEnter is the LEAST reliable detector of everyone's - the Editor log
    // showed "Collision detected" firing repeatedly from bystanders/the shooter but
    // KillPlayerClientRpc never firing at all, i.e. ragdoll silently never triggered. isDead is
    // now set directly by the server (default NetworkVariable write permission is Server, not
    // Owner, so this doesn't need an RPC at all) from GameManager.UpdatePlayerStateServerRpc,
    // which any peer that reliably detects the hit can already call.
}
