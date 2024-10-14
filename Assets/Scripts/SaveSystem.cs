using Steamworks;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

public static class SaveSystem
{
    private static readonly string steamCloudFileName = "Coin.Value"; // The filename saved in Steam Cloud

    public static void Save(Coin coin)
    {
        BinaryFormatter formatter = new();
        using (MemoryStream memoryStream = new())
        {
            CoinData data = new(coin);
            formatter.Serialize(memoryStream, data);

            // Write the data to Steam Cloud
            byte[] byteArray = memoryStream.ToArray();
            bool success = SteamRemoteStorage.FileWrite(steamCloudFileName, byteArray);
            
            if (success)
            {
                Debug.Log("File saved successfully to Steam Cloud.");
            }
            else
            {
                Debug.LogError("Failed to save file to Steam Cloud.");
            }
        }
    }

    public static CoinData LoadCoin()
    {
        if (SteamRemoteStorage.FileExists(steamCloudFileName))
        {
            Debug.Log("Found save file in Steam Cloud.");

            byte[] byteArray = SteamRemoteStorage.FileRead(steamCloudFileName);

            using MemoryStream memoryStream = new(byteArray);
            BinaryFormatter formatter = new();
            CoinData data = formatter.Deserialize(memoryStream) as CoinData;

            if (data != null)
            {
                Debug.Log("File loaded successfully from Steam Cloud.");
            }
            return data;
        }
        else
        {
            Debug.LogError($"Save file not found in Steam Cloud: {steamCloudFileName}");
            return null;
        }
    }

}
