using UnityEngine;
using Steamworks;
using Unity.Netcode;
using TMPro;
using Unity.Collections;
using System.Collections;
using Unity.VisualScripting;

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
            playerName.Value = SteamClient.Name;
            userName.gameObject.SetActive(false);
        }
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
    }
}
