using System.Collections.Generic;
using System.Net.Configuration;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    public int playerWithGun;
    public NetworkVariable<int> bulletPosition = new();
    public NetworkVariable<int> randomBulletPosition = new();
    public NetworkVariable<bool> isReloaded = new(false);


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            playerWithGun = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            CheckPlayerGunScript();
        }
    }
    public void OnClientShotChanged(ulong clientId, bool hasShot)
    {
        if (hasShot)
        {
            playerWithGun = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            while (playerWithGun == (int)clientId && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
            {
                playerWithGun = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            }
            CheckPlayerGunScript();
            bulletPosition.Value++;
            bulletPosition.Value %= 6;
        }
    }

    [ClientRpc]
    private void PlayerShootingScriptClientRpc(ulong clientId, bool activate)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<Shooting>(out var shootingScript))
        {
            if (NetworkManager.Singleton.LocalClientId == clientId)
            {
                shootingScript.enabled = activate;
            }
        }
    }

    private void CheckPlayerGunScript()
    {
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if ((int)clientId == playerWithGun)
            {
                PlayerShootingScriptClientRpc(clientId, true);
            }
            else
            {
                PlayerShootingScriptClientRpc(clientId, false);
            }
        }
    }
    public void Reload()
    {
        randomBulletPosition.Value = Random.Range(0, 6);
        isReloaded.Value = true;
    }

    [ClientRpc]
    public void StunPlayerClientRpc(ulong clientId)
    {
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == id)
            {
                NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<Ragdoll>().TriggerRagdoll();
            }
        }
    }

    [ClientRpc]
    public void SendVCClientRpc(byte[] voice, uint bytes, ulong senderId)
    {
        foreach(ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != senderId)
            {
                NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<VoiceSender>();
            }
        }
    }

    [ClientRpc]
    public void PlayFootstepClientRpc(ulong clientId)
    {
        // Fetch the player's object by their clientId
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(clientId, out NetworkObject playerObject))
        {
            // Get the FootstepScript from the player's object
            
            if (playerObject.TryGetComponent<FootStepScript>(out var footstepScript))
            {
                AudioSource footstepSource = footstepScript.footstepSource;

                // Randomize pitch and select footstep sound
                footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
                int index = Random.Range(0, footstepScript.footstepClips.Length);
                
                // Play footstep sound
                footstepSource.PlayOneShot(footstepScript.footstepClips[index], 0.9f);
            }
            else
            {
                Debug.LogWarning("FootStepScript not found on the player object.");
            }
        }
        else
        {
            Debug.LogWarning("Player object with clientId not found.");
        }
    }
}
