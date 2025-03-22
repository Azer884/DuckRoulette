using Unity.Netcode;
using UnityEngine;

public class BlackjackTable : MonoBehaviour, IInteractable
{
    public bool IsHeld { get; set;} = false;
    public bool IsPickable {get; set;} = false;

    public void Drop()
    {
        CardDeck.instance.ExitGame(NetworkManager.Singleton.LocalClientId);
        IsHeld = false;
    }

    public void Interact(ulong clientId)
    {
        Transform hand = transform.GetChild((int)clientId);
        NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<BlackJack>().handTransform = hand;

        CardDeck.instance.EnterGame(clientId);
        IsHeld = true;
    }
}
