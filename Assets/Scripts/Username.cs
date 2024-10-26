using UnityEngine;
using Steamworks;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class Username : NetworkBehaviour
{
    private NetworkVariable<FixedString32Bytes> playerName = new();
    private bool nameTagSet = false;
    [SerializeField] private TextMeshProUGUI userName;

    [SerializeField] private Camera mainCamera;

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
        userName.transform.LookAt(userName.transform.position + mainCamera.transform.rotation * Vector3.forward, mainCamera.transform.rotation * Vector3.up);
    }
}
