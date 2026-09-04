public interface IInteractable
{
    bool IsHeld { get; set; }
    bool IsPickable { get; set; }
    // Short verb phrase shown in the interaction prompt HUD, e.g. "Pick Up", "Hide".
    string InteractionPrompt { get; }

    // Whether the local player can start an interaction with this right now - drives both the
    // prompt and the press. Defaults to "as long as nobody else already has it", which is what
    // every pick-up and hiding spot wants; TaskObjective narrows it to tasks actually on the
    // local player's list.
    bool CanInteract => !IsHeld;

    // Whether interacting latches the player onto this object until they press Interact again
    // (pick-ups, hiding spots). One-shot interactions return false, so the next press is free to
    // go somewhere else instead of being spent releasing this one.
    bool LatchesPlayer => true;

    void Interact(ulong clientId);
    void Drop();
}
