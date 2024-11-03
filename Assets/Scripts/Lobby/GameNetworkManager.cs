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
using System.IO;

public class GameNetworkManager : MonoBehaviour
{
    public static GameNetworkManager Instance { get; private set; } = null;

    private FacepunchTransport transport = null;


    public List<Lobby> Lobbies { get; private set; } = new List<Lobby>(capacity: 100);

    public ulong hostId;
    private int mapIndex = 2;
    private readonly string RandomServerNamePath = Path.Combine(Application.dataPath, "RandomLobbyNames.txt");

    private void Awake()
    {
        if(Instance == null)
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
        transport = GetComponent<FacepunchTransport>();

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

        if(NetworkManager.Singleton == null)
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

    //when you accept the invite or Join on a friend
    private async void SteamFriends_OnGameLobbyJoinRequested(Lobby _lobby, SteamId _steamId)
    {
        RoomEnter joinedLobby = await _lobby.Join();
        if(joinedLobby != RoomEnter.Success)
        {
            Debug.Log("Failed to create lobby");
        }
        else
        {
            LobbySaver.instance.currentLobby = _lobby;
            LobbyManager.instance.ConnectedAsClient();
            Debug.Log("Joined Lobby");
        }
    }

    private void SteamMatchmaking_OnLobbyGameCreated(Lobby _lobby, uint _ip, ushort _port, SteamId _steamId)
    {
        Debug.Log("Lobby was created");
        LobbyManager.instance.SendMessageToChat($"Lobby was created", NetworkManager.Singleton.LocalClientId, true);
    }

    //friend send you an steam invite
    private void SteamMatchmaking_OnLobbyInvite(Friend _steamId, Lobby _lobby)
    {
        Debug.Log($"Invite from {_steamId.Name}");
    }

    private void SteamMatchmaking_OnLobbyMemberLeave(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("member leave");
        LobbyManager.instance.SendMessageToChat($"{_steamId.Name} has left", _steamId.Id, true);
        NetworkTransmission.instance.RemoveMeFromDictionaryServerRPC(_steamId.Id);
    }

    private void SteamMatchmaking_OnLobbyMemberJoined(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("member join");
    }

    private void SteamMatchmaking_OnLobbyEntered(Lobby _lobby)
    {
        if (NetworkManager.Singleton.IsHost)
        {
            return;
        }
        StartClient(LobbySaver.instance.currentLobby.Value.Owner.Id);
        LobbyManager.instance.lobbyId.text = _lobby.Id.ToString();
    }

    private void SteamMatchmaking_OnLobbyCreated(Result _result, Lobby _lobby)
    {
        if(_result != Result.OK)
        {
            Debug.Log("lobby was not created");
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
        if (File.Exists(RandomServerNamePath))
        {
            string[] lines = File.ReadAllLines(RandomServerNamePath);

            if (lines.Length > 0)
            {
                int randomIndex = Random.Range(0, lines.Length);
                randomLine = lines[randomIndex];
            }
        } 
        _lobby.SetData("name", $"{SteamClient.Name}{randomLine}");

        Debug.Log($"lobby created {SteamClient.Name}");
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, NetworkManager.Singleton.LocalClientId); //
        LobbyManager.instance.lobbyId.text = _lobby.Id.ToString();
    }

    public async void StartHost(TMP_InputField _maxMembers)
    {
        NetworkManager.Singleton.OnServerStarted += Singleton_OnServerStarted;
        NetworkManager.Singleton.StartHost();
        LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        LobbySaver.instance.currentLobby = await SteamMatchmaking.CreateLobbyAsync(int.Parse(_maxMembers.text));
    }

    public async void JoinById(TMP_InputField input)
    {
        if (!ulong.TryParse(input.text, out ulong ID))
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
            Debug.Log("Joined Private Lobby");
        }
    }

    private async void JoinLobby(Lobby lobby)
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
            Debug.Log("Joined Lobby");
            return;
        }
    }

    public void StartClient(SteamId _sId)
    {
        NetworkManager.Singleton.OnClientConnectedCallback += Singleton_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += Singleton_OnClientDisconnectCallback;
        transport.targetSteamId = _sId;
        LobbyManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Client has started");
        }
    }

    public void Disconnected()
    {
        LobbySaver.instance.currentLobby?.Leave();
        if(NetworkManager.Singleton == null)
        {
            return;
        }
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.OnServerStarted -= Singleton_OnServerStarted;
        }
        else
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= Singleton_OnClientConnectedCallback;
        }
        NetworkManager.Singleton.Shutdown(true);
        if (LobbyManager.instance != null)
        {
            LobbyManager.instance.ClearChat();
            LobbyManager.instance.Disconnected(); 
        }
        Debug.Log("disconnected");
    }

    private void Singleton_OnClientDisconnectCallback(ulong _cliendId)
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
        if(_cliendId == 0)
        {
            Disconnected();
        }
    }

    private void Singleton_OnClientConnectedCallback(ulong _cliendId)
    {
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, _cliendId);
        LobbyManager.instance.myClientId = _cliendId;
        NetworkTransmission.instance.IsTheClientReadyServerRPC(false, Coin.Instance.amount >= 5, _cliendId);
        Debug.Log($"Client has connected : {SteamClient.Name}");
    }

    private void Singleton_OnServerStarted()
    {
        Debug.Log("Host started");
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
            Button joinButton = lobbyObj.transform.GetChild(2).GetComponent<Button>();

            joinButton.onClick.AddListener(() => JoinLobby(lobby));
        }
    }

    public async void RandomJoin()
    {
        // Check if there are any valid lobbies available
        if (Lobbies.Count == 0)
        {
            Debug.Log("No available lobbies with an owner name to join.");
            return;
        }

        // Select a random lobby index from the filtered list
        int randomIndex = Random.Range(0, Lobbies.Count);

        // Try to join the randomly selected lobby
        RoomEnter result = await Lobbies[randomIndex].Join();

        // Check if the join attempt was successful
        if (result != RoomEnter.Success)
        {
            Debug.Log("Failed to join lobby.");
        }
        else
        {
            LobbyManager.instance.ConnectedAsClient();
            Debug.Log("Joined Lobby");
        }
    }

}
