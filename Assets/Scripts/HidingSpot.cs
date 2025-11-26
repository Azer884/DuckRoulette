using Unity.Netcode;
using UnityEngine;
using System.Collections;
using Unity.Netcode;
public class HidingSpot : NetworkBehaviour, IInteractable
{
    public Animation[]  animations;
    public string causeOfLeaving;
    public float hideDuration = 10f;
    public Transform hidingSpot, leavingSpot;
    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = false;
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

    [ServerRpc]
    private void HideServerRpc(ulong clientId)
    {
        HideClientRpc(clientId);
    }

    [ClientRpc]
    private void HideClientRpc(ulong clientId)
    {
        IsHeld = true;
        holderId = (int)clientId;

        if (OwnerClientId == clientId)
        {
            GameObject player = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
            
            PlayHidingAnimation(player);
            StartCountDown();
        }
    }

    [ServerRpc]
    private void ExitServerRpc(ulong clientId)
    {
        ExitClientRpc(clientId);
    }

    [ClientRpc]
    private void ExitClientRpc(ulong clientId)
    {
        if (OwnerClientId == clientId)
        {
            GameObject player = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
            
            PlayExitAnimation(player);
        }
        
        IsHeld = false;
        holderId = -1;
    }

    private void PlayHidingAnimation(GameObject player)
    {
        //Animation logic
        leavingSpot.position = player.transform.position;
        player.transform.position = hidingSpot.position;
        
        Ragdoll ragdoll = player.GetComponent<Ragdoll>();
            
        ragdoll.SetScriptsEnabled(false);
        ragdoll.SetVisualsEnabled(false);
    }
    
    private void PlayExitAnimation(GameObject player)
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
