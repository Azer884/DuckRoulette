using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

public class FriendObject : MonoBehaviour
{
    public SteamId steamid;

    public void Invite()
    {
        GameNetworkManager.Instance.CurrentLobby.Value.InviteFriend(steamid);
        Debug.Log("Invited " + steamid);
    }

}