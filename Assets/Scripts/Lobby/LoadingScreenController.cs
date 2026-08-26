using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Random = UnityEngine.Random;

[RequireComponent(typeof(NetworkObject))]
public class LoadingScreenController : NetworkBehaviour
{
    public struct ProgressEntry : INetworkSerializable, IEquatable<ProgressEntry>
    {
        public ulong ClientId;
        public float Progress;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Progress);
        }

        public bool Equals(ProgressEntry other) => ClientId == other.ClientId && Progress.Equals(other.Progress);
    }

    [Header("My Progress")]
    public Slider myProgressBar;
    public TextMeshProUGUI myPercentText;

    [Header("Other Players")]
    public Transform otherPlayersContainer;
    public GameObject otherPlayerBarTemplate;

    [Header("Bottom")]
    public TextMeshProUGUI tipText;
    public string[] tips;

    private readonly NetworkList<ProgressEntry> progress = new NetworkList<ProgressEntry>();
    // GameNetworkManager.PendingGameSceneName only exists locally on whichever peer called
    // StartGame() (the host) - every other client needs the target scene name too, or their
    // OnLoad below never recognizes it, never holds/reports their load, and the host then
    // waits forever at 90% for a progress report that never arrives - so the player never spawns.
    private readonly NetworkVariable<FixedString64Bytes> pendingSceneName = new();
    private readonly Dictionary<ulong, Slider> otherBars = new();
    private AsyncOperation pendingOp;
    private bool activationSent;

    private void Awake()
    {
        if (tipText != null && tips != null && tips.Length > 0)
        {
            tipText.text = tips[Random.Range(0, tips.Length)];
        }

        if (otherPlayerBarTemplate != null)
        {
            otherPlayerBarTemplate.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.Singleton.SceneManager.OnLoad += OnSceneLoadBegin;

        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadingScreenReached;
            pendingSceneName.Value = GameNetworkManager.Instance.PendingGameSceneName;

            progress.Clear();
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                progress.Add(new ProgressEntry { ClientId = id, Progress = 0f });
            }
        }

        progress.OnListChanged += OnProgressChanged;
        RefreshAllBars();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoad -= OnSceneLoadBegin;
            if (IsServer)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadingScreenReached;
            }
        }

        progress.OnListChanged -= OnProgressChanged;
    }

    // Server only: everyone has reached the loading screen itself, now kick off the real load of the game scene.
    private void OnLoadingScreenReached(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName != "LoadingScreen") return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadingScreenReached;
        NetworkManager.Singleton.SceneManager.LoadScene(GameNetworkManager.Instance.PendingGameSceneName, LoadSceneMode.Single);
    }

    // Every peer: intercept its own load of the game scene and hold it just before activation.
    private void OnSceneLoadBegin(ulong clientId, string sceneName, LoadSceneMode mode, AsyncOperation asyncOperation)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId) return;
        if (sceneName != pendingSceneName.Value.ToString()) return;

        pendingOp = asyncOperation;
        pendingOp.allowSceneActivation = false;
        StartCoroutine(TrackMyProgress());
    }

    private IEnumerator TrackMyProgress()
    {
        while (pendingOp != null && !pendingOp.isDone)
        {
            float p = Mathf.Clamp01(pendingOp.progress / 0.9f);
            if (myProgressBar != null) myProgressBar.value = p;
            if (myPercentText != null) myPercentText.text = $"{Mathf.RoundToInt(p * 100)}%";

            ReportProgressServerRpc(p);

            if (p >= 1f) yield break;
            yield return null;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportProgressServerRpc(float value, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        for (int i = 0; i < progress.Count; i++)
        {
            if (progress[i].ClientId == clientId)
            {
                progress[i] = new ProgressEntry { ClientId = clientId, Progress = value };
                break;
            }
        }

        CheckEveryoneReady();
    }

    private void CheckEveryoneReady()
    {
        if (activationSent) return;

        foreach (ProgressEntry entry in progress)
        {
            if (entry.Progress < 1f) return;
        }

        activationSent = true;
        ActivateSceneClientRpc();
    }

    [ClientRpc]
    private void ActivateSceneClientRpc(ClientRpcParams rpcParams = default)
    {
        if (pendingOp != null)
        {
            pendingOp.allowSceneActivation = true;
        }
    }

    private void OnProgressChanged(NetworkListEvent<ProgressEntry> change)
    {
        RefreshAllBars();
    }

    private void RefreshAllBars()
    {
        if (NetworkManager.Singleton == null) return;
        ulong myId = NetworkManager.Singleton.LocalClientId;

        foreach (ProgressEntry entry in progress)
        {
            if (entry.ClientId == myId) continue;

            if (!otherBars.TryGetValue(entry.ClientId, out Slider bar))
            {
                if (otherPlayerBarTemplate == null || otherPlayersContainer == null) continue;

                GameObject row = Instantiate(otherPlayerBarTemplate, otherPlayersContainer);
                row.SetActive(true);
                bar = row.GetComponentInChildren<Slider>();
                otherBars[entry.ClientId] = bar;

                TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null && LobbyManager.instance != null &&
                    LobbyManager.instance.playerInfo.TryGetValue(entry.ClientId, out GameObject playerInfoObj))
                {
                    label.text = playerInfoObj.GetComponent<PlayerInfo>().steamName;
                }
            }

            if (bar != null)
            {
                bar.value = entry.Progress;
            }
        }
    }
}
