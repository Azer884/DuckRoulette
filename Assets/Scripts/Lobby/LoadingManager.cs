using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject))]
public class LoadingManager : NetworkBehaviour
{
    public static LoadingManager Instance;

    [Header("UI References")]
    [Tooltip("Root GameObject for your loading UI (canvas/panel)")]
    public GameObject loadingScreen;
    [Tooltip("Slider (0–1) to show progress")]
    public Slider progressBar;
    [Tooltip("Optional text for percent (e.g. “50%”)")]
    public TextMeshProUGUI percentText;

    [Header("Player Prefab")]
    [Tooltip("The player prefab to spawn")]
    public GameObject playerPrefab;

    // Tracks which clients have reported 100%
    private readonly Dictionary<ulong,bool> clientsDone = new Dictionary<ulong,bool>();
    private AsyncOperation loadOp;
    private bool activationAllowed = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Listen for Unity scene-loaded so host can spawn players
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Call this on the SERVER to begin a synchronized load & spawn.
    /// </summary>
    public void StartGame(string sceneName)
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only the server can StartGame()");
            return;
        }

        // Reset tracking
        clientsDone.Clear();
        foreach(var id in NetworkManager.Singleton.ConnectedClientsIds)
            clientsDone[id] = false;

        // Tell all clients to show UI and start loading
        BeginLoadClientRpc(sceneName);
    }

    [ClientRpc]
    private void BeginLoadClientRpc(string sceneName, ClientRpcParams rpcParams = default)
    {
        // Show UI
        loadingScreen.SetActive(true);
        progressBar.value = 0f;
        if (percentText != null) percentText.text = "0%";

        // Kick off async load without activating
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        loadOp.allowSceneActivation = false;

        // Report progress [0 → 0.9)
        while (loadOp.progress < 0.9f)
        {
            float p = loadOp.progress / 0.9f;
            UpdateLocalUI(p);
            ReportProgressServerRpc(p);
            yield return null;
        }

        // Final “ready” tick at 1.0
        UpdateLocalUI(1f);
        ReportProgressServerRpc(1f);

        // Wait for host/server to say “go”
        while (!activationAllowed)
            yield return null;

        // Now activate the scene
        loadOp.allowSceneActivation = true;
    }

    private void UpdateLocalUI(float p)
    {
        progressBar.value = p;
        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(p * 100)}%";
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportProgressServerRpc(float progress, ServerRpcParams rpcParams = default)
    {
        var clientId = rpcParams.Receive.SenderClientId;
        if (progress >= 1f && clientsDone.ContainsKey(clientId))
        {
            clientsDone[clientId] = true;
            CheckAllClientsReady();
        }
    }

    private void CheckAllClientsReady()
    {
        // Wait for every connected client to hit 100%
        foreach (var done in clientsDone.Values)
            if (!done) return;

        // Everyone’s ready—tell all clients to activate
        ActivateSceneClientRpc();
    }

    [ClientRpc]
    private void ActivateSceneClientRpc(ClientRpcParams rpcParams = default)
    {
        activationAllowed = true;
    }

    /// <summary>
    /// Unity callback once a scene has fully loaded & activated.
    /// We'll use this on the HOST to spawn players into "GameScene",
    /// and on the SERVER to detect return to "Lobby".
    /// </summary>
    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Hide loading UI on every client as soon as scene is active
        loadingScreen.SetActive(false);

        if (scene.name == "GameScene" && IsHost)
        {
            // Host spawns one player object per client
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                GameObject go = Instantiate(playerPrefab);
                go.GetComponent<NetworkObject>()
                  .SpawnAsPlayerObject(clientId, true);
            }
        }

        if (scene.name == "Lobby" && IsServer)
        {
            // Reset done‑flags for next round
            foreach (var key in new List<ulong>(clientsDone.Keys))
                clientsDone[key] = false;

            // Now run your lobby‑return logic
            if (NetworkManager.Singleton.IsHost)
            {
                LobbyManager.instance.HostCreated();
                AddPlayersClientRpc();
            }
            else
            {
                LobbyManager.instance.ConnectedAsClient();
            }
        }
    }

    [ClientRpc]
    private void AddPlayersClientRpc(ClientRpcParams rpcParams = default)
    {
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(
            SteamClient.SteamId,
            SteamClient.Name,
            OwnerClientId);

        LobbyManager.instance.lobbyId.text =
            LobbySaver.instance.currentLobby?.Id.ToString();

        Cursor.lockState = CursorLockMode.Confined;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }
}
