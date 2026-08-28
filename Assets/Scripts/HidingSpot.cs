using Unity.Netcode;
using UnityEngine;
using System.Collections;
public class HidingSpot : NetworkBehaviour, IInteractable
{
    public Animation[]  animations;
    public string causeOfLeaving;
    public float hideDuration = 10f;
    public Transform hidingSpot, leavingSpot;
    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = false;
    public string InteractionPrompt => "Hide";
    public int holderId = -1;
    public void Interact(ulong clientId)
    {
        if (IsHeld) return;
        
        HideServerRpc(clientId);
    }

    public void Drop()
    {
        if (!IsHeld) return;

        ExitServerRpc((ulong)holderId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void HideServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        // clientId is otherwise a client-supplied value with no other check - without this, any
        // connected client could force an arbitrary player into (or out of) hiding.
        if (clientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        HideClientRpc(clientId);
    }

    [ClientRpc]
    private void HideClientRpc(ulong clientId)
    {
        IsHeld = true;
        holderId = (int)clientId;

        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            GameObject player = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
            
            Hide(player);
            StartCountDown();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ExitServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        if (clientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        ExitClientRpc(clientId);
    }

    [ClientRpc]
    private void ExitClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            GameObject player = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
            
            Exit(player);
        }
        
        IsHeld = false;
        holderId = -1;
    }

    private void Hide(GameObject player)
    {
        //Animation logic
        player.transform.position = hidingSpot.position;
        
        Ragdoll ragdoll = player.GetComponent<Ragdoll>();
            
        ragdoll.SetScriptsEnabled(false);
        ragdoll.SetVisualsEnabled(false);
    }
    
    private void Exit(GameObject player)
    {
        //Animation logic
        player.transform.position = leavingSpot.position;
        
        Ragdoll ragdoll = player.GetComponent<Ragdoll>();
            
        ragdoll.SetScriptsEnabled(true);
        ragdoll.SetVisualsEnabled(true);
    }

    private void StartCountDown()
    {
        StartCoroutine(CountDown());
    }

    private IEnumerator CountDown()
    {
        yield return new WaitForSeconds(hideDuration);
        
        Drop();
    }
}
