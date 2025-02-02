public interface IInteractable
{
    bool IsHeld { get; set; }
    bool IsPickable { get; set; }
    void Interact(ulong clientId);
    void Drop();
}
