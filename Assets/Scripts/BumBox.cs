using UnityEngine;
using Unity.Netcode;

public class BumBox : NetworkBehaviour, IInteractable
{

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = true;
    public string InteractionPrompt => "Pick Up";
    public int holderId = -1;

    [Header("Music")]
    [Tooltip("The tracks this boombox cycles through with the Change Music key (N). Drop new " +
        "AudioClips in here and they are in rotation - no code change. Order is the play order; " +
        "the list wraps around. Leaving it empty falls back to whatever clip the AudioSource was " +
        "authored with, and the Change Music key does nothing.")]
    public AudioClip[] playlist;

    [SerializeField, Tooltip("Optional: the task completed by changing this box's music (the " +
        "Change Music key). Leave empty on a box that is not a task objective. This box already " +
        "implements IInteractable for pick-up, so it cannot also carry a TaskObjective component " +
        "- only one IInteractable per collider is ever found.")]
    private Challenge musicTask;

    // Which playlist entry is currently playing. Server-writable only: the track is shared world
    // state, so everyone standing near the box has to hear the same thing, and a client that
    // joins late needs the current track rather than track 0.
    private readonly NetworkVariable<int> trackIndex = new(-1);

    private AudioSource _audioSource;


    // The boombox is its own objective: it already implements IInteractable for pick-up, so it
    // registers its task here instead of carrying a TaskObjective component (only one
    // IInteractable per collider is ever found by the Interact raycast).
    private void OnEnable()
    {
        TaskManager.RegisterObjective(musicTask);
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(musicTask);
    }

    private void Awake()
    {
        TryGetComponent(out _audioSource);
    }

    public override void OnNetworkSpawn()
    {
        trackIndex.OnValueChanged += OnTrackChanged;

        // A late joiner gets whatever is already playing. -1 means nobody has pressed the key
        // yet, so the authored AudioSource clip is still the right one and is left alone.
        if (trackIndex.Value >= 0)
        {
            ApplyTrack(trackIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        trackIndex.OnValueChanged -= OnTrackChanged;
    }

    /// <summary>Called on the local player's client by Interact when the Change Music key is
    /// pressed while looking at (or holding) this box.</summary>
    public void ChangeMusic()
    {
        if (playlist == null || playlist.Length == 0)
        {
            return;
        }

        ChangeMusicServerRpc();

        // "Change boombox music" is this exact press. Gated on the task actually being open for
        // this player so idle key presses do not spam the server with dead RPCs.
        if (musicTask != null && TaskManager.Instance != null &&
            TaskManager.Instance.IsTaskOpenForLocalPlayer(musicTask))
        {
            TaskManager.Instance.ReportTaskCompleted(musicTask);
        }
    }

    // The server owns the track so every client lands on the same one. No parameter to validate:
    // the next track is derived here, not named by the caller.
    [ServerRpc(RequireOwnership = false)]
    private void ChangeMusicServerRpc()
    {
        if (playlist == null || playlist.Length == 0)
        {
            return;
        }

        trackIndex.Value = (trackIndex.Value + 1) % playlist.Length;
    }

    private void OnTrackChanged(int previous, int current)
    {
        ApplyTrack(current);
    }

    private void ApplyTrack(int index)
    {
        if (_audioSource == null || playlist == null || index < 0 || index >= playlist.Length)
        {
            return;
        }

        AudioClip clip = playlist[index];
        if (clip == null)
        {
            return;
        }

        // Swapping the clip on a paused source leaves it paused, so an unmuted box that gets a
        // new track would go silent until someone hit Mute twice. Play() unconditionally.
        _audioSource.clip = clip;
        _audioSource.Play();
    }

    public void Interact(ulong clientId)
    {
        if (IsHeld) return;
        PickUpServerRpc(clientId);

        var localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent<Interact>(out var interact))
        {
            interact.fakeBox.gameObject.SetActive(true);
            interact.fakeboxShadow.gameObject.SetActive(true);
        }
    }
    
    public void Drop()
    {
        if (!IsHeld) return;
        DropServerRpc();

        var localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent<Interact>(out var interact))
        {
            interact.fakeBox.gameObject.SetActive(false);
            interact.fakeboxShadow.gameObject.SetActive(false);
        }
    }

    public void Mute()
    {
        MuteServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PickUpServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        // clientId is otherwise a client-supplied value with no other check - without this, any
        // connected client could assign the box to an arbitrary holderId, not just themselves.
        if (clientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        PickUpClientRpc(clientId);
    }

    [ClientRpc]
    private void PickUpClientRpc(ulong clientId)
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
        holderId = (int)clientId;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropServerRpc(ServerRpcParams serverRpcParams = default)
    {
        // Only the player currently holding the box may drop it.
        if ((ulong)holderId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        DropClientRpc();
    }

    [ClientRpc]
    private void DropClientRpc()
    {
        IsHeld = false;
        
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = false;
        }
        
        var playerObj = NetworkManager.Singleton?.SpawnManager?.GetPlayerNetworkObject((ulong)holderId);
        if (playerObj != null)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.AddForce(playerObj.transform.forward * 5f, ForceMode.Impulse);
        }
        
        holderId = -1;
    }

    [ServerRpc(RequireOwnership = false)]
    private void MuteServerRpc()
    {
        MuteClientRpc();
    }

    [ClientRpc]
    private void MuteClientRpc()
    {
        if (TryGetComponent<AudioSource>(out var audioSource))
        {
            if (audioSource.isPlaying)
            {
                audioSource.Pause();
            }
            else
            {
                audioSource.UnPause();
            }
        }
    }
}
