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

    [ServerRpc(RequireOwnership = false)]
    public void IWishToSendAChatServerRPC(string _message, ulong _fromWho, bool isServer)
    {
        ChatFromServerClientRPC(_message, _fromWho, isServer);
    }

    [ClientRpc]
    private void ChatFromServerClientRPC(string _message, ulong _fromWho, bool isServer)
    {
        LobbyManager.instance.SendMessageToChat(_message, _fromWho, isServer);
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddMeToDictionaryServerRPC(ulong _steamId, string _steamName, ulong _clientId)
    {
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
            
            if (GridManager.Instance != null)
            {
                GridManager.Instance.AddCharacter(networkObject);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RemoveMeFromDictionaryServerRPC(ulong _steamId)
    {
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
    public void IsTheClientReadyServerRPC(bool _ready, bool haveEoughCoins, ulong _clientId)
    {
        AClientMightBeReadyClientRPC(_ready, haveEoughCoins, _clientId);
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

    [ServerRpc(RequireOwnership = false)]
    public void ChangeObjectPosServerRpc(ulong characterIndex, Vector3 pos, Quaternion rot)
    {
        ChangeObjectPosClientRpc(characterIndex, pos, rot);
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
