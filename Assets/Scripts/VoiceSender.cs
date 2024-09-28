using UnityEngine;
using Unity.Netcode;
using System;
using Steamworks;

public class VoiceSender : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }
    [ServerRpc]
    private void SendVCServerRpc(byte[] voice, uint bytes, ulong senderId)
    {
        GameManager.Instance.SendVCClientRpc(voice, bytes, senderId);
    }

    public void SendVC(byte[] voiceBuffer, uint bytesWritten)
    {
        SendVCServerRpc(voiceBuffer, bytesWritten, OwnerClientId);
    }
}
