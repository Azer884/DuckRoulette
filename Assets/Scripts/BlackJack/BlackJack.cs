using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BlackJack : NetworkBehaviour
{
    public List<Card> hand;
    public Transform handTransform;
    public float cardSpacing = .1f;
    private int handSum;
    public bool canBlackjack, canDraw, canDone;
    public bool drawnFirstCard = false;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
        }
    }

    void Update()
    {
        if (canDraw && Input.GetKeyDown(KeyCode.Z))
        {
            DrawCard();
        }
        if (canDone && Input.GetKeyDown(KeyCode.X))
        {
            Done();
        }
        if (canBlackjack && Input.GetKeyDown(KeyCode.C))
        {
            Blackjack();
        }
        if (Input.GetKeyDown(KeyCode.Return))
        {
            CardDeck.instance.ExitGame(OwnerClientId);
        }
    }

    public void DrawCard(bool isFirstCard = false)
    {
        if (CardDeck.instance.playerTurn.Value == OwnerClientId || isFirstCard)
        {
            Card newCard = CardDeck.instance.GetRandomCard();
            if (newCard != null)
            {
                if (!isFirstCard)
                {
                    CardDeck.instance.GetNextPlayerServerRpc();
                }
                CardDeck.instance.cardDictionary[newCard]--;
                hand.Add(newCard);
                int index = hand.Count - 1; // Get the index of the newly added card
                float positionX = (index % 2 == 0 ? 1 : -1) * Mathf.Ceil(index / 2f) * cardSpacing;
                Vector3 worldPosition = handTransform.TransformPoint(new Vector3(positionX, 0, 0));
                
                CardDeck.instance.SpawnCardServerRpc(OwnerClientId, worldPosition, handTransform.rotation, newCard.cardValue, isFirstCard);
        
                if (hand.Count >= 2)
                {
                    canDone = true;
                }
                
                CheckHand();
            }
            else
            {
                CardDeck.instance.SendMsgServerRpc("Deck is empty!");
                canDone = true;
                canDraw = false;
            }
        }
        else
        {
            CardDeck.instance.SendMsgServerRpc("It's not your turn!", OwnerClientId);
        }
    }

    private void CheckHand()
    {
        handSum += hand[^1].cardValue;
        if (handSum > 21)
        {
            LostThisGameServerRpc(OwnerClientId);

            

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
        FinishTurnServerRpc(OwnerClientId);
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
        CardDeck.instance.ResetGameServerRpc();

        CardDeck.instance.playerInGameList[OwnerClientId]++;
        CardDeck.instance.SendMsgServerRpc($"{GameManager.Instance.GetPlayerNickname(OwnerClientId)} got a Blackjack!");
    }

    [ServerRpc]
    private void FinishTurnServerRpc(ulong clientId)
    {
        if (CardDeck.instance.playerInCurrentGameList.ContainsKey(clientId))
        {
            CardDeck.instance.playerInCurrentGameList[clientId] = (true, handSum);
        }
    }

    [ServerRpc]
    private void LostThisGameServerRpc(ulong clientId)
    {
        if (CardDeck.instance.playerInCurrentGameList.ContainsKey(clientId))
        {
            CardDeck.instance.playerInCurrentGameList.Remove(clientId);
        }
        CardDeck.instance.CheckIfAllPlayersDoneServerRpc();
    }

    void OnDisable()
    {
        CardDeck.instance.ExitGame(OwnerClientId);
    }
}
