using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

// The blackjack table's dealer. Server-authoritative: the server owns the shoe, every hand sum,
// the seating order and whose turn it is; clients only ever ask.
//
// House rules here are the simple ones this deck supports - the card assets are 1..10 with no
// face cards and no aces, so a hand is a plain sum and there is no soft/hard distinction. Each
// turn a seated player draws exactly one card, then the turn passes. Bust over 21, exactly 21 is
// an instant win, otherwise the highest standing hand takes the round.
public class CardDeck : NetworkBehaviour
{
    // playerTurn parks here whenever nobody is seated. ulong.MaxValue is never a real client id.
    public const ulong NoPlayer = ulong.MaxValue;

    public static CardDeck instance; // Singleton instance
    public List<Card> cardDeck; // List to store all card objects
    public Dictionary<Card, int> cardDictionary;
    public Dictionary<ulong, int> playerInGameList = new();
    public Dictionary<ulong, (bool, int)> playerInCurrentGameList = new();

    public NetworkVariable<ulong> playerTurn = new(NoPlayer);

    // Seating order, server-side. The turn used to be advanced as
    // `(playerTurn + 1) % playerInGameList.Count` and then used to index playerInCurrentGameList -
    // mixing up "position in the rotation" with "client id". With clients 0 and 2 seated that
    // rotates 0,1,0,1 and the lookup for id 1 threw KeyNotFoundException on the very first pass,
    // which is what made the table unplayable outside a host-only test. The rotation is an index
    // into this list; playerTurn always holds a real client id.
    private readonly List<ulong> _seatOrder = new();

    public GameObject cardPrefab;
    public float moveDuration = 0.5f;
    public AnimationCurve movementCurve;

    [Tooltip("Sideways gap between cards in a player's hand, in table-local units.")]
    public float cardSpacing = .1f;

    private List<NetworkObject> spawnedCards = new();
    public GameObject message, messageHolder;
    public TMPro.TextMeshProUGUI turnsText;

    [Tooltip("Optional: the group task completed by finishing a round at this table. Only counts " +
        "when at least three players are seated - see Challenge.TaskType.ThreePlus.")]
    public Challenge blackjackTask;

    [Tooltip("How many players must be seated for a finished round to count as the group task.")]
    public int taskMinimumPlayers = 3;

    void Awake()
    {
        if(instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(this);
            return;
        }
        PopulateDictionary();
    }

    private void OnEnable()
    {
        TaskManager.RegisterObjective(blackjackTask);
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(blackjackTask);
    }

    public override void OnNetworkSpawn()
    {
        playerTurn.OnValueChanged += OnPlayerTurnChanged;

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

    public override void OnNetworkDespawn()
    {
        playerTurn.OnValueChanged -= OnPlayerTurnChanged;
    }

    // The turn text used to only be refreshed inside GetNextPlayer on the server, so remote
    // clients never saw it change. It rides the NetworkVariable now, which every client gets.
    private void OnPlayerTurnChanged(ulong previous, ulong current)
    {
        TrySetTurnText();
    }

    #region Seating

    public void EnterGame()
    {
        EnterGameServerRpc();
    }

    // The seat is claimed for whoever actually sent this. It used to take a clientId parameter
    // straight off the wire, so any client could seat (or re-seat) somebody else.
    [ServerRpc(RequireOwnership = false)]
    public void EnterGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("CardDeck: GameManager.Instance is not ready yet.");
            return;
        }

        if (playerInCurrentGameList.ContainsKey(clientId))
        {
            return;
        }

        int seatCount = BlackjackTable.Instance != null ? BlackjackTable.Instance.SeatCount : 0;
        if (seatCount == 0)
        {
            Debug.LogWarning("CardDeck: the table has no seats, nobody can sit down.");
            return;
        }

        if (_seatOrder.Count >= seatCount)
        {
            SendMsgServerRpc("The table is full!", clientId);
            return;
        }

        // Seat index is the first free chair, not the client id - client id 4 on a four-seat
        // table used to walk off the end of the table's children and grab the deck or the canvas.
        int seatIndex = FirstFreeSeat(seatCount);
        _seatOrder.Add(clientId);
        playerInGameList.TryAdd(clientId, 0);
        playerInCurrentGameList.Add(clientId, (false, 0));
        _seatByClient[clientId] = seatIndex;

        AssignSeatClientRpc(seatIndex, ToClient(clientId));
        SendMsgServerRpc($"{GameManager.Instance.GetPlayerNickname(clientId)} joined the game");

        // First seat of a fresh round takes the turn, otherwise the round in progress carries on.
        if (playerTurn.Value == NoPlayer || !playerInCurrentGameList.ContainsKey(playerTurn.Value))
        {
            playerTurn.Value = clientId;
        }

        DealCard(clientId, true);
    }

    private readonly Dictionary<ulong, int> _seatByClient = new();

    private int FirstFreeSeat(int seatCount)
    {
        for (int seat = 0; seat < seatCount; seat++)
        {
            if (!_seatByClient.ContainsValue(seat))
            {
                return seat;
            }
        }

        return 0;
    }

    [ClientRpc]
    private void AssignSeatClientRpc(int seatIndex, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;

        if (BlackjackTable.Instance == null)
        {
            return;
        }

        NetworkObject localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer == null || !localPlayer.TryGetComponent(out BlackJack blackJack))
        {
            return;
        }

        // BlackjackTable.Interact used to do this through NetworkManager.ConnectedClients, which
        // is only populated on the server - on a remote client the seat was never assigned.
        blackJack.handTransform = BlackjackTable.Instance.GetSeat(seatIndex);
        blackJack.OnSeated();
    }

    public void ExitGame()
    {
        if (!IsSpawned)
        {
            return;
        }

        ExitGameServerRpc();
    }

    // ExitGame used to be a plain method mutating the dictionaries on whichever machine called
    // it. On a client that touched nothing the server could see, so leaving the table never
    // actually freed the seat or took the player out of the turn rotation.
    [ServerRpc(RequireOwnership = false)]
    private void ExitGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        RemoveFromGame(serverRpcParams.Receive.SenderClientId);
    }

    private void RemoveFromGame(ulong clientId)
    {
        if (!_seatOrder.Contains(clientId))
        {
            return;
        }

        bool wasTheirTurn = playerTurn.Value == clientId;

        _seatOrder.Remove(clientId);
        _seatByClient.Remove(clientId);
        playerInCurrentGameList.Remove(clientId);
        playerInGameList.Remove(clientId);
        _cardsInHand.Remove(clientId);

        // Tell the client its keys are dead again. Skipped for a player who already dropped off
        // the connection - the RPC would have nowhere to land.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
        {
            LeftTableClientRpc(ToClient(clientId));
        }

        if (_seatOrder.Count == 0)
        {
            playerTurn.Value = NoPlayer;
            ResetGame();
            return;
        }

        if (wasTheirTurn)
        {
            // Hand the turn on rather than leaving it parked on someone who stood up.
            AdvanceTurnFrom(-1);
        }
        else
        {
            CheckIfAllPlayersDone();
        }
    }

    [ClientRpc]
    private void LeftTableClientRpc(ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;

        NetworkObject localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent(out BlackJack blackJack))
        {
            blackJack.OnLeftTable();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            RemoveFromGame(clientId);
        }
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        base.OnDestroy();
    }

    #endregion

    #region Drawing

    public void RequestDraw()
    {
        if (IsSpawned)
        {
            RequestDrawCardServerRpc();
        }
    }

    // Server-authoritative draw. The old version took an isFirstCard bool off the wire and used
    // it to bypass the turn check, which needed a whole HashSet of guard state to stop a modified
    // client claiming it forever. The first card is now dealt by the server itself at seat time,
    // so this path has no bypass at all.
    [ServerRpc(RequireOwnership = false)]
    public void RequestDrawCardServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;

        if (!playerInCurrentGameList.TryGetValue(clientId, out var entry) || entry.Item1)
        {
            return;
        }

        if (playerTurn.Value != clientId)
        {
            SendMsgServerRpc("It's not your turn!", clientId);
            return;
        }

        DealCard(clientId, false);
    }

    // Server only. Picks a card, updates the shoe and the player's running sum, puts the card
    // object on the table, then passes the turn on.
    private void DealCard(ulong clientId, bool isFirstCard)
    {
        if (!playerInCurrentGameList.TryGetValue(clientId, out var entry))
        {
            return;
        }

        Card newCard = GetRandomCard();
        if (newCard == null)
        {
            SendMsgServerRpc("Deck is empty!");
            HandResultClientRpc(0, entry.Item2, true, ToClient(clientId));
            return;
        }

        cardDictionary[newCard]--;

        int cardListIndex = cardDeck.IndexOf(newCard);
        int newSum = entry.Item2 + newCard.cardValue;
        playerInCurrentGameList[clientId] = (entry.Item1, newSum);

        // Which slot in the fan this card takes. Tracked server-side so the layout no longer
        // depends on the client's own hand list arriving first.
        _cardsInHand.TryGetValue(clientId, out int handSlot);
        _cardsInHand[clientId] = handSlot + 1;

        SpawnCard(clientId, cardListIndex, handSlot, isFirstCard);
        HandResultClientRpc(cardListIndex, newSum, false, ToClient(clientId));

        if (newSum > 21)
        {
            SendMsgServerRpc($"{NameOf(clientId)} busted!");
            // A bust is out of the round but stays in the seat, so the next round still deals
            // them in. Marking them done rather than dropping them keeps the rotation intact.
            playerInCurrentGameList[clientId] = (true, newSum);
            AdvanceTurn(clientId);
            return;
        }

        if (newSum == 21)
        {
            AwardBlackjack(clientId);
            return;
        }

        // The first card is the deal, not a turn - the player still gets to act.
        if (!isFirstCard)
        {
            AdvanceTurn(clientId);
        }
    }

    private readonly Dictionary<ulong, int> _cardsInHand = new();

    // Server only. The card object used to be spawned by a ServerRpc the *client* fired after it
    // received its card, which meant the layout depended on client state and the spawn could be
    // replayed. The server knows the seat and the card count, so it places the card itself.
    private void SpawnCard(ulong clientId, int cardIndex, int handSlot, bool isFirstCard)
    {
        if (cardPrefab == null || cardIndex < 0 || cardIndex >= cardDeck.Count)
        {
            return;
        }

        Transform seat = BlackjackTable.Instance != null && _seatByClient.TryGetValue(clientId, out int seatIndex)
            ? BlackjackTable.Instance.GetSeat(seatIndex)
            : null;

        Vector3 targetPosition;
        Quaternion rotation;
        if (seat != null)
        {
            float positionX = (handSlot % 2 == 0 ? 1 : -1) * Mathf.Ceil(handSlot / 2f) * cardSpacing;
            targetPosition = seat.TransformPoint(new Vector3(positionX, 0, 0));
            rotation = seat.rotation;
        }
        else
        {
            targetPosition = transform.position;
            rotation = transform.rotation;
        }

        GameObject newCardObject = Instantiate(cardPrefab, transform.position, rotation);
        NetworkObject networkObject = newCardObject.GetComponent<NetworkObject>();
        networkObject.SpawnWithOwnership(clientId);
        spawnedCards.Add(networkObject);

        int artworkIndex = Random.Range(0, Mathf.Max(1, cardDeck[cardIndex].artworks.Length));

        // The owner always sees the face; everyone else sees the back of a face-down first card.
        SpawnCardClientRpc(networkObject.NetworkObjectId, cardIndex, artworkIndex, targetPosition, true,
            ToClient(clientId));
        SpawnCardClientRpc(networkObject.NetworkObjectId, cardIndex, artworkIndex, targetPosition, !isFirstCard,
            ToOthers(clientId));
    }

    [ClientRpc]
    private void HandResultClientRpc(int cardListIndex, int newHandSum, bool deckEmpty, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;

        NetworkObject localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent(out BlackJack blackJack))
        {
            blackJack.ReceiveDealtCard(cardListIndex, newHandSum, deckEmpty);
        }
    }

    [ClientRpc]
    public void SpawnCardClientRpc(ulong networkObjectId, int cardIndex, int artworkIndex, Vector3 targetedPos, bool faceUp = false, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;

        // The spawn message and this RPC can arrive in either order on a slow client, so the
        // object may not be in SpawnedObjects yet - the old indexer threw KeyNotFoundException.
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) ||
            cardIndex < 0 || cardIndex >= cardDeck.Count)
        {
            return;
        }

        Mesh[] artworks = cardDeck[cardIndex].artworks;
        if (artworks != null && artworkIndex >= 0 && artworkIndex < artworks.Length &&
            networkObject.TryGetComponent(out MeshFilter meshFilter))
        {
            meshFilter.mesh = artworks[artworkIndex];
        }

        if (!faceUp)
        {
            networkObject.transform.Rotate(0, 0, 180); // Flip the card
        }

        StartCoroutine(MoveCard(networkObject.transform, targetedPos));
    }

    public Card GetRandomCard()
    {
        if (DeckIsEmpty(cardDictionary))
        {
            return null;
        }

        // Draw from what is actually left rather than re-rolling a random index until it happens
        // to land on a card with stock - that loop got slower and slower as the shoe emptied.
        List<Card> remaining = cardDeck.Where(card => cardDictionary[card] > 0).ToList();
        return remaining[Random.Range(0, remaining.Count)];
    }

    private void PopulateDictionary()
    {
        cardDictionary = new Dictionary<Card, int>();
        for (int i = 0; i < cardDeck.Count; i++)
        {
            if (cardDeck[i] == null)
            {
                continue;
            }

            // The same Card asset listed twice used to throw ArgumentException here and take the
            // whole table down before it ever spawned.
            cardDictionary[cardDeck[i]] = 4;
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
            if (card != null)
            {
                cardDictionary[card] = 4;
            }
        }
    }

    public IEnumerator MoveCard(Transform cardTransform, Vector3 end)
    {
        Vector3 start = cardTransform.position;
        float elapsedTime = 0f;
        while (elapsedTime < moveDuration)
        {
            // Lerping from `transform.position` (the deck) every frame instead of from the card's
            // own start meant a card already in flight snapped back to the deck each frame.
            if (cardTransform == null)
            {
                yield break;
            }

            float t = elapsedTime / moveDuration;
            float curvedT = movementCurve.Evaluate(t); // Apply curve
            cardTransform.position = Vector3.Lerp(start, end, curvedT);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (cardTransform != null)
        {
            cardTransform.position = end; // Ensure it snaps to the final position
        }
    }

    #endregion

    #region Turn flow

    public void Stand()
    {
        if (IsSpawned)
        {
            StandServerRpc();
        }
    }

    // "Done"/stand. This used to be three separate calls the client made in sequence
    // (FinishTurn, GetNextPlayer, CheckIfAllPlayersDone) with the client deciding from its own
    // copy of playerTurn whether to pass the turn on. One server step instead.
    [ServerRpc(RequireOwnership = false)]
    private void StandServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;

        if (!playerInCurrentGameList.TryGetValue(clientId, out var entry) || entry.Item1)
        {
            return;
        }

        playerInCurrentGameList[clientId] = (true, entry.Item2);
        AdvanceTurn(clientId);
    }

    private void AdvanceTurn(ulong fromClientId)
    {
        AdvanceTurnFrom(_seatOrder.IndexOf(fromClientId));
    }

    // Walks the seating order for the next player who still has a turn to take. Iterative on
    // purpose: the old GetNextPlayer recursed into itself to skip finished players and could run
    // away entirely once the index/client-id mix-up above put it on a seat that did not exist.
    private void AdvanceTurnFrom(int fromSeatPosition)
    {
        if (_seatOrder.Count == 0)
        {
            playerTurn.Value = NoPlayer;
            return;
        }

        for (int step = 1; step <= _seatOrder.Count; step++)
        {
            int index = (fromSeatPosition + step) % _seatOrder.Count;
            if (index < 0)
            {
                index += _seatOrder.Count;
            }

            ulong candidate = _seatOrder[index];
            if (playerInCurrentGameList.TryGetValue(candidate, out var entry) && !entry.Item1)
            {
                playerTurn.Value = candidate;
                return;
            }
        }

        // Nobody left to act - the round is over.
        CheckIfAllPlayersDone();
    }

    private void AwardBlackjack(ulong clientId)
    {
        if (playerInGameList.ContainsKey(clientId))
        {
            playerInGameList[clientId]++;
        }

        SendMsgServerRpc($"{NameOf(clientId)} got a Blackjack!");
        FinishRound();
    }

    // Server only.
    private void CheckIfAllPlayersDone()
    {
        if (playerInCurrentGameList.Count == 0)
        {
            return;
        }

        if (playerInCurrentGameList.Values.Any(value => !value.Item1))
        {
            return;
        }

        List<ulong> winners = new();
        int highestScore = -1;
        foreach (KeyValuePair<ulong, (bool, int)> player in playerInCurrentGameList)
        {
            int score = player.Value.Item2;
            if (score > 21)
            {
                continue; // Busted hands never win, whatever they add up to.
            }

            if (score > highestScore)
            {
                highestScore = score;
                winners.Clear();
                winners.Add(player.Key);
            }
            else if (score == highestScore)
            {
                winners.Add(player.Key);
            }
        }

        if (winners.Count > 0)
        {
            string winnerString = winners.Count > 1 ? "Winners are" : "Winner is";
            SendMsgServerRpc($"{winnerString} : " + string.Join(", ", winners.Select(NameOf)));
            foreach (ulong winner in winners)
            {
                if (playerInGameList.ContainsKey(winner))
                {
                    playerInGameList[winner]++;
                }
            }
        }
        else
        {
            SendMsgServerRpc("No winners this round");
        }

        FinishRound();
    }

    // Server only. A finished round is what completes the group task - for everyone who sat it
    // out to the end, not just the winner, and only once the table actually had a crowd.
    private void FinishRound()
    {
        if (blackjackTask != null && TaskManager.Instance != null &&
            _seatOrder.Count >= taskMinimumPlayers)
        {
            foreach (ulong clientId in _seatOrder)
            {
                TaskManager.Instance.CompleteTaskForPlayer(clientId, blackjackTask);
            }
        }
        else if (blackjackTask != null && _seatOrder.Count > 0 && _seatOrder.Count < taskMinimumPlayers)
        {
            SendMsgServerRpc($"Needs {taskMinimumPlayers} players for the task to count.");
        }

        ResetGame();
    }

    // Server only.
    public void ResetGame()
    {
        if (!IsServer)
        {
            return;
        }

        RestartDeck();
        DestroyCards();
        ResetHandsClientRpc();
        _cardsInHand.Clear();

        List<ulong> keys = new(playerInCurrentGameList.Keys);
        foreach (ulong key in keys)
        {
            playerInCurrentGameList[key] = (false, 0);
        }

        // The turn used to be reset to literal client id 0 - the host - whether or not the host
        // was even sitting at the table.
        playerTurn.Value = _seatOrder.Count > 0 ? _seatOrder[0] : NoPlayer;
    }

    private void DestroyCards()
    {
        foreach (NetworkObject card in spawnedCards)
        {
            if (card == null)
            {
                continue;
            }

            // Despawn(true) already destroys the object; the extra Destroy right after it was
            // operating on a destroyed object and logged an error for every card on the table.
            if (card.IsSpawned)
            {
                card.Despawn(true);
            }
            else
            {
                Destroy(card.gameObject);
            }
        }
        spawnedCards.Clear();
    }

    [ClientRpc]
    private void ResetHandsClientRpc()
    {
        NetworkObject localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent(out BlackJack blackJack))
        {
            blackJack.RestartHand();
        }
    }

    #endregion

    #region Messaging / helpers

    private static ClientRpcParams ToClient(ulong clientId) => new()
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    private static ClientRpcParams ToOthers(ulong clientId) => new()
    {
        Send = new ClientRpcSendParams
        {
            TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds
                .Where(id => id != clientId)
                .ToArray()
        }
    };

    private static string NameOf(ulong clientId) =>
        GameManager.Instance != null ? GameManager.Instance.GetPlayerNickname(clientId) : clientId.ToString();

    [ServerRpc(RequireOwnership = false)]
    public void SendMsgServerRpc(string msgToSend)
    {
        SendMsgClientRpc(msgToSend);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendMsgServerRpc(string msgToSend, ulong clientId)
    {
        SendMsgClientRpc(msgToSend, ToClient(clientId));
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
        if (turnsText == null)
        {
            return false;
        }

        if (playerTurn.Value == NoPlayer)
        {
            turnsText.text = "Turn: -";
            return true;
        }

        if (GameManager.Instance == null)
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

    #endregion
}
