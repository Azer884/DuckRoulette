using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// The local player's side of a blackjack seat: reads the Draw / Done / Leave keys and mirrors the
// hand the server dealt. It holds no authority - the sum shown here is the one the server sent.
public class BlackJack : NetworkBehaviour
{
    public List<Card> hand;
    public Transform handTransform;
    public float cardSpacing = .1f;
    private int handSum;
    public bool canDraw, canDone;
    public bool IsSeated { get; private set; }
    private InputAction drawAction, doneAction, leaveAction;

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
            leaveAction = inputActions.FindAction("LeaveBlackjack");
        }
    }

    void Update()
    {
        // Every one of these used to be live from the moment the player spawned, so the Leave key
        // fired ExitGame at a table nobody was sitting at.
        if (!IsSeated)
        {
            return;
        }

        if (canDraw && drawAction != null && drawAction.triggered)
        {
            DrawCard();
        }
        if (canDone && doneAction != null && doneAction.triggered)
        {
            Done();
        }
        if (leaveAction != null && leaveAction.triggered)
        {
            CardDeck.instance.ExitGame();
        }
    }

    /// <summary>Called on this client once the server has given it a chair. The first card is on
    /// its way; the player can act from here.</summary>
    public void OnSeated()
    {
        IsSeated = true;
        hand.Clear();
        handSum = 0;
        canDraw = true;
        canDone = false;
    }

    /// <summary>Called when the seat is given up, so the keys go quiet again.</summary>
    public void OnLeftTable()
    {
        IsSeated = false;
        canDraw = false;
        canDone = false;
        hand.Clear();
        handSum = 0;

        // Sitting down latches the player onto the table through Interact (that is what makes the
        // next Interact press stand up again). Leaving with the Leave key instead of the Interact
        // key skipped that release, so the player stayed latched to a table they had left and
        // could not interact with anything else until they pressed Interact once into thin air.
        // Owner-gated: BlackjackTable.Instance is one shared scene object, so a remote player's
        // copy of this component clearing it would unset the *local* player's seated flag - and
        // every remote copy runs OnDisable the instant it spawns.
        if ((!IsSpawned || IsOwner) && BlackjackTable.Instance != null)
        {
            BlackjackTable.Instance.ReleaseLocalPlayer(this);
        }
    }

    public void DrawCard()
    {
        if (CardDeck.instance == null)
        {
            Debug.LogError("CardDeck instance is null!");
            return;
        }

        // The server picks the card, updates the shoe, tracks the hand sum and places the card
        // object - this client only finds out the result via ReceiveDealtCard below.
        CardDeck.instance.RequestDraw();
    }

    // Called by CardDeck once the server has authoritatively resolved a draw.
    public void ReceiveDealtCard(int cardListIndex, int newHandSum, bool deckEmpty)
    {
        if (deckEmpty)
        {
            canDone = true;
            canDraw = false;
            return;
        }

        if (cardListIndex >= 0 && cardListIndex < CardDeck.instance.cardDeck.Count)
        {
            hand.Add(CardDeck.instance.cardDeck[cardListIndex]);
        }

        handSum = newHandSum;

        // Standing needs a hand to stand on, and a decided hand takes no more input - the server
        // has already busted or paid out at this point and will reset everyone shortly.
        canDone = hand.Count >= 1 && handSum < 21;
        canDraw = handSum < 21;
    }

    public void RestartHand()
    {
        hand.Clear();
        handSum = 0;

        // A reset only puts a player back in play if they still have a chair.
        canDraw = IsSeated;
        canDone = false;
    }

    public void Done()
    {
        // One server call decides everything: mark this player done, pass the turn, and settle the
        // round if that was the last one. This used to be three RPCs fired back to back with the
        // client deciding from its own copy of playerTurn whether to advance it.
        CardDeck.instance.Stand();
        canDraw = false;
        canDone = false;
    }

    void OnDisable()
    {
        // Only the owner has a seat to give up. This used to run on every remote player's copy of
        // the component too - which are disabled the instant they spawn - so each remote spawn
        // fired an ExitGame that took the *local* player off the table.
        if (IsOwner && IsSeated && CardDeck.instance != null)
        {
            CardDeck.instance.ExitGame();
        }

        OnLeftTable();
    }
}
