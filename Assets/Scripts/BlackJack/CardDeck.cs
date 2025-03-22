using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class CardDeck : NetworkBehaviour
{
    public static CardDeck instance; // Singleton instance
    public List<Card> cardDeck; // List to store all card objects
    public Dictionary<Card, int> cardDictionary;
    public Dictionary<ulong, int> playerInGameList = new();
    public Dictionary<ulong, (bool, int)> playerInCurrentGameList = new();

    public NetworkVariable<ulong> playerTurn = new(0);

    public GameObject cardPrefab;
    public float moveDuration = 0.5f;
    public AnimationCurve movementCurve;
    private List<NetworkObject> spawnedCards = new();
    public GameObject message, messageHolder;
    public TMPro.TextMeshProUGUI turnsText;

    void Awake()
    {
        if(instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(this);
        }
        PopulateDictionary();

        
    }
    public override void OnNetworkSpawn()
    {
        turnsText.text = $"Turn: {GameManager.Instance.GetPlayerNickname(playerTurn.Value)}";
    }
    public void EnterGame(ulong clientId)
    {
        EnterGameServerRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EnterGameServerRpc(ulong clientId)
    {
        if (!playerInCurrentGameList.ContainsKey(clientId))
        {
            playerInGameList.Add(clientId, 0);
            playerInCurrentGameList.Add(clientId, (false, 0));
            DrawFirstCardServerRpc(clientId);

            SendMsgServerRpc($"{GameManager.Instance.GetPlayerNickname(clientId)} joined the game");
        }
    }
    public void ExitGame(ulong clientId)
    {
        if (playerInCurrentGameList.ContainsKey(clientId))
        {
            playerInCurrentGameList.Remove(clientId);
        }
        playerInGameList.Remove(clientId);
    }

    public Card GetRandomCard()
    {
        int randomIndex = Random.Range(0, cardDeck.Count);
        if (!DeckIsEmpty(cardDictionary))
        {
            while (cardDictionary[cardDeck[randomIndex]] == 0)
            {
                randomIndex = Random.Range(0, cardDeck.Count);
            }
            return cardDeck[randomIndex];
        }
        return null;
    }

    private void PopulateDictionary()
    {
        cardDictionary = new Dictionary<Card, int>();
        for (int i = 0; i < cardDeck.Count; i++)
        {
            cardDictionary.Add(cardDeck[i], 4);
        }
    }

    private bool DeckIsEmpty(Dictionary<Card, int> cardDictionary)
    {
        foreach (KeyValuePair<Card, int> card in cardDictionary)
        {
            if (card.Value > 0)
            {
                return false;
            }
        }
        return true;
    }
    public void RestartDeck()
    {
        foreach (Card card in cardDeck)
        {
            cardDictionary[card] = 4;
        }
    }

    public IEnumerator MoveCard(Transform cardTransform, Vector3 end)
    {

        float elapsedTime = 0f;
        while (elapsedTime < moveDuration)
        {
            float t = elapsedTime / moveDuration;
            float curvedT = movementCurve.Evaluate(t); // Apply curve
            cardTransform.position = Vector3.Lerp(transform.position, end, curvedT);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        cardTransform.position = end; // Ensure it snaps to the final position
    }

    [ServerRpc (RequireOwnership = false)]
    public void SpawnCardServerRpc(ulong clientId, Vector3 targetedPosition, Quaternion rot , int cardIndex, bool IsFirstCard = false)
    {
        Debug.Log($"Spawning card {cardIndex}");
        GameObject newCardObject = Instantiate(cardPrefab, transform.position, rot);

        NetworkObject networkObject = newCardObject.GetComponent<NetworkObject>();
        networkObject.SpawnWithOwnership(clientId);

        spawnedCards.Add(networkObject);

        ClientRpcParams onlyPlayerParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };
        ClientRpcParams othersParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds
                    .Where(id => id != clientId) // Exclude the specified clientId
                    .ToArray()
            }
        };

        int artworkIndex = Random.Range(0, cardDeck[cardIndex - 1].artworks.Length);
        SpawnCardClientRpc(newCardObject.GetComponent<NetworkObject>().NetworkObjectId, cardIndex, artworkIndex, targetedPosition, true, onlyPlayerParams);
        SpawnCardClientRpc(newCardObject.GetComponent<NetworkObject>().NetworkObjectId, cardIndex, artworkIndex, targetedPosition, IsFirstCard, othersParams);
        
    }

    [ServerRpc(RequireOwnership = false)]
    private void DestroyCardsServerRpc()
    {
        foreach (NetworkObject card in spawnedCards)
        {
            card.Despawn(true);
            Destroy(card.gameObject);
        }
        spawnedCards.Clear();
    }

    [ClientRpc]
    public void SpawnCardClientRpc(ulong networkObjectId, int cardIndex, int artworkIndex, Vector3 targetedPos, bool IsFirstCard = false, ClientRpcParams clientRpcParams = default)
    {
        StopAllCoroutines();

        NetworkObject networkObject = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        networkObject.GetComponent<MeshFilter>().mesh = cardDeck[cardIndex - 1].artworks[artworkIndex];

        if (!IsFirstCard)
        {
            networkObject.transform.Rotate(0, 0, 180); // Flip the card
        }
        

        StartCoroutine(MoveCard(networkObject.transform, targetedPos));
    }

    [ServerRpc(RequireOwnership = false)]
    public void ResetGameServerRpc()
    {
        RestartDeck();
        DestroyCardsServerRpc();
        ResetHandsClientRpc();
        playerTurn.Value = 0;

        List<ulong> keys = new(playerInCurrentGameList.Keys);
        foreach (ulong key in keys)
        {
            playerInCurrentGameList[key] = (false, 0);
        }
    }


    [ServerRpc(RequireOwnership = false)]
    public void GetNextPlayerServerRpc()
    {
        playerTurn.Value = (playerTurn.Value + 1) % (ulong)playerInGameList.Count;
        if(playerInCurrentGameList.Values.Any(value => value.Item1 == false))
        {
            if (playerInCurrentGameList[playerTurn.Value].Item1)
            {
                GetNextPlayerServerRpc();
            }
        }
        else
        {
            CheckIfAllPlayersDoneServerRpc();
        }

        turnsText.text = $"Turn: {GameManager.Instance.GetPlayerNickname(playerTurn.Value)}";
    }

    [ServerRpc(RequireOwnership = false)]
    public void CheckIfAllPlayersDoneServerRpc()
    {
        if (playerInCurrentGameList.Values.Any(value => value.Item1 == false))
        {
            return;
        }
        else
        {

            List<ulong> winners = new();
            int highestScore = 0;
            foreach (KeyValuePair<ulong, (bool, int)> player in playerInCurrentGameList)
            {
                if (player.Value.Item2 > highestScore && player.Value.Item2 <= 21)
                {
                    highestScore = player.Value.Item2;
                    winners.Clear();
                    winners.Add(player.Key);
                }
                else if (player.Value.Item2 == highestScore)
                {
                    winners.Add(player.Key);
                }
            }
            if (winners.Count > 0)
            {
                string winnerString = winners.Count > 1 ? "Winners are" : "Winner is";
                SendMsgServerRpc($"{winnerString} : " + string.Join(", ", winners.Select(winner => GameManager.Instance.GetPlayerNickname(winner)).ToArray()));
                foreach (ulong winner in winners)
                {
                    playerInGameList[winner]++;
                }
            }
            else
            {
                SendMsgServerRpc("No winners this round");
            }

            ResetGameServerRpc();
        }
    }

    [ClientRpc]
    private void ResetHandsClientRpc()
    {
        NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<BlackJack>().RestartHand();
    }

    [ServerRpc(RequireOwnership = false)]
    public void DrawFirstCardServerRpc(ulong clientId)
    {
        ClientRpcParams clientRpcParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };
        DrawFirstCardClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void DrawFirstCardClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<BlackJack>().drawnFirstCard)
        {
            NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<BlackJack>().DrawCard(true);
        }
        NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<BlackJack>().drawnFirstCard = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendMsgServerRpc(string msgToSend)
    {
        SendMsgClientRpc(msgToSend);
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void SendMsgServerRpc(string msgToSend, ulong clientId)
    {
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
        SendMsgClientRpc(msgToSend, clientRpcParams);
    }

    [ClientRpc]
    public void SendMsgClientRpc(string msgToSend, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log(msgToSend);
        GameObject msg = Instantiate(message, messageHolder.transform);
        msg.GetComponent<TMPro.TextMeshProUGUI>().text = msgToSend;
        Destroy(msg, 3f);
    }
}
