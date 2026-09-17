using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
public class VoiceChatRaycast : NetworkBehaviour
{
    [SerializeField] private LayerMask wallLayerMask;
    [SerializeField] private List<Transform> otherPlayers = new();  // Keep track of other players
    [SerializeField] private float maxDistance = 25f;
    [SerializeField] private float headHeight;
    [SerializeField] private float chestHeight;
    [SerializeField] private float feetHeight;

    private readonly Dictionary<Transform, AudioSource> audioSourceCache = new();

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Start()
    {
        // Automatically add existing players
        AddExistingPlayers();

        // Subscribe to new players joining
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnect;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnect;
    }
    private void OnDisable()
    {
        // Unsubscribe when the object is destroyed
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnect;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnPlayerDisconnect;
        }
    }

    private void Update()
    {
        foreach (Transform otherPlayer in otherPlayers)
        {
            if (otherPlayer == null) continue;

            float distanceToOtherPlayer = Vector3.Distance(transform.position, otherPlayer.position);

            AudioSource voiceAudio = ResolveVoiceAudio(otherPlayer);
            if (voiceAudio == null)
            {
                continue;
            }

            if (distanceToOtherPlayer <= maxDistance)
            {
                // Distance falloff is now the AudioSource's own 3D rolloff (H2 fix); this script
                // only applies the wall-occlusion multiplier on top of it.
                voiceAudio.volume = 1f;

                Vector3 rayStartHead = transform.position + Vector3.up * headHeight;
                Vector3 rayEndHead = otherPlayer.position + Vector3.up * headHeight;

                Vector3 rayStartChest = transform.position + Vector3.up * chestHeight;
                Vector3 rayEndChest = otherPlayer.position + Vector3.up * chestHeight;

                Vector3 rayStartFeet = transform.position + Vector3.up * feetHeight;
                Vector3 rayEndFeet = otherPlayer.position + Vector3.up * feetHeight;

                bool wallInTheWay = RayCheck(rayStartHead, rayEndHead) ||
                                    RayCheck(rayStartChest, rayEndChest) ||
                                    RayCheck(rayStartFeet, rayEndFeet);

                if (wallInTheWay)
                {
                    voiceAudio.volume *= 0.5f;
                    ApplyLowPassFilter(true, otherPlayer);
                }
                else
                {
                    ApplyLowPassFilter(false, otherPlayer);
                }
            }
            else
            {
                voiceAudio.volume = 0f;
            }
        }
    }

    // The voice AudioSource lives on the VoiceChat component's own GameObject, which is a CHILD
    // of the player root this list holds - a plain GetComponent on the root found nothing, so
    // every player was skipped (no proximity falloff at all) while logging a warning per player
    // per frame. Misses are cached as a null entry too, so a player that genuinely has no voice
    // source costs one lookup instead of one GetComponent every frame forever.
    private AudioSource ResolveVoiceAudio(Transform otherPlayer)
    {
        if (audioSourceCache.TryGetValue(otherPlayer, out AudioSource cached) && cached != null)
        {
            return cached;
        }

        if (cached == null && audioSourceCache.ContainsKey(otherPlayer))
        {
            return null;
        }

        AudioSource resolved = null;
        VoiceChat voiceChat = otherPlayer.GetComponentInChildren<VoiceChat>(true);
        if (voiceChat != null && voiceChat.audioSource != null)
        {
            resolved = voiceChat.audioSource;
        }
        else
        {
            resolved = otherPlayer.GetComponentInChildren<AudioSource>(true);
        }

        audioSourceCache[otherPlayer] = resolved;
        if (resolved == null)
        {
            Debug.LogWarning($"VoiceChatRaycast: no voice AudioSource under {otherPlayer.name}; proximity voice is disabled for them.");
        }

        return resolved;
    }

    // Function to check if a ray hits a wall
    private bool RayCheck(Vector3 startPosition, Vector3 endPosition)
    {
        Vector3 direction = (endPosition - startPosition).normalized;
        float distance = Vector3.Distance(startPosition, endPosition);

        if (Physics.Raycast(startPosition, direction, out RaycastHit hit, Mathf.Min(distance, maxDistance), wallLayerMask))
        {
            Debug.DrawRay(startPosition, direction * distance, Color.red);  // Visualize the ray
            return true;  // Hit a wall
        }
        else
        {
            Debug.DrawRay(startPosition, direction * distance, Color.green);  // Visualize the ray
            return false;  // No wall
        }
    }

    // Function to apply low-pass filter
    void ApplyLowPassFilter(bool enable, Transform otherPlayer)
    {
        if (otherPlayer.TryGetComponent<AudioLowPassFilter>(out var filter))
        {
            filter.enabled = enable;
        }
        if (otherPlayer.TryGetComponent<AudioReverbFilter>(out var reverbFilter))
        {
            reverbFilter.enabled = enable;
        }
    }

    // This method is called when a new player connects to the game
    private void OnPlayerConnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogWarning("VoiceChatRaycast: NetworkManager or SpawnManager is null!");
            return;
        }

        var playerNetObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        if (playerNetObj == null)
        {
            Debug.LogWarning($"VoiceChatRaycast: Player network object not found for clientId {clientId}");
            return;
        }

        GameObject playerObject = playerNetObj.gameObject;
        if (playerObject != null && playerObject.CompareTag("Player") && playerObject != gameObject)
        {
            // Add the new player to the list
            otherPlayers.Add(playerObject.transform);
        }
    }
    
    private void OnPlayerDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogWarning("VoiceChatRaycast: NetworkManager or SpawnManager is null!");
            return;
        }

        var playerNetObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        GameObject playerObject = playerNetObj?.gameObject;
        
        if (playerObject != null && playerObject.CompareTag("Player"))
        {
            // Remove the disconnected player from the list
            otherPlayers.Remove(playerObject.transform);
            audioSourceCache.Remove(playerObject.transform);
        }
    }

    // Add already existing players to the list on start
    private void AddExistingPlayers()
    {
        foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (player != gameObject)
            {
                otherPlayers.Add(player.transform);
            }
        }
    }
}
