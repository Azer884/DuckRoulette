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

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Start()
    {
        // Automatically add existing players
        AddExistingPlayers();

        // Subscribe to new players joining
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
    }
    private void OnDisable()
    {
        // Unsubscribe when the object is destroyed
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnPlayerDisconnected;
        }
    }

    private void Update()
    {
        foreach (Transform otherPlayer in otherPlayers)
        {
            if (otherPlayer == null) continue;

            float distanceToOtherPlayer = Vector3.Distance(transform.position, otherPlayer.position);
            AudioSource voiceAudio = otherPlayer.GetComponent<AudioSource>();

            if (distanceToOtherPlayer <= maxDistance)
            {
                float proximityVolume = 1f - (distanceToOtherPlayer / maxDistance);  // Closer = louder
                voiceAudio.volume = Mathf.Clamp(proximityVolume, 0f, 1f);

                Vector3 rayStartHead = transform.position + Vector3.up * headHeight;
                Vector3 rayEndHead = otherPlayer.position + Vector3.up * headHeight;

                Vector3 rayStartChest = transform.position + Vector3.up * chestHeight;
                Vector3 rayEndChest = otherPlayer.position + Vector3.up * chestHeight;

                Vector3 rayStartFeet = transform.position + Vector3.up * feetHeight;
                Vector3 rayEndFeet = otherPlayer.position + Vector3.up * feetHeight;

                bool wallInTheWay = RayCheck(rayStartHead, rayEndHead) &&
                                    RayCheck(rayStartChest, rayEndChest) &&
                                    RayCheck(rayStartFeet, rayEndFeet);

                if (wallInTheWay)
                {
                    Debug.Log("Wall detected between players");
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
    private void OnPlayerConnected(ulong clientId)
    {
        GameObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId).gameObject;
        if (playerObject != null && playerObject.CompareTag("Player") && playerObject != gameObject)
        {
            // Add the new player to the list
            otherPlayers.Add(playerObject.transform);
        }
    }
    private void OnPlayerDisconnected(ulong clientId)
    {
        GameObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId)?.gameObject;
        if (playerObject != null && playerObject.CompareTag("Player"))
        {
            // Remove the disconnected player from the list
            otherPlayers.Remove(playerObject.transform);
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
