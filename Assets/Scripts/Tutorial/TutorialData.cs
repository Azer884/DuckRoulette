using UnityEngine;
using Steamworks;
using System;

[Serializable]
public class TutorialData
{
    public bool hasCompletedTutorial = false;
}

public static class Tutorial
{
    private const string FileName = "TutorialData.json";

    public static TutorialData Data = new();

    public static void Load()
    {
        if (!SteamClient.IsValid) return;

        if (SteamRemoteStorage.FileExists(FileName))
        {
            var bytes = SteamRemoteStorage.FileRead(FileName);
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            Data = JsonUtility.FromJson<TutorialData>(json);
        }
        else
        {
            Data = new TutorialData(); // default values
        }
    }

    public static void Save()
    {
        if (!SteamClient.IsValid) return;

        string json = JsonUtility.ToJson(Data, true);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        SteamRemoteStorage.FileWrite(FileName, bytes);
    }
}
