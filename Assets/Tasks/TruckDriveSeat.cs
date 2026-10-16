using UnityEngine;

// The "Drive" prompt on the truck. Sits on a trigger child of Truck.prefab on the Interactable
// layer, so the player's Interact raycast finds it; everything else is TruckDriver's.
[RequireComponent(typeof(Collider))]
public class TruckDriveSeat : MonoBehaviour, IInteractable
{
    [SerializeField] private TruckDriver driver;

    public bool IsHeld { get => false; set { } }
    public bool IsPickable { get; set; } = false;
    public string InteractionPrompt => "Drive";

    // Only once the engine is back in, and while nobody else is at the wheel.
    public bool CanInteract => driver != null && driver.CanLocalPlayerDrive;

    // The drive ends on its own timer, so the next Interact press must stay free.
    public bool LatchesPlayer => false;

    private void Awake()
    {
        if (driver == null)
        {
            driver = GetComponentInParent<TruckDriver>();
        }
    }

    public void Interact(ulong clientId)
    {
        if (CanInteract)
        {
            driver.RequestDrive();
        }
    }

    public void Drop()
    {
    }
}
