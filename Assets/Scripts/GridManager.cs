using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GridManager : NetworkBehaviour
{
    public List<Transform> characters = new List<Transform>();
    public NetworkList<NetworkObjectReference> NetworkCharacters { get; private set; } = new NetworkList<NetworkObjectReference>();

    public static GridManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkCharacters.Clear();
            NetworkCharacters.OnListChanged += OnCharactersListChanged;
        }
    }

    private void OnDisable()
    {
        if (IsServer)
        {
            NetworkCharacters.OnListChanged -= OnCharactersListChanged;
        }
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
        }
    }

    private void OnCharactersListChanged(NetworkListEvent<NetworkObjectReference> changeEvent)
    {
        Debug.Log($"NetworkList changed. Event type: {changeEvent.Type}");

        characters.Clear();

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
            }
        }

        ReassignCharactersToPrioritizedSlots();
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
                    // Call the RPC to update the character's position and rotation
                    NetworkTransmission.instance.ChangeObjectPosServerRpc(character.GetComponent<NetworkObject>().NetworkObjectId, slot.position, slot.rotation);
                    characterIndex++;
                }
            }
        }

        // Remove any null references that might still be in the list
        characters.RemoveAll(item => item == null);
    }


}
