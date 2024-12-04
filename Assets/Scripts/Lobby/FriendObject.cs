using UnityEngine;
using Steamworks;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;

public class FriendObject : MonoBehaviour
{
    public TextMeshProUGUI playerName;
    public Image onlineStats;
    private float inviteCooldown = 5f;
    private float lastInviteTime = -5f; 
    public SteamId steamid;

    public void Invite()
    {
        // Check for an existing lobby, create if none exists
        if (LobbySaver.instance.currentLobby == null)
        {
            Debug.Log("No lobby found. Creating a new one...");
            GameNetworkManager.Instance.StartHost(6);
            
            // Wait for the lobby to initialize
            StartCoroutine(WaitForLobbyCreationAndInvite());
        }
        else
        {
            SendInvite(false);
        }
    }

    private System.Collections.IEnumerator WaitForLobbyCreationAndInvite()
    {
        while (LobbySaver.instance.currentLobby == null)
        {
            yield return null; // Wait until the lobby is initialized
        }

        SendInvite(true);
    }

    private void SendInvite(bool isHosting)
    {
        if (Time.time >= lastInviteTime + inviteCooldown || isHosting)
        {
            LobbySaver.instance.currentLobby.Value.InviteFriend(steamid);
            Debug.Log("Invited " + steamid);
            lastInviteTime = Time.time;
        }
        else
        {
            Debug.Log("Please wait before sending another invite.");
        }
    }
}