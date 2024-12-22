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
    }


    public static Texture2D GetTextureFromImage(Steamworks.Data.Image image)
    {
        Texture2D texture = new((int)image.Width, (int)image.Height);

        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                var p = image.GetPixel(x, y);
                texture.SetPixel(x, (int)image.Height - y, new Color(p.r / 255.0f, p.g / 255.0f, p.b / 255.0f, p.a / 255.0f));
            }
        }
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
        friendObject.GetComponent<Button>().interactable = online;
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
        // Update the text color based on the friend's new status
        if (friend.IsPlayingThisGame)
        {
            inGameFriends[friend].GetComponent<Button>().interactable = true;
            if (!alphaOrder)
            {
                inGameFriends[friend].transform.SetAsFirstSibling();
            }
            //friendUI.GetComponent<FriendObject>().onlineStats.color = inGameColor;
        }
        else if (friend.IsOnline)
        {
            onlineFriends[friend].GetComponent<Button>().interactable = true;
            if (!alphaOrder)
            {
                onlineFriends[friend].transform.SetAsFirstSibling();
            }
            //friendUI.GetComponent<FriendObject>().onlineStats.color = onlineColor;
        }
        else
        {
            //friendUI.GetComponent<FriendObject>().onlineStats.color = offlineColor;
            offlineFriends[friend].GetComponent<Button>().interactable = false;
            if (!alphaOrder)
            {
                offlineFriends[friend].transform.SetAsFirstSibling();
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
    }
}