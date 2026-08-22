using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class BlackJack : NetworkBehaviour
{
    public List<Card> hand;
    public Transform handTransform;
    public float cardSpacing = .1f;
    private int handSum;
    public bool canBlackjack, canDraw, canDone;
    public bool drawnFirstCard = false;
    private InputAction drawAction, doneAction, blackjackAction, leaveAction;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        // This prefab doesn't carry its own InputSystem component (unlike the main player
        // prefab), so read from the shared/global action asset instead.
        if (RebindSaveLoad.Instance != null)
        {
            InputActionAsset inputActions = RebindSaveLoad.Instance.actions;
            drawAction = inputActions.FindAction("Draw");
            doneAction = inputActions.FindAction("Done");
            blackjackAction = inputActions.FindAction("Blackjack");
            leaveAction = inputActions.FindAction("LeaveBlackjack");
        }
    }

    void Update()
    {
        if (canDraw && drawAction != null && drawAction.triggered)
        {
            DrawCard();
        }
        if (canDone && doneAction != null && doneAction.triggered)
        {
            Done();
        }
        if (canBlackjack && blackjackAction != null && blackjackAction.triggered)
        {
            Blackjack();
        }
        if (leaveAction != null && leaveAction.triggered)
        {
            CardDeck.instance.ExitGame(OwnerClientId);
        }
    }

    public void DrawCard(bool isFirstCard = false)
    {
        // Null check
        if (CardDeck.instance == null)
        {
            Debug.LogError("CardDeck instance is null!");
            return;
        }

        // The server now picks the card, updates the real deck count, and tracks the running
        // hand sum - this client only finds out the result via ReceiveDealtCard below.
        CardDeck.instance.RequestDrawCardServerRpc(isFirstCard);
    }

    // Called by CardDeck once the server has authoritatively resolved a draw request.
    public void ReceiveDealtCard(int cardListIndex, int newHandSum, bool deckEmpty, bool isFirstCard)
    {
        if (deckEmpty)
        {
            canDone = true;
            canDraw = false;
            return;
        }

        Card newCard = CardDeck.instance.cardDeck[cardListIndex];
        hand.Add(newCard);
        handSum = newHandSum;

        int index = hand.Count - 1; // Get the index of the newly added card
        float positionX = (index % 2 == 0 ? 1 : -1) * Mathf.Ceil(index / 2f) * cardSpacing;
        Vector3 worldPosition = handTransform.TransformPoint(new Vector3(positionX, 0, 0));

        CardDeck.instance.SpawnCardServerRpc(OwnerClientId, worldPosition, handTransform.rotation, cardListIndex, isFirstCard);

        if (hand.Count >= 2)
        {
            canDone = true;
        }

        if (handSum > 21)
        {
            LostThisGameServerRpc();

            Invoke(nameof(LostMsg), .5f);
            canDraw = false;
            canDone = false;
        }
        else if (handSum == 21)
        {
            canBlackjack = true;
        }
    }

    private void LostMsg()
    {
        CardDeck.instance.SendMsgServerRpc($"{GameManager.Instance.GetPlayerNickname(OwnerClientId)} lost this game!", OwnerClientId);
    }

    public void RestartHand()
    {
        canDraw = true;
        canDone = true;
        canBlackjack = false;
        hand.Clear();
        handSum = 0;
    }
    
    public void Done()
    {
        FinishTurnServerRpc();
        if (CardDeck.instance.playerTurn.Value == OwnerClientId)
        {
            CardDeck.instance.GetNextPlayerServerRpc();
        }
        canDraw = false;
        canDone = false;

        CardDeck.instance.CheckIfAllPlayersDoneServerRpc();
    }

    public void Blackjack()
    {
        CardDeck.instance.BlackjackServerRpc();
    }

    // No clientId parameter needed - [ServerRpc] (RequireOwnership defaults to true) already
    // guarantees this only ever runs for this NetworkObject's own owner, so OwnerClientId is
    // trustworthy here.
    [ServerRpc]
    private void FinishTurnServerRpc()
    {
        if (CardDeck.instance.playerInCurrentGameList.TryGetValue(OwnerClientId, out var entry))
        {
            CardDeck.instance.playerInCurrentGameList[OwnerClientId] = (true, entry.Item2);
        }
    }

    [ServerRpc]
    private void LostThisGameServerRpc()
    {
        if (CardDeck.instance.playerInCurrentGameList.ContainsKey(OwnerClientId))
        {
            CardDeck.instance.playerInCurrentGameList.Remove(OwnerClientId);
        }
        CardDeck.instance.CheckIfAllPlayersDoneServerRpc();
    }

    void OnDisable()
    {
        if (CardDeck.instance != null)
        {
            CardDeck.instance.ExitGame(OwnerClientId);
        }
    }
}
