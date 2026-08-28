public interface IInteractable
{
    bool IsHeld { get; set; }
    bool IsPickable { get; set; }
    // Short verb phrase shown in the interaction prompt HUD, e.g. "Pick Up", "Hide".
    string InteractionPrompt { get; }
    void Interact(ulong clientId);
    void Drop();
}
