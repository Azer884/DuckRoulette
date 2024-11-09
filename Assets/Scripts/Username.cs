using UnityEngine;
using Steamworks;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class Username : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> playerName = new();
    private bool nameTagSet = false;
    public TextMeshProUGUI userName;

    [SerializeField] private Camera ownerCamera;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            SetPlayerNameServerRpc(SteamClient.Name);
            userName.gameObject.SetActive(false);
        }
    }

    [ServerRpc]
    private void SetPlayerNameServerRpc(string name)
    {
        playerName.Value = new FixedString32Bytes(name);
    }

    public void SetOverlay()
    {
        userName.text = playerName.Value.ToString();
    }

    private void Update() {
        if (!nameTagSet && !string.IsNullOrEmpty(playerName.Value.ToString()))
        {
            SetOverlay();
            nameTagSet = true;
        }
        if (ownerCamera != null && !IsOwner)
        {
            userName.transform.LookAt(ownerCamera.transform);
        }
    }
}
