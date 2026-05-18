using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using Netcode.Transports.Facepunch;
using UnityEngine.SceneManagement;
using TMPro;
using System.Threading.Tasks;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Threading;

public class GameNetworkManager : MonoBehaviour
{
    public static GameNetworkManager Instance { get; private set; } = null;

    private FacepunchTransport transport = null;

    public List<Lobby> Lobbies { get; private set; } = new List<Lobby>(capacity: 100);

    public NetworkObject playerObj;
    private int mapIndex = 2;

    private readonly SemaphoreSlim actionLock = new SemaphoreSlim(1, 1);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();

        SteamMatchmaking.OnLobbyCreated += SteamMatchmaking_OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += SteamMatchmaking_OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += SteamMatchmaking_OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += SteamMatchmaking_OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyInvite += SteamMatchmaking_OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated += SteamMatchmaking_OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested += SteamFriends_OnGameLobbyJoinRequested;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SteamMatchmaking.OnLobbyCreated -= SteamMatchmaking_OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= SteamMatchmaking_OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= SteamMatchmaking_OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= SteamMatchmaking_OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyInvite -= SteamMatchmaking_OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated -= SteamMatchmaking_OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested -= SteamFriends_OnGameLobbyJoinRequested;
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnServerStarted -= Singleton_OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= Singleton_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
    }

    private void OnApplicationQuit()
    {
        Disconnected();
    }

    private async void SteamFriends_OnGameLobbyJoinRequested(Lobby _lobby, SteamId _steamId)
    {
        await PerformActionWithLock(async () =>
        {
            RoomEnter joinedLobby = await _lobby.Join();
            if (joinedLobby != RoomEnter.Success)
            {
                Debug.Log("Failed to join lobby.");
            }
            else
            {
                LobbySaver.instance.currentLobby = _lobby;
                LobbyManager.instance.ConnectedAsClient();
                Debug.Log("Joined Lobby.");
            }
        });
    }

    private void SteamMatchmaking_OnLobbyGameCreated(Lobby _lobby, uint _ip, ushort _port, SteamId _steamId)
    {
        Debug.Log("Lobby was created.");
        LobbyManager.instance.SendMessageToChat("Lobby was created", NetworkManager.Singleton.LocalClientId, true);
    }

    private void SteamMatchmaking_OnLobbyInvite(Friend _steamId, Lobby _lobby)
    {
        Debug.Log($"Invite from {_steamId.Name}");
    }

    private void SteamMatchmaking_OnLobbyMemberLeave(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("Member left.");
        LobbyManager.instance.SendMessageToChat($"{_steamId.Name} has left", _steamId.Id, true);
        NetworkTransmission.instance.RemoveMeFromDictionaryServerRPC(_steamId.Id);
    }

    private void SteamMatchmaking_OnLobbyMemberJoined(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("Member joined.");
    }

    private void SteamMatchmaking_OnLobbyEntered(Lobby _lobby)
    {
        Debug.Log("Client entered.");
        Debug.Log(_lobby.GetData("name"));

        if (NetworkManager.Singleton.IsHost)
        {
            return;
        }
        StartClient(_lobby.Owner.Id);
        LobbyManager.instance.lobbyId.text = _lobby.Id.ToString();
    }

    private void SteamMatchmaking_OnLobbyCreated(Result _result, Lobby _lobby)
    {
        if (_result != Result.OK)
        {
            Debug.Log("Lobby was not created.");
            return;
        }

        if (LobbyManager.instance.privateToggle.isOn)
        {
            _lobby.SetPrivate();
        }
        else if (LobbyManager.instance.friendToggle.isOn)
        {
            _lobby.SetFriendsOnly();
        }
        else
        {
            _lobby.SetPublic();
        }

        _lobby.SetJoinable(true);
        _lobby.SetGameServer(_lobby.Owner.Id);

        string randomLine = "";
        TextAsset textAsset = Resources.Load<TextAsset>("RandomLobbyNames");
        if (textAsset != null)
        {
            string[] lines = textAsset.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                int randomIndex = Random.Range(0, lines.Length);
                randomLine = lines[randomIndex];
            }
        }
        _lobby.SetData("name", $"{SteamClient.Name}{randomLine}");

        Debug.Log($"Lobby created: {SteamClient.Name}");
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, NetworkManager.Singleton.LocalClientId);
        LobbyManager.instance.lobbyId.text = _lobby.Id.ToString();
    }

    public async void StartHost(TMP_InputField _maxMembers)
    {
        await PerformActionWithLock(async () =>
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManager not found");
                return;
            }
            
            NetworkManager.Singleton.OnServerStarted += Singleton_OnServerStarted;
            NetworkManager.Singleton.StartHost();
            LobbySaver.instance.currentLobby = await SteamMatchmaking.CreateLobbyAsync(int.Parse(_maxMembers.text));
        });
    }

    public async void StartHost(int _maxMembers)
    {
        await PerformActionWithLock(async () =>
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManager not found");
                return;
            }
            
            NetworkManager.Singleton.OnServerStarted += Singleton_OnServerStarted;
            NetworkManager.Singleton.StartHost();
            LobbySaver.instance.currentLobby = await SteamMatchmaking.CreateLobbyAsync(_maxMembers);
        });
    }

    public async Task JoinByIdAsync(TMP_InputField input)
    {
        try
        {
            if (!ulong.TryParse(input.text, out ulong ID) || string.IsNullOrWhiteSpace(input.text))
                return;

            Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(ID);

            if (!lobby.HasValue)
            {
                Debug.Log("Lobby not found or inaccessible.");
                return;
            }

            RoomEnter result = await lobby.Value.Join();

            if (result != RoomEnter.Success)
            {
                Debug.Log("Failed to join lobby.");
            }
            else
            {
                LobbySaver.instance.currentLobby = lobby;
                LobbyManager.instance.ConnectedAsClient();
                Debug.Log("Joined Private Lobby.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    // Compatibility wrapper for UI callbacks
    public async void JoinById(TMP_InputField input)
    {
        await JoinByIdAsync(input);
    }

    public async Task JoinLobbyAsync(Lobby lobby)
    {
        try
        {
            RoomEnter joinedLobby = await lobby.Join();

            if (joinedLobby != RoomEnter.Success)
            {
                Debug.Log("Failed to join lobby.");
            }
            else
            {
                LobbySaver.instance.currentLobby = lobby;
                LobbyManager.instance.ConnectedAsClient();
                Debug.Log("Joined Lobby.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    // Compatibility wrapper for UI callbacks
    public async void JoinLobby(Lobby lobby)
    {
        await JoinLobbyAsync(lobby);
    }

    public void StartClient(SteamId _sId)
    {
        PerformActionWithLock(() =>
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManager not found");
                return;
            }
            
            if (transport == null)
            {
                Debug.LogError("FacepunchTransport not found");
                return;
            }
            
            NetworkManager.Singleton.OnClientConnectedCallback += Singleton_OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback += Singleton_OnClientDisconnectCallback;
            transport.targetSteamId = _sId;
            LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;

            if (NetworkManager.Singleton.StartClient())
            {
                Debug.Log("Client has started.");
            }
            else
            {
                Debug.LogError("Failed to start client");
            }
        });
    }

    public void Disconnected()
    {
        PerformActionWithLock(() =>
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown(true);
                if (NetworkManager.Singleton.IsHost)
                {
                    NetworkManager.Singleton.OnServerStarted -= Singleton_OnServerStarted;
                }

                NetworkManager.Singleton.OnClientConnectedCallback -= Singleton_OnClientConnectedCallback;
                NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
            }

            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.ClearChat();
                LobbyManager.instance.Disconnected();
            }
            Debug.Log("Disconnected.");

            if (LobbySaver.instance != null)
            {
                LobbySaver.instance.currentLobby?.Leave();
                LobbySaver.instance.currentLobby = null;
            }
        });
    }

    private async Task PerformActionWithLock(System.Func<Task> action)
    {
        if (!await actionLock.WaitAsync(0)) return;
        try
        {
            await action();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            actionLock.Release();
        }
    }

    private void PerformActionWithLock(System.Action action)
    {
        if (!actionLock.Wait(0)) return;
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            actionLock.Release();
        }
    }

    public void Singleton_OnClientDisconnectCallback(ulong _clientId)
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
        if (_clientId == 0)
        {
            Disconnected();
        }
    }

    public void Singleton_OnClientConnectedCallback(ulong _clientId)
    {
        if (NetworkTransmission.instance == null)
        {
            Debug.LogError("NetworkTransmission not found");
            return;
        }
        
        if (LobbyManager.instance == null)
        {
            Debug.LogError("LobbyManager not found");
            return;
        }
        
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, _clientId);
        LobbyManager.instance.myClientId = _clientId;
        
        bool hasEnoughCoins = Coin.Instance != null && Coin.Instance.amount >= 5;
        NetworkTransmission.instance.IsTheClientReadyServerRPC(false, hasEnoughCoins, _clientId);
        Debug.Log($"Client has connected: {SteamClient.Name}");

        if (LobbySaver.instance != null && LobbySaver.instance.currentLobby?.MemberCount >= 6)
        {
            LobbyManager.instance.friendList.SetTrigger("Hide");
        }
    }

    public void Singleton_OnServerStarted()
    {
        if (LobbyManager.instance == null)
        {
            Debug.LogError("LobbyManager not found");
            return;
        }
        
        LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        Debug.Log("Host started.");
        LobbyManager.instance.HostCreated();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        UpdateRichPresenceStatus(scene.name);
    }
    public void UpdateRichPresenceStatus(string SceneName)
    {
        string richPresenceKey = "steam_display";

        if (SceneName.Equals("GameScene"))
        {
            SteamFriends.SetRichPresence(richPresenceKey, "In-Game #Map1");
        }
        else if (SceneName.Contains("Lobby"))
        {
            SteamFriends.SetRichPresence(richPresenceKey, "In-Lobby");
        }
    }
    public void StartGame()
    {
        if (mapIndex < 0 || mapIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError($"mapIndex {mapIndex} is out of valid range [0, {SceneManager.sceneCountInBuildSettings - 1}]");
            return;
        }

        string scenePath = SceneUtility.GetScenePathByBuildIndex(mapIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        NetworkTransmission.instance.StarGameFeeServerRpc();
    }
    
    #region MapSelection
    public void ChooseMap(int index)
    {
        mapIndex = index;
    }
    public void ChooseRandomMap()
    {
        int totalScenes = SceneManager.sceneCountInBuildSettings;

        mapIndex = Random.Range(2, totalScenes);
    }
    #endregion

    public async Task<bool> RefreshLobbies(int maxResults = 20)
	{
		try
		{
			Lobbies.Clear();

		var lobbies = await SteamMatchmaking.LobbyList
        .FilterDistanceClose()
		.WithMaxResults(maxResults)
		.RequestAsync();

		if (lobbies != null)
		{
			for (int i = 0; i < lobbies.Length; i++)
            {
				if (!string.IsNullOrWhiteSpace(lobbies[i].GetData("name")))
                {
                    Lobbies.Add(lobbies[i]);
                }
            }
		}

		return true;
		}
		catch (System.Exception ex)
		{
			Debug.Log("Error fetching lobbies", this);
			Debug.LogException(ex, this);
			return false;
		}
	}
    public async void LobbiesListAsync()
    {
        await RefreshLobbies();
        foreach (Transform child in LobbyManager.instance.lobbiesBox.transform)
        {
            Destroy(child.gameObject);
        }

        foreach (Lobby lobby in Lobbies)
        {
            lobby.Refresh();
            GameObject lobbyObj = Instantiate(LobbyManager.instance.lobbiesObj, LobbyManager.instance.lobbiesBox.transform);

            lobbyObj.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = lobby.GetData("name");
            lobbyObj.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = lobby.MemberCount + "/" + lobby.MaxMembers;
            Button joinButton = lobbyObj.GetComponent<Button>();

            var capturedLobby = lobby; // capture loop variable to avoid closure issues
            joinButton.onClick.AddListener(() => JoinLobby(capturedLobby));
        }
    }

    public async void RandomJoin()
    {
        await PerformActionWithLock(async () =>
        {
            try
            {
                if (Lobbies.Count == 0)
                {
                    Debug.Log("No available lobbies to join.");
                    return;
                }

                int randomIndex = Random.Range(0, Lobbies.Count);
                RoomEnter result = await Lobbies[randomIndex].Join();

                if (result != RoomEnter.Success)
                {
                    Debug.Log("Failed to join random lobby.");
                }
                else
                {
                    if (LobbyManager.instance == null)
                    {
                        Debug.LogError("LobbyManager not found");
                        return;
                    }
                    LobbyManager.instance.ConnectedAsClient();
                    Debug.Log("Joined Random Lobby");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        });
    }
}
