using System.Collections.Generic;
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
}
