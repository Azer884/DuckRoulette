using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;

public class NetworkTransmission : NetworkBehaviour
{
    public static NetworkTransmission instance;

    private void Awake()
    {
        if(instance != null)
        {
            Destroy(gameObject);
        }
        else
        {
            instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnServerClientDisconnected;
        }
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    private void OnServerClientDisconnected(ulong clientId)
    {
        if (!IsServer || GridManager.Instance == null)
            return;

        // Find and remove the player's character from the lobby grid
        NetworkObject playerNetworkObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        if (playerNetworkObject != null)
        {
            GridManager.Instance.RemoveCharacter(playerNetworkObject);
        }
    }

    private const int MaxChatLength = 200;
    private const int MaxSteamNameLength = 64;
    private const double ChatCooldownSeconds = 0.3;
    private readonly Dictionary<ulong, double> _lastChatTime = new();
    // clientId -> the lobby character the server spawned for them, so a repeated/forged
    // AddMeToDictionaryServerRPC can't spawn extra characters.
    private readonly Dictionary<ulong, NetworkObject> _lobbyCharacters = new();

    // _fromWho/isServer used to be trusted: any client could post as any other player, or as red
    // "Server" system text, with unbounded length and TMP rich-text tags. The author is the sender,
    // only the host may post system messages, and non-host chat is rate-limited.
    [ServerRpc(RequireOwnership = false)]
    public void IWishToSendAChatServerRPC(string _message, ulong _fromWho, bool isServer, ServerRpcParams serverRpcParams = default)
    {
        ulong sender = serverRpcParams.Receive.SenderClientId;
        bool fromHost = sender == NetworkManager.ServerClientId;
        string message = RpcValidation.SanitizeChatMessage(_message, MaxChatLength);
        if (message.Length == 0)
            return;

        if (!fromHost)
        {
            if (_lastChatTime.TryGetValue(sender, out double lastChat) &&
                !RpcValidation.IsCooldownElapsed(lastChat, Time.timeAsDouble, ChatCooldownSeconds))
                return;
            _lastChatTime[sender] = Time.timeAsDouble;
        }

        ChatFromServerClientRPC(message, sender, isServer && fromHost);
    }

    [ClientRpc]
    private void ChatFromServerClientRPC(string _message, ulong _fromWho, bool isServer)
    {
        LobbyManager.instance.SendMessageToChat(_message, _fromWho, isServer);
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddMeToDictionaryServerRPC(ulong _steamId, string _steamName, ulong _clientId, ServerRpcParams serverRpcParams = default)
    {
        // "Me" is always the sender: _clientId used to be trusted, letting any client spawn lobby
        // characters/list entries for (or on top of) other players, repeatedly. The name is
        // display text shown to everyone, so it is sanitized too. _steamId is still
        // caller-provided - see Docs/SecurityAudit.md residual risks.
        _clientId = serverRpcParams.Receive.SenderClientId;
        _steamName = RpcValidation.SanitizeChatMessage(_steamName, MaxSteamNameLength);
        if (_lobbyCharacters.TryGetValue(_clientId, out NetworkObject existingCharacter) &&
            existingCharacter != null && existingCharacter.IsSpawned)
        {
            return;
        }

        // Check for null instances
        if (LobbyManager.instance == null)
        {
            Debug.LogError("LobbyManager instance is null!");
            return;
        }
        
        if (GameNetworkManager.Instance == null || GameNetworkManager.Instance.playerObj == null)
        {
            Debug.LogError("GameNetworkManager Instance or playerObj is null!");
            return;
        }

        LobbyManager.instance.SendMessageToChat($"{_steamName} has joined", _clientId, true);
        _ = LobbyManager.instance.AddPlayerToDictionaryAsync(_clientId, _steamName, _steamId);
        LobbyManager.instance.UpdateClients();

        GameObject playerObj = Instantiate(GameNetworkManager.Instance.playerObj.gameObject);

        // Add the player's transform to the synchronized list
        if (playerObj.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.SpawnAsPlayerObject(_clientId, true);
            _lobbyCharacters[_clientId] = networkObject;

            if (GridManager.Instance != null)
            {
                GridManager.Instance.AddCharacter(networkObject);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestKickServerRpc(ulong targetClientId, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            return;

        if (!LobbyManager.instance.playerInfo.ContainsKey(targetClientId))
            return;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { targetClientId }
            }
        };

        YouWereKickedClientRPC(clientRpcParams);

        NetworkManager.Singleton.DisconnectClient(targetClientId, DisconnectNotice.KickedMessage);
    }

    [ClientRpc]
    private void YouWereKickedClientRPC(ClientRpcParams clientRpcParams = default)
    {
        // Not the player's choice: KickedByHost shows the error popup explaining it.
        GameNetworkManager.Instance.KickedByHost();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendPrivateChatServerRpc(string _message, ulong _toWho, ServerRpcParams serverRpcParams = default)
    {
        ulong _fromWho = serverRpcParams.Receive.SenderClientId;
        _message = RpcValidation.SanitizeChatMessage(_message, MaxChatLength);

        if (_message.Length == 0 || !LobbyManager.instance.playerInfo.ContainsKey(_toWho))
            return;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { _fromWho, _toWho }
            }
        };

        PrivateChatClientRPC(_message, _fromWho, _toWho, clientRpcParams);
    }

    [ClientRpc]
    private void PrivateChatClientRPC(string _message, ulong _fromWho, ulong _toWho, ClientRpcParams clientRpcParams = default)
    {
        LobbyManager.instance.ReceivePrivateMessage(_message, _fromWho, _toWho);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RemoveMeFromDictionaryServerRPC(ulong _steamId, ServerRpcParams serverRpcParams = default)
    {
        // Driven by Steam's lobby-member-left callback, which the host receives too - honour only
        // the host's report, so a client can't delete arbitrary players from everyone's list.
        if (serverRpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            return;

        RemovePlayerFromDictionaryClientRPC(_steamId);
    }

    [ClientRpc]
    private void RemovePlayerFromDictionaryClientRPC(ulong _steamId)
    {
        Debug.Log("removing client");
        LobbyManager.instance.RemovePlayerFromDictionary(_steamId);
    }

    [ClientRpc]
    public void UpdateClientsPlayerInfoClientRPC(ulong _steamId,string _steamName, ulong _clientId)
    {
        _ = LobbyManager.instance.AddPlayerToDictionaryAsync(_clientId, _steamName, _steamId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void IsTheClientReadyServerRPC(bool _ready, bool haveEoughCoins, ulong _clientId, ServerRpcParams serverRpcParams = default)
    {
        // A client may only set its own ready state (_clientId used to be trusted). haveEoughCoins
        // is still self-reported - coins are client-side Steam Cloud data.
        AClientMightBeReadyClientRPC(_ready, haveEoughCoins, serverRpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void AClientMightBeReadyClientRPC(bool _ready, bool haveEoughCoins, ulong _clientId)
    {
        foreach(KeyValuePair<ulong,GameObject> player in LobbyManager.instance.playerInfo)
        {
            if(player.Key == _clientId)
            {
                player.Value.GetComponent<PlayerInfo>().isReady = _ready;
                player.Value.GetComponent<PlayerInfo>().haveEoughCoins = haveEoughCoins;
                
                if (_ready)
                {
                    player.Value.GetComponent<PlayerInfo>().readyStatus.color = new Color(0.1686275f, 1f, 0.05098039f);
                }
                else
                {
                    player.Value.GetComponent<PlayerInfo>().readyStatus.color = new Color(1f, 0.2588235f, 0.03137255f);
                }

                
                if (NetworkManager.Singleton.IsHost)
                {
                    Debug.Log(LobbyManager.instance.CheckIfPlayersAreReady());
                }
            }
        }
    }

    [ServerRpc]
    public void StarGameFeeServerRpc()
    {
        StarGameFeeClientRpc();
    }

    [ClientRpc]
    public void StarGameFeeClientRpc()
    {
        Coin.Instance.UpdateCoinAmount(-5);
    }

    [ClientRpc]
    public void ChangeObjectPosClientRpc(ulong characterIndex, Vector3 pos, Quaternion rot)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(characterIndex, out NetworkObject character) && character != null)
        {
            character.transform.position = pos;
            character.transform.rotation = rot;
        }
    }
}
