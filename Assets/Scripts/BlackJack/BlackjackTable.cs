using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The walk-up-and-sit-down half of the blackjack table. Everything about the game itself lives on
// CardDeck (on the Deck child); this only owns the seats and the interaction.
public class BlackjackTable : MonoBehaviour, IInteractable
{
    public static BlackjackTable Instance { get; private set; }

    [SerializeField, Tooltip("Seat anchors, one per chair. Left empty they are collected from the " +
        "children named \"Hand ...\" at startup, which is how the prefab is laid out.")]
    private List<Transform> seats = new();

    public bool IsHeld { get; set;} = false;
    public bool IsPickable {get; set;} = false;
    public string InteractionPrompt => "Play Blackjack";

    public int SeatCount => seats.Count;

    private void Awake()
    {
        Instance = this;

        if (seats.Count == 0)
        {
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Hand"))
                {
                    seats.Add(child);
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>The anchor a player's cards are dealt to, or null for an out-of-range seat.</summary>
    public Transform GetSeat(int seatIndex) =>
        seatIndex >= 0 && seatIndex < seats.Count ? seats[seatIndex] : null;

    /// <summary>Clears the Interact latch for a player who left the table by some route other
    /// than pressing Interact again (the Leave key, a disconnect, being despawned).</summary>
    public void ReleaseLocalPlayer(Component player)
    {
        IsHeld = false;

        if (player != null && player.TryGetComponent(out Interact interact))
        {
            interact.ClearHeldObjectIfMatches(transform);
        }
    }

    public void Drop()
    {
        if (CardDeck.instance != null)
        {
            CardDeck.instance.ExitGame();
        }

        IsHeld = false;
    }

    public void Interact(ulong clientId)
    {
        if (CardDeck.instance == null)
        {
            return;
        }

        // The seat, and the hand anchor that goes with it, are handed back by the server - this
        // used to pick the chair by client id and read it out of NetworkManager.ConnectedClients,
        // which is empty on a client, so a remote player got no anchor and a fifth player indexed
        // straight past the chairs into the deck.
        CardDeck.instance.EnterGame();
        IsHeld = true;
    }
}
