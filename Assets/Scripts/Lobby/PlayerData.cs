using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerData : NetworkBehaviour 
{
    public static PlayerData Instance { get; private set; }

    public Dictionary<ulong, (string, ulong)> playerInfo = new();

    private void Awake()
    {
        if (Instance == null)
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

    private void OnEnable()
    {
        SceneManager.sceneLoaded += GoBackToLobby;
    }

    private void GoBackToLobby(Scene arg0, LoadSceneMode arg1)
    {
        string sceneName = arg0.name;
        if (sceneName != "Lobby" || LobbySaver.instance == null || LobbySaver.instance.currentLobby == null)
            return;

        // Clear existing playerInfo in the lobby to avoid duplicates
        if (LobbyManager.instance != null)
        {
            LobbyManager.instance.ClearPlayerInfo();  // Ensure this method clears out old entries before repopulating
        }

        foreach (KeyValuePair<ulong, (string, ulong)> info in playerInfo)
        {
            _ = LobbyManager.instance.AddPlayerToDictionaryAsync(info.Key, info.Value.Item1, info.Value.Item2);
        }

        if (IsHost)
        {
            LobbyManager.instance.HostCreated();
            NetworkManager.Singleton.OnServerStarted += GameNetworkManager.Instance.Singleton_OnServerStarted;
            LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
            LobbyManager.instance.UpdateClients();
        }
        else
        {
            NetworkManager.Singleton.OnClientConnectedCallback += GameNetworkManager.Instance.Singleton_OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback += GameNetworkManager.Instance.Singleton_OnClientDisconnectCallback;
            LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;

            LobbyManager.instance.ConnectedAsClient();
            LobbyManager.instance.UpdateClients();
        }

        LobbyManager.instance.lobbyId.text = LobbySaver.instance.currentLobby?.Id.ToString();
    }

}