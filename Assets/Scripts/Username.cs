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
        // FixedString32Bytes' string constructor throws past 29 UTF-8 bytes (a long or non-Latin
        // Steam name), and the name is rendered for every player - truncate and sanitize.
        playerName.Value = new FixedString32Bytes(RpcValidation.TruncateUtf8(RpcValidation.SanitizeChatMessage(name, 64), 29));
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
