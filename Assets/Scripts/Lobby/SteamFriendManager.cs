using System;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class SteamFriendsManager : MonoBehaviour
{
    public RawImage pp;
    public TextMeshProUGUI playername;

    public Transform content;
    public GameObject friendObj;
    public Color onlineColor, inGameColor, offlineColor;
    private Dictionary<SteamId, GameObject> friendObjects = new Dictionary<SteamId, GameObject>();
    private Dictionary<string, Transform> sectionHeaders = new();


    async void Start()
    {
        if (!SteamClient.IsValid) return;

        playername.text = SteamClient.Name;
        InitFriendsAsync();
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
        friendObjects.Clear();
        // Lists to hold categorized friends
        List<Friend> inGameFriends = new();
        List<Friend> onlineFriends = new();
        List<Friend> offlineFriends = new();

        // Categorize friends
        foreach (var friend in SteamFriends.GetFriends())
        {
            if (friend.IsPlayingThisGame)
            {
                inGameFriends.Add(friend);
            }
            else if (friend.IsOnline)
            {
                onlineFriends.Add(friend);
            }
            else
            {
                offlineFriends.Add(friend);
            }
        }

        // Instantiate friends in order: In-Game -> Online -> Offline
        if (inGameFriends.Count > 0)
        {
            sectionHeaders["In-Game"] = content.GetChild(0);

            foreach (Friend friend in inGameFriends)
            {
                CreateFriendObject(friend, inGameColor, true);
            }
        }
        else
        {
            content.GetChild(0).gameObject.SetActive(false);
        }

        if (onlineFriends.Count > 0)
        {
            GameObject onlineText = Instantiate(content.GetChild(0).gameObject, content);
            onlineText.SetActive(true);

            sectionHeaders["Online"] = onlineText.transform;

            foreach (Friend friend in onlineFriends)
            {
                CreateFriendObject(friend, onlineColor, false);
            }
        }
        if (offlineFriends.Count > 0)
        {
            GameObject offlineText = Instantiate(content.GetChild(0).gameObject, content);
            offlineText.SetActive(true);

            sectionHeaders["Offline"] = offlineText.transform;

            foreach (Friend friend in offlineFriends)
            {
                CreateFriendObject(friend, offlineColor, false);
            }
        }
    }

// Helper method to create and assign friend objects
    private void CreateFriendObject(Friend friend, Color statusColor, bool invite)
    {
        GameObject f = Instantiate(friendObj, content);
        f.GetComponentInChildren<TextMeshProUGUI>().text = friend.Name;
        f.GetComponent<FriendObject>().steamid = friend.Id;
        AssingFriendImage(f, friend.Id);
        f.GetComponentInChildren<TextMeshProUGUI>().color = statusColor;
        f.transform.GetChild(2).gameObject.SetActive(invite);

        friendObjects[friend.Id] = f;
    }


    public async void AssingFriendImage(GameObject f, SteamId id)
    {
        var img = await SteamFriends.GetLargeAvatarAsync(id);
        f.GetComponentInChildren<RawImage>().texture = GetTextureFromImage(img.Value);
    }
    private void OnFriendStateChange(Friend friend)
    {
        // Check if this friend is already in the dictionary
        if (friendObjects.TryGetValue(friend.Id, out GameObject friendUI))
        {
            // Update the text color based on the friend's new status
            if (friend.IsPlayingThisGame)
            {
                sectionHeaders["In-Game"].gameObject.SetActive(true);
                friendUI.GetComponentInChildren<TextMeshProUGUI>().color = inGameColor; 
                friendUI.transform.SetSiblingIndex(sectionHeaders["Online"].GetSiblingIndex() - 1);
                friendUI.transform.GetChild(2).gameObject.SetActive(true);
            }
            else if (friend.IsOnline)
            {
                sectionHeaders["Online"].gameObject.SetActive(true);
                friendUI.GetComponentInChildren<TextMeshProUGUI>().color = onlineColor;
                friendUI.transform.SetSiblingIndex(sectionHeaders["Offline"].GetSiblingIndex() - 1);
                friendUI.transform.GetChild(2).gameObject.SetActive(false);

            }
            else
            {
                sectionHeaders["Offline"].gameObject.SetActive(true);
                friendUI.GetComponentInChildren<TextMeshProUGUI>().color = offlineColor;
                friendUI.transform.SetSiblingIndex(content.childCount - 1);
                friendUI.transform.GetChild(2).gameObject.SetActive(false);
            }
        }
    }

    public string IncreaseNumberInString(string input)
{
    // Use regex to find a number in the string
    Match match = Regex.Match(input, @"\d+");
    
    if (match.Success)
    {
        // Get the number as a string
        string numberStr = match.Value;
        
        // Convert the number string to an integer and increase it by one
        int number = int.Parse(numberStr);
        number++;

        // Replace the old number in the input string with the new number
        string updatedString = Regex.Replace(input, numberStr, number.ToString());
        return updatedString;
    }

    // Return the input string unchanged if no number is found
    return input;
}

    public static async System.Threading.Tasks.Task<Texture2D> GetTextureFromSteamIdAsync(SteamId id)
    {
        var img = await SteamFriends.GetLargeAvatarAsync(SteamClient.SteamId);
        Steamworks.Data.Image image = img.Value;
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
    
    private void OnDestroy() {
        SteamFriends.OnPersonaStateChange -= OnFriendStateChange;
    }
}