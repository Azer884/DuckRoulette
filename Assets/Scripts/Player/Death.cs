using UnityEngine;
using Unity.Netcode;

public class Death : NetworkBehaviour
{
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    // RequireOwnership (the default) means only this player's own client can report their own
    // death - collision physics runs locally on every client, including the victim's own, so
    // the victim's client always sees its own DeathTrigger.OnTriggerEnter fire too. Previously
    // RequireOwnership=false with a client-supplied clientId let any connected client kill (or
    // revive) any other player on demand.
    [ServerRpc]
    public void DieServerRpc(bool died = true)
    {
        isDead.Value = died;
    }

    [ServerRpc]
    public void KillPlayerServerRpc(bool died = true)
    {
        KillPlayerClientRpc(OwnerClientId, died);
    }
    [ClientRpc]
    private void KillPlayerClientRpc(ulong clientId, bool died = true)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            // Get the player's object and trigger the ragdoll
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                playerObject.GetComponent<Ragdoll>().TriggerRagdoll(died);
            }
        }
    }
}
