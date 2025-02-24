using UnityEngine;
using Unity.Netcode;

public class Death : NetworkBehaviour
{
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    [ServerRpc(RequireOwnership = false)]
    public void DieServerRpc(bool died = true)
    {
        isDead.Value = died;
    }

    [ServerRpc(RequireOwnership = false)]
    public void KillPlayerServerRpc(ulong clientId, bool died = true)
    {
        KillPlayerClientRpc(clientId, died);
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
