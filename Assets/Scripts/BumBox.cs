using UnityEngine;
using Unity.Netcode;

public class BumBox : NetworkBehaviour, IInteractable
{

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = true;
    public int holderId = -1;


    public void Interact(ulong clientId)
    {
        if (IsHeld) return;
        PickUpServerRpc(clientId);

        Interact interact = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<Interact>();
        interact.fakeBox.gameObject.SetActive(true);
        interact.fakeboxShadow.gameObject.SetActive(true);
    }
    public void Drop()
    {
        if (!IsHeld) return;
        DropServerRpc();

        Interact interact = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<Interact>();
        interact.fakeBox.gameObject.SetActive(false);
        interact.fakeboxShadow.gameObject.SetActive(false);
    }

    public void Mute()
    {
        MuteServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PickUpServerRpc(ulong clientId)
    {
        PickUpClientRpc(clientId);
    }

    [ClientRpc]
    private void PickUpClientRpc(ulong clientId)
    {
        IsHeld = true;
        if (GetComponent<Rigidbody>() != null)
        {
            Destroy(GetComponent<Rigidbody>());
        }
        if (GetComponent<Collider>() != null)
        {
            GetComponent<Collider>().isTrigger = true;
        }
        holderId = (int)clientId;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropServerRpc()
    {
        DropClientRpc();
    }

    [ClientRpc]
    private void DropClientRpc()
    {
        IsHeld = false;
        GetComponent<Collider>().isTrigger = false;
        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.AddForce(NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject((ulong)holderId).transform.forward * 5f, ForceMode.Impulse);
        holderId = -1;
    }

    [ServerRpc(RequireOwnership = false)]
    private void MuteServerRpc()
    {
        MuteClientRpc();
    }

    [ClientRpc]
    private void MuteClientRpc()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource.isPlaying)
        {
            audioSource.Pause();
        }
        else
        {
            audioSource.UnPause();
        }
    }
}
