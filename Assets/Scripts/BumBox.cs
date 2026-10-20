using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class BumBox : NetworkBehaviour, IInteractable
{

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = true;
    public string InteractionPrompt => "Pick Up";
    public int holderId = -1;

    [Header("Music")]
    [Tooltip("Fallback tracks, only used when there is no MusicManager (e.g. a test scene). In a " +
        "normal session the box plays MusicManager's in-game tracks, so the game music and the " +
        "boombox are always the same list.")]
    public AudioClip[] playlist;

    /// <summary>The tracks this box cycles through: MusicManager's in-game tracks, or the local
    /// fallback list when there is no MusicManager.</summary>
    public AudioClip[] Playlist =>
        MusicManager.Instance != null && MusicManager.Instance.GameTracks.Length > 0
            ? MusicManager.Instance.GameTracks
            : playlist;

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

    // MusicManager's source while it plays this box's music; null when the box plays on its own.
    private AudioSource _drivingSource;

    [Header("Map-wide music events")]
    [SerializeField, Tooltip("Seconds between the moments the boombox is heard across the whole " +
        "map (2D for everyone), picked at random in this range.")]
    private Vector2 broadcastIntervalRange = new(150f, 300f);
    [SerializeField, Tooltip("How long, in seconds, one of those map-wide moments lasts.")]
    private Vector2 broadcastDurationRange = new(10f, 15f);

    // True while the music is heard everywhere at full volume instead of from the box. Server
    // decides, so every player hears the switch at the same moment.
    private readonly NetworkVariable<bool> broadcasting = new(false);

    private static readonly List<BumBox> spawned = new();

    /// <summary>The boombox the game music comes from (the map can place two; the one spawned
    /// first wins), or null when there is none.</summary>
    public static BumBox Primary
    {
        get
        {
            BumBox best = null;
            foreach (BumBox box in spawned)
            {
                if (box != null && box.IsSpawned && (best == null || box.NetworkObjectId < best.NetworkObjectId))
                {
                    best = box;
                }
            }
            return best;
        }
    }

    /// <summary>Raised on every peer when a box switches track. MusicManager plays it.</summary>
    public static event Action<BumBox, AudioClip> TrackChanged;

    /// <summary>The track picked with the Change Music key, or null before anyone has pressed it.</summary>
    public AudioClip CurrentClip =>
        Playlist != null && trackIndex.Value >= 0 && trackIndex.Value < Playlist.Length ? Playlist[trackIndex.Value] : null;

    /// <summary>True while the music is heard across the whole map instead of from the box.</summary>
    public bool IsBroadcasting => broadcasting.Value;

    /// <summary>MusicManager takes over this box's music: its own speaker goes quiet (so the song
    /// isn't heard twice) and its pulse follows the manager's source instead.</summary>
    public void SetDrivenBy(AudioSource musicSource)
    {
        _drivingSource = musicSource;
        if (_audioSource != null)
        {
            _audioSource.mute = musicSource != null;
        }

        if (TryGetComponent(out SoundToScale pulse))
        {
            pulse.audioSource = musicSource != null ? musicSource : _audioSource;
        }
    }


    // The boombox is its own objective: it already implements IInteractable for pick-up, so it
    // registers its task here instead of carrying a TaskObjective component (only one
    // IInteractable per collider is ever found by the Interact raycast).
    private void OnEnable()
    {
        TaskManager.RegisterObjective(musicTask);
        TaskHighlighter.Register(musicTask, gameObject);
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(musicTask);
        TaskHighlighter.Unregister(musicTask, gameObject);
    }

    private void Awake()
    {
        TryGetComponent(out _audioSource);
    }

    public override void OnNetworkSpawn()
    {
        trackIndex.OnValueChanged += OnTrackChanged;
        broadcasting.OnValueChanged += OnBroadcastingChanged;
        spawned.Add(this);

        if (IsServer)
        {
            // Start on a random in-game track so each match doesn't open with the same song.
            AudioClip[] tracks = Playlist;
            if (trackIndex.Value < 0 && tracks != null && tracks.Length > 0)
            {
                trackIndex.Value = UnityEngine.Random.Range(0, tracks.Length);
            }

            StartCoroutine(BroadcastLoop());
        }

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
        broadcasting.OnValueChanged -= OnBroadcastingChanged;
        spawned.Remove(this);
        StopAllCoroutines();
    }

    // Server only: every so often the music the box plays is blasted to the whole map for a while.
    private IEnumerator BroadcastLoop()
    {
        while (IsSpawned)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(broadcastIntervalRange.x, broadcastIntervalRange.y));
            if (Primary != this)
            {
                continue;
            }

            broadcasting.Value = true;
            yield return new WaitForSeconds(UnityEngine.Random.Range(broadcastDurationRange.x, broadcastDurationRange.y));
            broadcasting.Value = false;
        }
    }

    private void OnBroadcastingChanged(bool previous, bool current)
    {
        if (current && Primary == this)
        {
            MessageBox.Informate("The boombox is blasting across the whole map!", new Color(1f, 0.557f, 0.024f));
        }
    }

    /// <summary>Called on the local player's client by Interact when the Change Music key is
    /// pressed while looking at (or holding) this box.</summary>
    public void ChangeMusic()
    {
        if (Playlist == null || Playlist.Length == 0)
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
    private void ChangeMusicServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsSenderNear(serverRpcParams.Receive.SenderClientId) || Playlist == null || Playlist.Length == 0)
        {
            return;
        }

        trackIndex.Value = (trackIndex.Value + 1) % Playlist.Length;
    }

    /// <summary>Server only: move on to the next song (the current one finished playing).</summary>
    public void ServerNextTrack()
    {
        if (!IsServer || Playlist == null || Playlist.Length == 0)
        {
            return;
        }

        trackIndex.Value = (trackIndex.Value + 1) % Playlist.Length;
    }

    private void OnTrackChanged(int previous, int current)
    {
        ApplyTrack(current);
    }

    private void ApplyTrack(int index)
    {
        if (_audioSource == null || Playlist == null || index < 0 || index >= Playlist.Length)
        {
            return;
        }

        AudioClip clip = Playlist[index];
        if (clip == null)
        {
            return;
        }

        // Swapping the clip on a paused source leaves it paused, so an unmuted box that gets a
        // new track would go silent until someone hit Mute twice. Play() unconditionally.
        _audioSource.clip = clip;
        _audioSource.Play();

        TrackChanged?.Invoke(this, clip);
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
        // Also server-side state: the box must be free (no stealing it out of someone's hands)
        // and within reach of the picker.
        if (clientId != serverRpcParams.Receive.SenderClientId || IsHeld || !IsSenderNear(clientId))
        {
            return;
        }

        PickUpClientRpc(clientId);
    }

    // Interact's raycast reach (5) plus movement lag. The box travels with its holder, so the
    // holder always passes this too.
    private const float MaxInteractDistance = 8f;

    private bool IsSenderNear(ulong senderId)
    {
        return NetworkManager.ConnectedClients.TryGetValue(senderId, out var client) && client.PlayerObject != null &&
               RpcValidation.IsWithinDistance(client.PlayerObject.transform.position, transform.position, MaxInteractDistance);
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
    private void MuteServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsSenderNear(serverRpcParams.Receive.SenderClientId))
        {
            return;
        }

        MuteClientRpc();
    }

    [ClientRpc]
    private void MuteClientRpc()
    {
        // While MusicManager plays this box's music, muting the box mutes that music.
        AudioSource audioSource = _drivingSource != null ? _drivingSource : _audioSource;
        if (audioSource != null)
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
