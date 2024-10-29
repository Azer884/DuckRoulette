using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    public int playerWithGun = -1;
    public NetworkVariable<int> bulletPosition = new();
    public NetworkVariable<int> randomBulletPosition = new();
    public NetworkVariable<bool> isReloaded = new(false);
    public NetworkVariable<bool> canShoot = new(true);


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
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        playerWithGun = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
        CheckPlayerGunScript();
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
        StartCoroutine(SwitchPlayerAfterDelay(2f));
    }

    private IEnumerator SwitchPlayerAfterDelay(float waitTime)
    {
        canShoot.Value = false;

        yield return new WaitForSeconds(waitTime); // Wait for 2 seconds before switching players

        canShoot.Value = true;
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
