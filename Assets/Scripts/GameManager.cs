using System.Collections;
using System.Collections.Generic;
using Steamworks;
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

    private NetworkVariable<int> alivePlayersCount = new(0);
    private Dictionary<ulong, bool> playerStates = new();
    public List<int> playersKills;
    private int coinsToWin;


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

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        NetworkManager.Singleton.OnClientDisconnectCallback += UpdatePlayerState;
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            playerStates[clientId] = true;
        }
        playersKills = new(new int[playerStates.Count]);
        alivePlayersCount.Value = playerStates.Count;

        coinsToWin = NetworkManager.Singleton.ConnectedClientsIds.Count * 5;

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
        ChangeVarValueServerRpc(canShoot.Value, false);

        yield return new WaitForSeconds(waitTime); // Wait for 2 seconds before switching players

        ChangeVarValueServerRpc(canShoot.Value, true);
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

    [ServerRpc(RequireOwnership = false)]
    private void ChangeVarValueServerRpc(bool oldValue, bool newValue)
    {
        oldValue = newValue;
    }

    public void UpdatePlayerState(ulong clientId, bool isDead)
    {
        if (playerStates.ContainsKey(clientId))
        {
            playerStates[clientId] = !isDead;

            // Update alive player count
            alivePlayersCount.Value = isDead ? alivePlayersCount.Value - 1 : alivePlayersCount.Value + 1;

            if (alivePlayersCount.Value <= 1)
            {
                ulong winnerId = 10;
                foreach (var playerState in playerStates)
                {
                    if (playerState.Value) // Player is alive
                    {
                        winnerId = playerState.Key;
                        break;
                    }
                }
                EndGameServerRpc(winnerId);
            }
        }
    }

    public void UpdatePlayerState(ulong clientId)
    {
        if (playerStates.ContainsKey(clientId))
        {
            playerStates[clientId] = false;

            // Update alive player count
            UpdateAlivePlayerCountServerRpc(-1);

            if (alivePlayersCount.Value <= 1)
            {
                ulong winnerId = 10;
                foreach (var playerState in playerStates)
                {
                    if (playerState.Value) // Player is alive
                    {
                        winnerId = playerState.Key;
                        break;
                    }
                }
                EndGameServerRpc(winnerId);
            }
        }
    }
    [ServerRpc(RequireOwnership = false)]
    public void UpdateAlivePlayerCountServerRpc(int value)
    {
        alivePlayersCount.Value += value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndGameServerRpc(ulong winnerId)
    {
        EndGameClientRpc(winnerId);

        NetworkManager.Singleton.SceneManager.LoadScene("Lobby", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    [ClientRpc]
    private void EndGameClientRpc(ulong winnerId)
    {
        Cursor.lockState = CursorLockMode.Confined;
        PlayerSpawner.Instance.isStarted = false;

        Debug.Log($"Game Over! {GetPlayerNickname(winnerId)} Won.");
        Debug.Log(winnerId);

        if (winnerId == OwnerClientId)
        {
            Debug.Log($"Won {coinsToWin}");
            Coin.Instance.UpdateCoinAmount(coinsToWin);
        }
    }

    public string GetPlayerNickname(ulong clientId)
    {
        foreach (var playerObject in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (playerObject.ClientId == clientId && playerObject.PlayerObject != null)
            {
                if (playerObject.PlayerObject.TryGetComponent<Username>(out var username))
                {
                    return username.playerName.Value.ToString();
                }
            }
        }

        // Return a placeholder if the player is not found
        return "Unknown Player";
    }

    private void OnClientDisconnect(ulong clientId)
    {
        bool haveTheGun = (int)clientId == playerWithGun;
        if (haveTheGun && !IsHost)
        {
            playerWithGun = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            StartCoroutine(SwitchPlayerAfterDelay(.5f));
        }
        Debug.Log($"{GetPlayerNickname(clientId)} has left the game.");
    
    }

    public void OnDisable()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        NetworkManager.Singleton.OnClientDisconnectCallback -= UpdatePlayerState;
    }
}
