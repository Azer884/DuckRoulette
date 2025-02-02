using System.Collections;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    public NetworkVariable<int> playerWithGun = new(-1);
    public NetworkVariable<int> bulletPosition = new();
    public NetworkVariable<int> randomBulletPosition = new();
    public NetworkVariable<bool> isReloaded = new(false);
    public NetworkVariable<bool> canShoot = new(true),
    powerGunIsActive = new(false);

    private NetworkVariable<int> alivePlayersCount = new(0);
    private Dictionary<ulong, bool> playerStates = new();
    public  List<int> playersKills = new();
    private int coinsToWin;
    private bool isGameEnded = false;
    private List<(ulong, ulong)> teams = new();

    //Events
    public delegate void OnPowerGunActive();
    public static event OnPowerGunActive OnPowerGunActived;
    public delegate void OnHostDisconnect();
    public static event OnHostDisconnect OnHostDisconnected;

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
        NetworkManager.Singleton.OnClientDisconnectCallback += UpdatePlayerStateServerRpc;
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            playerStates[clientId] = true;
        }
        playersKills = new(new int[NetworkManager.Singleton.ConnectedClientsIds.Count]);
        if(IsServer)
        {
            alivePlayersCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
            playerWithGun.Value = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            CheckPlayerGunScript();
        }
        
        coinsToWin = NetworkManager.Singleton.ConnectedClientsIds.Count * 5;

    }

    [ServerRpc(RequireOwnership = false)]
    public void OnClientShotChangedServerRpc(ulong clientId, bool hasShot)
    {
        if (hasShot)
        {
            playerWithGun.Value = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
            while (playerWithGun.Value == (int)clientId && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
            {
                playerWithGun.Value = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
                CheckPlayerGunScript();
            }
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
        if (!powerGunIsActive.Value)
        {
            powerGunIsActive.Value = Percentage(1);

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                PlayerShootingScriptClientRpc(clientId, (int)clientId == playerWithGun.Value);
            }
        }
        else
        {
            ActivatePowerGun();
        }
    }

    private void ActivatePowerGun()
    {
        NotifyPlayersClientRpc("Power gun is active!", true, 10);
        playerWithGun.Value = Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);

        // Wait for 10 seconds, then notify and assign gun
        Invoke(nameof(AssignGun), 10f);
        OnPowerGunActived?.Invoke();
    }

    private void AssignGun()
    {
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            PlayerShootingScriptClientRpc(clientId, (int)clientId == playerWithGun.Value);
        }
        NotifyPlayersClientRpc($"{GetPlayerNickname((ulong)playerWithGun.Value)} has the gun now.", true, 5);

        // Wait for 5 seconds, then deactivate the power gun
        Invoke(nameof(DeactivatePowerGun), 5f);
    }

    private void DeactivatePowerGun()
    {
        powerGunIsActive.Value = false;
        OnClientShotChangedServerRpc((ulong)playerWithGun.Value, true);

        NotifyPlayersClientRpc("Power gun is no longer active!");
    }
    public void Reload()
    {
        randomBulletPosition.Value = Random.Range(0, 6);
        isReloaded.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdatePlayerStateServerRpc(ulong clientId)
    {
        if (playerStates.ContainsKey(clientId))
        {
            playerStates[clientId] = false;

            // Update alive player count
            alivePlayersCount.Value--;

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
    private void EndGameServerRpc(ulong winnerId)
    {
        UpdateStatsClientRpc();
        EndGameClientRpc(winnerId, playersKills.ToArray());
    }

    [ClientRpc]
    private void UpdateStatsClientRpc()
    {
        if(NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<Stats>(out var stats))
        {
            stats.timeSurvived.Value = stats.GetComponent<HideGun>().survivedTime;
            stats.shotCounter.Value = stats.GetComponent<Shooting>().shotCounter;
            stats.emptyShots.Value = stats.GetComponent<Shooting>().emptyShots;
        }
    }

    [ClientRpc]
    private void EndGameClientRpc(ulong winnerId, int[] playersKills)
    {
        if (isGameEnded) return;
        isGameEnded = true;

        Cursor.lockState = CursorLockMode.Confined;
        PlayerSpawner.Instance.isStarted = false;

        Debug.Log($"Game Over! {GetPlayerNickname(winnerId)} Won.");
        if(NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<PauseMenu>(out var pauseMenu))
        {
            pauseMenu.End();
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                GameObject currentPlayer = Instantiate(pauseMenu.playerStatsObj, pauseMenu.endGamePanel.transform.GetChild(0).GetChild(6));

                //PlayerName
                TextMeshProUGUI stat = currentPlayer.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                stat.text = GetPlayerNickname(clientId);
                if (clientId == winnerId)
                {
                    stat.color = Color.yellow;
                }

                //PlayerKills
                stat = currentPlayer.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
                stat.text = playersKills[(int)clientId].ToString();

                //CoinsToWin
                int coins = playersKills[(int)clientId] * 2 + 1;
                if (clientId == winnerId)
                {
                    coins += coinsToWin; 
                }

                stat = currentPlayer.transform.GetChild(2).GetComponent<TextMeshProUGUI>();
                stat.text = $"{coins}";
                Coin.Instance.UpdateCoinAmount(coins);

                //PlayerSurvivalTime
                int minutes = Mathf.FloorToInt(pauseMenu.GetComponent<Stats>().timeSurvived.Value / 60f);
                int seconds = Mathf.FloorToInt(pauseMenu.GetComponent<Stats>().timeSurvived.Value % 60f);

                string formattedTime = $"{minutes:D2}m {seconds:D2}s";

                stat = currentPlayer.transform.GetChild(3).GetComponent<TextMeshProUGUI>();
                stat.text = formattedTime;

                //PlayerAccuracy
                stat = currentPlayer.transform.GetChild(4).GetComponent<TextMeshProUGUI>();
                stat.text = "0%";
                if(pauseMenu.GetComponent<Stats>().shotCounter.Value > 0)
                {
                    stat.text = (playersKills[(int)clientId] / pauseMenu.GetComponent<Stats>().shotCounter.Value * 100).ToString() + "%";
                }

                //Luck
                string luck = "0%";
                if(pauseMenu.GetComponent<Stats>().emptyShots.Value > 0)
                {
                    luck = (pauseMenu.GetComponent<Stats>().shotCounter.Value / pauseMenu.GetComponent<Stats>().shotCounter.Value * 100).ToString() + "%";
                }

                //stat = currentPlayer.transform.GetChild(5).GetComponent<TextMeshProUGUI>();
                //stat.text = luck;
            }
        }

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
        bool haveTheGun = (int)clientId == playerWithGun.Value;
        if (haveTheGun && !IsHost)
        {
            OnClientShotChangedServerRpc(clientId, true);
            Debug.Log($"{GetPlayerNickname(clientId)} has left the game.");
        }
    }

    public void OnDisable()
    {
        if (this == Instance)
        {
            OnHostDisconnected?.Invoke();
            LeaveGame();
        }
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        NetworkManager.Singleton.OnClientDisconnectCallback -= UpdatePlayerStateServerRpc;
    }

    public void LeaveGame()
    {
        LeaveSteamLobby();

        PlayerSpawner.Instance.isStarted = false;
        Cursor.lockState = CursorLockMode.Confined;
        SceneManager.LoadScene("Lobby");
    
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    public void LeaveSteamLobby()
    {
        if (SteamClient.IsValid && LobbySaver.instance.currentLobby != null)
        {
            LobbySaver.instance.currentLobby?.Leave();

            LobbyManager.instance.playerInfo.Remove(OwnerClientId);
            Debug.Log("Left Steam lobby successfully.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdateKillsServerRpc(ulong shooterId, int killAmount)
    {
        playersKills[(int)shooterId] += killAmount;
    }

    #region TeamUp

    [ServerRpc(RequireOwnership = false)]
    public void TeamUpRequestServerRpc(ulong teamMateId, ServerRpcParams serverRpcParams = default)
    {
        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { teamMateId }
            }
        };
        SendTeamUpRequestClientRpc(serverRpcParams.Receive.SenderClientId, clientRpcParams);
    }

    [ClientRpc]
    private void SendTeamUpRequestClientRpc(ulong senderId, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            if(teamUp.isTeamedUp)
            {
                return;
            }

            teamUp.RequestTeamUp(senderId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void TeamUpResponseServerRpc(ulong teamMateId, ulong requesterId, Vector3 soundPosition, int isPerfectDap, ServerRpcParams serverRpcParams = default)
    {
        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { requesterId}
            }
        };
        bool isPerfectDapBool = isPerfectDap == 1;
        PlayDapSoundClientRpc(soundPosition, isPerfectDapBool);

        teams.Add((requesterId, teamMateId));
        SendTeamUpResponseClientRpc(teamMateId, clientRpcParams);
    }
    
    [ClientRpc]
    private void SendTeamUpResponseClientRpc(ulong teamMateId, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.isTeamedUp = true;
            teamUp.teamMateId = (int)teamMateId;
            Debug.Log("You have teamed up with " + teamMateId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTeamUpServerRpc(ulong teamMateId, ServerRpcParams serverRpcParams = default)
    {
        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { teamMateId }
            }
        };

        // Remove the team regardless of the order of the tuple elements
        teams.RemoveAll(team => (team.Item1 == serverRpcParams.Receive.SenderClientId && team.Item2 == teamMateId) ||
                                (team.Item1 == teamMateId && team.Item2 == serverRpcParams.Receive.SenderClientId));
        SendEndTeamUpClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void SendEndTeamUpClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.EndTeamUp();
        }
    }

    [ClientRpc]
    private void PlayDapSoundClientRpc(Vector3 soundPosition, bool isPerfectDap)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.PlayDapSound(soundPosition, isPerfectDap);
        }
    }

    #endregion

    #region  Spectate

    public CinemachineCamera GetPlayerSpectateCam(ulong clientId)
    {
        foreach (var playerObject in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (playerObject.ClientId == clientId && playerObject.PlayerObject != null)
            {
                if (playerObject.PlayerObject.transform.GetChild(playerObject.PlayerObject.transform.childCount - 1).TryGetComponent<CinemachineCamera>(out var cam))
                {
                    return cam;
                }
            }
        }

        // Return a placeholder if the player is not found
        return null;
    }

    #endregion

    bool Percentage(int percentageChance)
    {
        if (percentageChance < 100)
        {
            int randomValue = Random.Range(0, 100); // Generates a number between 0 and 99
            return randomValue < percentageChance;
        }
        return true;
    }

    [ClientRpc]
    private void NotifyPlayersClientRpc(string message, bool activateCoolDown = false, int coolDownTime = 0)
    {
        Debug.Log(message);

        if (activateCoolDown && NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<UIManager>(out var uiManager))
        {
            uiManager.StartCoolDown(coolDownTime);
        }
    }
}
