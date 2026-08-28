using System;
using UnityEngine;

public class OfflineBumBox : MonoBehaviour, IInteractable
{
    // Raised every time the mute toggle is used on this box (pause/unpause its audio).
    // TutorialManager listens for this to advance the "shut down" tutorial step.
    public static event Action MuteToggled;

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = true;
    public string InteractionPrompt => "Pick Up";
    public int holderId = -1;


    public void Interact(ulong clientId)
    {
        if (IsHeld) return;
        PickUp();
    }
    public void Drop()
    {
        if (!IsHeld) return;
        IsHeld = false;
        GetComponent<Collider>().isTrigger = false;
        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.AddForce(transform.forward * 5f, ForceMode.Impulse);
    }
    private void PickUp()
    {
        IsHeld = true;
        if (GetComponent<Rigidbody>() != null)
        {
            Destroy(GetComponent<Rigidbody>());
        }
        if (GetComponent<Collider>() != null)
        {
            GetComponent<Collider>().isTrigger = true;
        }
    }

    public void Mute()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource.isPlaying)
        {
            audioSource.Pause();
        }
        else
        {
            audioSource.UnPause();
        }

        MuteToggled?.Invoke();
    }
}
