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

    // Tracks which players have already consumed their one legitimate isFirstCard=true draw
    // (the automatic deal triggered by EnterGameServerRpc) - isFirstCard is otherwise a
    // client-supplied bool with no other server-side guard, so without this a modified client
    // could keep claiming isFirstCard=true to skip the turn check indefinitely.
    private readonly HashSet<ulong> _hasDrawnFirstCard = new();

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
        // Safety checks before accessing components
        if (turnsText == null)
        {
            Debug.LogError("CardDeck: turnsText is not assigned!");
        }
        else if (!TrySetTurnText())
        {
            turnsText.text = "Turn: Unknown";
            StartCoroutine(WaitForGameManagerAndRefreshTurnText());
        }
    }
    public void EnterGame(ulong clientId)
    {
        EnterGameServerRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EnterGameServerRpc(ulong clientId)
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("CardDeck: GameManager.Instance is not ready yet.");
            return;
        }

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
        _hasDrawnFirstCard.Remove(clientId);
    }

    // Server-authoritative draw: picks the card, decrements the real deck count, and tracks the
    // player's running hand sum here (not on the client's own BlackJack.handSum field, which
    // never updates on the server for anyone but the host - see RequestDrawCardServerRpc).
    [ServerRpc(RequireOwnership = false)]
    public void RequestDrawCardServerRpc(bool isFirstCard, ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;

        if (!playerInCurrentGameList.TryGetValue(clientId, out var entry))
        {
            return;
        }

        bool grantsFirstCardBypass = isFirstCard && !_hasDrawnFirstCard.Contains(clientId);
        if (grantsFirstCardBypass)
        {
            _hasDrawnFirstCard.Add(clientId);
        }

        if (!grantsFirstCardBypass && playerTurn.Value != clientId)
        {
            SendMsgServerRpc("It's not your turn!", clientId);
            return;
        }

        ClientRpcParams targetParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        Card newCard = GetRandomCard();
        if (newCard == null)
        {
            SendMsgServerRpc("Deck is empty!");
            DealCardClientRpc(0, entry.Item2, true, isFirstCard, targetParams);
            return;
        }

        cardDictionary[newCard]--;

        int newSum = entry.Item2 + newCard.cardValue;
        playerInCurrentGameList[clientId] = (entry.Item1, newSum);

        if (!grantsFirstCardBypass)
        {
            GetNextPlayerServerRpc();
        }

        int cardListIndex = cardDeck.IndexOf(newCard);
        DealCardClientRpc(cardListIndex, newSum, false, isFirstCard, targetParams);
    }

    [ClientRpc]
    private void DealCardClientRpc(int cardListIndex, int newHandSum, bool deckEmpty, bool isFirstCard, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out BlackJack blackJack))
        {
            blackJack.ReceiveDealtCard(cardListIndex, newHandSum, deckEmpty, isFirstCard);
        }
    }

    // Only accepts the win if the server's own tracked hand sum for this player actually hit 21 -
    // a client can no longer just self-report a blackjack.
    [ServerRpc(RequireOwnership = false)]
    public void BlackjackServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;

        if (!playerInCurrentGameList.TryGetValue(clientId, out var entry) || entry.Item2 != 21)
        {
            return;
        }

        if (playerInGameList.ContainsKey(clientId))
        {
            playerInGameList[clientId]++;
        }

        SendMsgServerRpc($"{(GameManager.Instance != null ? GameManager.Instance.GetPlayerNickname(clientId) : clientId.ToString())} got a Blackjack!");
        ResetGameServerRpc();
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

    private bool DeckIsEmpty(Dictionary<Card, int> deckCounts)
    {
        foreach (KeyValuePair<Card, int> card in deckCounts)
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
    public void SpawnCardServerRpc(ulong clientId, Vector3 targetedPosition, Quaternion rot , int cardIndex, bool isFirstCard = false, ServerRpcParams serverRpcParams = default)
    {
        // A client can only ever legitimately spawn its own dealt card, and cardIndex must be a
        // valid cardDeck slot - both are otherwise unvalidated client-supplied values.
        if (clientId != serverRpcParams.Receive.SenderClientId || cardIndex < 0 || cardIndex >= cardDeck.Count)
        {
            return;
        }

        //Major error (can't use int in a string): Debug.Log($"Spawning card {cardIndex}");
        GameObject newCardObject = Instantiate(cardPrefab, transform.position, rot);

        NetworkObject networkObject = newCardObject.GetComponent<NetworkObject>();
        networkObject.SpawnWithOwnership(clientId);

        spawnedCards.Add(networkObject);

        ClientRpcParams onlyPlayerParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
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

        int artworkIndex = Random.Range(0, cardDeck[cardIndex].artworks.Length);
        SpawnCardClientRpc(newCardObject.GetComponent<NetworkObject>().NetworkObjectId, cardIndex, artworkIndex, targetedPosition, true, onlyPlayerParams);
        SpawnCardClientRpc(newCardObject.GetComponent<NetworkObject>().NetworkObjectId, cardIndex, artworkIndex, targetedPosition, isFirstCard, othersParams);
        
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
    public void SpawnCardClientRpc(ulong networkObjectId, int cardIndex, int artworkIndex, Vector3 targetedPos, bool isFirstCard = false, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;

        NetworkObject networkObject = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        networkObject.GetComponent<MeshFilter>().mesh = cardDeck[cardIndex].artworks[artworkIndex];

        if (!isFirstCard)
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
        if (playerInGameList.Count == 0)
        {
            return;
        }

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

        TrySetTurnText();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CheckIfAllPlayersDoneServerRpc()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("CardDeck: GameManager.Instance is not ready yet.");
        }
        else if (playerInCurrentGameList.Values.Any(value => value.Item1 == false))
        {
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
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out BlackJack blackJack))
        {
            blackJack.RestartHand();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void DrawFirstCardServerRpc(ulong clientId)
    {
        ClientRpcParams clientRpcParams = new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
        DrawFirstCardClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void DrawFirstCardClientRpc(ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null)
        {
            return;
        }

        var blackJack = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<BlackJack>();
        if (blackJack == null)
        {
            return;
        }

        if (!blackJack.drawnFirstCard)
        {
            blackJack.DrawCard(true);
        }
        blackJack.drawnFirstCard = true;
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
        _ = clientRpcParams;
        Debug.Log(msgToSend);
        if (message == null || messageHolder == null)
        {
            return;
        }

        GameObject msg = Instantiate(message, messageHolder.transform);
        msg.GetComponent<TMPro.TextMeshProUGUI>().text = msgToSend;
        Destroy(msg, 3f);
    }

    private bool TrySetTurnText()
    {
        if (turnsText == null || GameManager.Instance == null)
        {
            return false;
        }

        turnsText.text = $"Turn: {GameManager.Instance.GetPlayerNickname(playerTurn.Value)}";
        return true;
    }

    private IEnumerator WaitForGameManagerAndRefreshTurnText()
    {
        while (GameManager.Instance == null)
        {
            yield return null;
        }

        TrySetTurnText();
    }
}
