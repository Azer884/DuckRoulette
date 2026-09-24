using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SteamFriendsManager : MonoBehaviour
{
    public TMP_InputField searchInput; // Assign this in the inspector.
    public RawImage pp;
    public TextMeshProUGUI playername;

    public Transform content;
    public GameObject friendObj;
    public Color onlineColor, inGameColor, offlineColor;
    private Dictionary<Friend, GameObject> allFriends = new();
    private Dictionary<Friend, GameObject> inGameFriends = new();
    private Dictionary<Friend, GameObject> onlineFriends = new();
    private Dictionary<Friend, GameObject> offlineFriends = new();
    private bool alphaOrder = false;



    async void Start()
    {
        if (!SteamClient.IsValid) return;

        playername.text = SteamClient.Name;
        InitFriendsAsync();

        searchInput.onValueChanged.AddListener(SearchFriends);

        var img = await SteamFriends.GetLargeAvatarAsync(SteamClient.SteamId);
        pp.texture = GetTextureFromImage(img.Value);

        SteamFriends.OnPersonaStateChange += OnFriendStateChange;
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMembershipChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMembershipChanged;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
    }

    // A friend already sitting in our current Steam lobby can't be invited again - the button is
    // greyed out and its name replaced with "In party" (FriendObject.SetInParty) instead of
    // silently doing nothing or letting a duplicate invite go out.
    private void OnLobbyMembershipChanged(Steamworks.Data.Lobby lobby, Friend member) => RefreshPartyMembership();

    private void OnLobbyEntered(Steamworks.Data.Lobby lobby) => RefreshPartyMembership();

    private readonly HashSet<ulong> partyMemberIds = new();
    private float partyRefreshTimer;

    // Steam's lobby callbacks can arrive before GameNetworkManager has stored the lobby in
    // LobbySaver (both listen to the same events), so a cheap once-a-second recheck backs them up.
    private void Update()
    {
        partyRefreshTimer += Time.unscaledDeltaTime;
        if (partyRefreshTimer >= 1f)
        {
            partyRefreshTimer = 0f;
            RefreshPartyMembership();
        }
    }

    public void RefreshPartyMembership()
    {
        partyMemberIds.Clear();
        var lobby = LobbySaver.instance != null ? LobbySaver.instance.currentLobby : null;
        if (lobby.HasValue)
        {
            foreach (Friend member in lobby.Value.Members)
            {
                partyMemberIds.Add(member.Id.Value);
            }
        }

        foreach (var kvp in allFriends)
        {
            if (kvp.Value != null && kvp.Value.TryGetComponent(out FriendObject friendObject))
            {
                friendObject.SetInParty(partyMemberIds.Contains(kvp.Key.Id.Value));
            }
        }
    }


    public static Texture2D GetTextureFromImage(Steamworks.Data.Image image)
    {
        int width = (int)image.Width;
        int height = (int)image.Height;
        Texture2D texture = new(width, height);

        // Batch into a Color32[] and upload once instead of one SetPixel call per pixel
        // (SetPixel is extremely slow - a 184x184 avatar was ~34k individual calls per friend).
        Color32[] pixels = new Color32[width * height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var p = image.GetPixel(x, y);
                int destY = height - y; // preserves original SetPixel(x, height - y, ...) flip/bounds behavior
                if (destY >= 0 && destY < height)
                {
                    pixels[destY * width + x] = new Color32(p.r, p.g, p.b, p.a);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    public void InitFriendsAsync()
    {
        inGameFriends.Clear();
        onlineFriends.Clear();
        offlineFriends.Clear();
        allFriends.Clear();

        // Categorize friends
        foreach (var friend in SteamFriends.GetFriends())
        {
            if (!allFriends.ContainsKey(friend))
            {
                GameObject friendObject = CreateFriendObject(friend, friend.IsOnline);
                allFriends[friend] = friendObject;

                if (friend.IsPlayingThisGame)
                {
                    inGameFriends[friend] = friendObject;
                }
                else if (friend.IsOnline)
                {
                    onlineFriends[friend] = friendObject;
                }
                else
                {
                    offlineFriends[friend] = friendObject;
                }
            }
        }

        alphaOrder = true;
        UpdateFriendUI();
        RefreshPartyMembership();
    }

    public void UpdateFriendUI()
    {
        if (alphaOrder)
        {
            int index = 0;
    
            foreach (var friend in inGameFriends.Values)
            {
                friend.transform.SetSiblingIndex(index++);
            }
    
            foreach (var friend in onlineFriends.Values)
            {
                friend.transform.SetSiblingIndex(index++);
            }
    
            foreach (var friend in offlineFriends.Values)
            {
                friend.transform.SetSiblingIndex(index++);
            }
            alphaOrder = false;
        }
    }

    public void SortFriendsAlphabetically()
    {
        if (!alphaOrder)
        {
            // Combine all friends into a single list for sorting
            var allFriends = new List<(Friend friend, GameObject obj)>();
            allFriends.AddRange(inGameFriends.Select(kvp => (kvp.Key, kvp.Value)));
            allFriends.AddRange(onlineFriends.Select(kvp => (kvp.Key, kvp.Value)));
            allFriends.AddRange(offlineFriends.Select(kvp => (kvp.Key, kvp.Value)));
    
            // Sort friends alphabetically by their name
            allFriends.Sort((a, b) => string.Compare(a.friend.Name, b.friend.Name, StringComparison.OrdinalIgnoreCase));
    
            // Rearrange the UI
            int index = 0;
            foreach (var (_, obj) in allFriends)
            {
                obj.transform.SetSiblingIndex(index++);
            }
            alphaOrder = true;
        }
    }
    
    private GameObject CreateFriendObject(Friend friend, bool online)
    {
        GameObject f = Instantiate(friendObj, content);
        FriendObject friendObject = f.GetComponent<FriendObject>();
        friendObject.playerName.text = friend.Name;
        friendObject.steamid = friend.Id;
        AssingFriendImage(f, friend.Id);
        friendObject.SetAvailable(online);
        //friendObject.onlineStats.color = statusColor;

        return f;
    }


    public async void AssingFriendImage(GameObject f, SteamId id)
    {
        var img = await SteamFriends.GetLargeAvatarAsync(id);
        f.GetComponentInChildren<RawImage>().texture = GetTextureFromImage(img.Value);
    }
    private void OnFriendStateChange(Friend friend)
    {
        // `friend` only ever lives in whichever of the three status dictionaries matched
        // its status at InitFriendsAsync time - it must be moved to the new one here,
        // otherwise the very first status change throws KeyNotFoundException.
        if (!allFriends.TryGetValue(friend, out GameObject friendUI))
        {
            return;
        }

        inGameFriends.Remove(friend);
        onlineFriends.Remove(friend);
        offlineFriends.Remove(friend);

        // Update the text color based on the friend's new status
        if (friend.IsPlayingThisGame)
        {
            inGameFriends[friend] = friendUI;
            friendUI.GetComponent<FriendObject>().SetAvailable(true);
            if (!alphaOrder)
            {
                friendUI.transform.SetAsFirstSibling();
            }
            //friendUI.GetComponent<FriendObject>().onlineStats.color = inGameColor;
        }
        else if (friend.IsOnline)
        {
            onlineFriends[friend] = friendUI;
            friendUI.GetComponent<FriendObject>().SetAvailable(true);
            if (!alphaOrder)
            {
                friendUI.transform.SetAsFirstSibling();
            }
            //friendUI.GetComponent<FriendObject>().onlineStats.color = onlineColor;
        }
        else
        {
            //friendUI.GetComponent<FriendObject>().onlineStats.color = offlineColor;
            offlineFriends[friend] = friendUI;
            friendUI.GetComponent<FriendObject>().SetAvailable(false);
            if (!alphaOrder)
            {
                friendUI.transform.SetAsFirstSibling();
            }
        }
    }

    public void SearchFriends(string query)
    {
        query = query.ToLower();

        foreach (var kvp in allFriends)
        {
            string friendName = kvp.Key.Name.ToLower();
            GameObject friendObject = kvp.Value;

            // Show or hide the friend based on the search query
            friendObject.SetActive(string.IsNullOrWhiteSpace(query) || friendName.Contains(query));
        }
    }

    
    private void OnDestroy() {
        SteamFriends.OnPersonaStateChange -= OnFriendStateChange;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMembershipChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMembershipChanged;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
    }
}