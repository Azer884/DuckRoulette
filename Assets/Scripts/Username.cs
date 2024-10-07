using UnityEngine;
using Steamworks;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class Username : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI userName;
    private Camera mainCamera;

    private NetworkVariable<FixedString32Bytes> playerName = new();

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Set the player name using Steam name
            string steamName = SteamClient.Name;
            playerName.Value = steamName;

            // Hide the name tag for the local player
            userName.gameObject.SetActive(false);
        }
        else
        {
            // Set the name tag for other players
            userName.text = playerName.Value.ToString();
        }

        // Get the main camera
        mainCamera = Camera.main;
    }

    private void Update()
    {
        // Make sure the camera and user name are assigned
        if (mainCamera != null && userName != null && !IsOwner)
        {
            // Make the username look at the camera
            userName.transform.LookAt(mainCamera.transform);
            userName.transform.Rotate(0, 180, 0); // Adjust rotation so the text is readable
        }
    }
}
