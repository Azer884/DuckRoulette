using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System;
using Steamworks;

public class PlayerSpawner : NetworkBehaviour
{

    public static PlayerSpawner Instance;
    [SerializeField]private GameObject player;
    public bool isStarted;

    void Start() 
    {
        if(Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneLoaded;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += GoBackToLobby;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= SceneLoaded;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= GoBackToLobby;
        }
    }


    private void SceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        // isStarted only means "GameScene" already spawned players for this round - it must
        // never latch true on an unrelated scene's load event (e.g. LoadingScreen), or the real
        // GameScene event that follows gets blocked by the guard below before it even checks
        // the scene name, and players stop spawning entirely.
        if (sceneName == "Lobby")
        {
            isStarted = false;
            return;
        }

        if (sceneName != "GameScene" || isStarted)
        {
            return;
        }

        isStarted = true;

        if (IsHost)
        {
            foreach (ulong id in clientsCompleted)
            {
                GameObject player0 = Instantiate(player);
                player0.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);
            }
        }
    }

    private void GoBackToLobby(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != "Lobby")
            return;
            
        // Check all required components exist
        if (LobbySaver.instance == null || LobbySaver.instance.currentLobby == null)
        {
            Debug.LogWarning("LobbySaver or current lobby is null");
            return;
        }
        
        if (GameNetworkManager.Instance == null)
        {
            Debug.LogWarning("GameNetworkManager is null");
            return;
        }
        
        if (NetworkTransmission.instance == null)
        {
            Debug.LogWarning("NetworkTransmission is null");
            return;
        }

        if (IsHost)
        {
            LobbyManager.instance.HostCreated();
            AddPlayersClientRpc();
        }
        else
        {
            LobbyManager.instance.ConnectedAsClient();
        }
    }
    [ClientRpc]
    private void AddPlayersClientRpc()
    {
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, OwnerClientId);
        LobbyManager.instance.lobbyId.text = LobbySaver.instance.currentLobby?.Id.ToString();
        
        Cursor.lockState = CursorLockMode.Confined;
    }
}