using UnityEngine;
using Steamworks;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class Username : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> playerName = new();
    // Lets a client-side HUD (TeamUpRequestHUD's profile pic) resolve another player's Steam
    // avatar without a lobby-scene-only lookup (LobbyManager.playerInfo doesn't survive into
    // GameScene). Read permission Everyone, same as playerName - a Steam ID is not secret and
    // every peer already renders this player's name tag from this same component.
    public NetworkVariable<ulong> steamId = new();
    private bool nameTagSet = false;
    public TextMeshProUGUI userName;

    [SerializeField] private Camera ownerCamera;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            SetPlayerNameServerRpc(SteamClient.Name, SteamClient.SteamId);
            userName.gameObject.SetActive(false);
        }
    }

    [ServerRpc]
    private void SetPlayerNameServerRpc(string name, ulong senderSteamId)
    {
        // FixedString32Bytes' string constructor throws past 29 UTF-8 bytes (a long or non-Latin
        // Steam name), and the name is rendered for every player - truncate and sanitize.
        playerName.Value = new FixedString32Bytes(RpcValidation.TruncateUtf8(RpcValidation.SanitizeChatMessage(name, 64), 29));
        steamId.Value = senderSteamId;
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
