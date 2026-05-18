using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GridManager : NetworkBehaviour
{
    public List<Transform> characters = new List<Transform>();
    public NetworkList<NetworkObjectReference> NetworkCharacters { get; private set; } = new NetworkList<NetworkObjectReference>();

    public static GridManager Instance { get; private set; }
    public event Action OnLobbyLayoutChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            return;
        }

        Destroy(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        NetworkCharacters.OnListChanged += OnCharactersListChanged;

        if (IsServer)
        {
            NetworkCharacters.Clear();
        }

        RefreshCharacterCache();
    }

    public override void OnNetworkDespawn()
    {
        NetworkCharacters.OnListChanged -= OnCharactersListChanged;
        base.OnNetworkDespawn();
    }

    public void AddCharacter(NetworkObject networkObject)
    {
        if (IsServer && networkObject != null)
        {
            var reference = new NetworkObjectReference(networkObject);
            if (!NetworkCharacters.Contains(reference))
            {
                NetworkCharacters.Add(reference);
            }

            RefreshCharacterCache();
        }
    }

    public void RemoveCharacter(NetworkObject networkObject)
    {
        if (!IsServer || networkObject == null)
            return;

        NetworkObjectReference reference = new(networkObject);
        if (NetworkCharacters.Remove(reference))
        {
            RefreshCharacterCache();
        }
    }

    private void OnCharactersListChanged(NetworkListEvent<NetworkObjectReference> changeEvent)
    {
        Debug.Log($"NetworkList changed. Event type: {changeEvent.Type}");

        RefreshCharacterCache();
    }

    private void RefreshCharacterCache()
    {
        characters.Clear();
        List<NetworkObjectReference> invalidReferences = null;

        foreach (var netObjRef in NetworkCharacters)
        {
            if (netObjRef.TryGet(out NetworkObject networkObject))
            {
                characters.Add(networkObject.transform);
                Debug.Log($"Character added: {networkObject.name}");
            }
            else
            {
                Debug.LogWarning("Failed to resolve NetworkObjectReference.");

                if (IsServer)
                {
                    invalidReferences ??= new List<NetworkObjectReference>();
                    invalidReferences.Add(netObjRef);
                }
            }
        }

        if (IsServer && invalidReferences != null)
        {
            foreach (var invalidReference in invalidReferences)
            {
                NetworkCharacters.Remove(invalidReference);
            }
        }

        if (IsServer)
        {
            ReassignCharactersToPrioritizedSlots();
        }

        OnLobbyLayoutChanged?.Invoke();
    }

    public void ReassignCharactersToPrioritizedSlots()
    {
        int characterIndex = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform slot = transform.GetChild(i);

            if (characterIndex < characters.Count)
            {
                Transform character = characters[characterIndex];
                if (character != null)
                {
                    NetworkObject networkObject = character.GetComponent<NetworkObject>();
                    if (networkObject != null && NetworkTransmission.instance != null)
                    {
                        // Update the character's position and rotation for every client.
                        NetworkTransmission.instance.ChangeObjectPosClientRpc(networkObject.NetworkObjectId, slot.position, slot.rotation);
                    }

                    character.SetPositionAndRotation(slot.position, slot.rotation);
                    characterIndex++;
                }
            }
        }

        // Remove any null references that might still be in the list
        characters.RemoveAll(item => item == null);
    }

    public int CurrentCharacterCount => NetworkCharacters.Count;


}
