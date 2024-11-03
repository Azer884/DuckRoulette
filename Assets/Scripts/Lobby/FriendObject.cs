using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

public class FriendObject : MonoBehaviour
{
    public float inviteCooldown = 5f;
    private float lastInviteTime = -5f; 
    public SteamId steamid;

    public void Invite()
    {
        if (Time.time >= lastInviteTime + inviteCooldown)
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