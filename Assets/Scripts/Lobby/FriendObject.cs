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
        GameObject settingsBar = GameObject.FindGameObjectWithTag("SettingsBar");
        if (settingsBar != null && settingsBar.TryGetComponent(out Animator settingsAnim))
        {
            settingsAnim.Play("Settings");
        }

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
        float timeout = Time.time + 15f;
        while (LobbySaver.instance.currentLobby == null)
        {
            if (Time.time >= timeout)
            {
                Debug.LogWarning("Timed out waiting for lobby creation; invite not sent.");
                yield break;
            }
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