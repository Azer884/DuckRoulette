using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerData : MonoBehaviour 
{
    public static PlayerData Instance { get; private set; }
    public Dictionary<ulong, GameObject> playerInfo = new();

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
        if (sceneName != "Lobby" || playerInfo.Count == 0 || LobbySaver.instance == null || LobbySaver.instance.currentLobby == null)
            return;

        if (LobbyManager.instance == null) return;

        PlayerSpawner.Instance.isStarted = false;

        if (!NetworkManager.Singleton.IsHost)
        {
            LobbyManager.instance.ConnectedAsClient();

        }
        else
        {
            LobbyManager.instance.HostCreated();

        }

        LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, NetworkManager.Singleton.LocalClientId);
        NetworkTransmission.instance.IsTheClientReadyServerRPC(false, Coin.Instance.amount >= 5, NetworkManager.Singleton.LocalClientId);
        LobbyManager.instance.lobbyId.text = LobbySaver.instance.currentLobby?.Id.ToString();
    }
}