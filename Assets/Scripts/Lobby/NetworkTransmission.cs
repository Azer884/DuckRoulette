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
    public void AddMeToDictionaryServerRPC(ulong _steamId,string _steamName, ulong _clientId)
    {
        LobbyManager.instance.SendMessageToChat($"{_steamName} has joined", _clientId, true);
        _ = LobbyManager.instance.AddPlayerToDictionaryAsync(_clientId, _steamName, _steamId);
        LobbyManager.instance.UpdateClients();
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
                    player.Value.GetComponent<PlayerInfo>().playerName.color = new Color(0, .5f, 0);
                }
                else
                {
                    player.Value.GetComponent<PlayerInfo>().playerName.color = new Color(.5f, 0, 0);
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
    public void KickAllPlayersClientRpc()
    {
        NetworkManager.Singleton.Shutdown(true);
        if (LobbyManager.instance != null)
        {
            LobbyManager.instance.ClearChat();
            LobbyManager.instance.Disconnected(); 
        }
        Debug.Log("disconnected");
    }
}
