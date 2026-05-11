using Steamworks;
using System.Text;
using UnityEngine;

public static class SaveSystem
{
    private static readonly string steamCloudFileName = "Coin.Value"; // The filename saved in Steam Cloud

    public static void Save(Coin coin)
    {
        try
        {
            CoinData data = new(coin);
            string jsonData = JsonUtility.ToJson(data);
            byte[] byteArray = Encoding.UTF8.GetBytes(jsonData);
            
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
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving to Steam Cloud: {e.Message}");
        }
    }

    public static CoinData LoadCoin()
    {
        if (SteamRemoteStorage.FileExists(steamCloudFileName))
        {
            try
            {
                Debug.Log("Found save file in Steam Cloud.");

                byte[] byteArray = SteamRemoteStorage.FileRead(steamCloudFileName);
                string jsonData = Encoding.UTF8.GetString(byteArray);
                
                CoinData data = JsonUtility.FromJson<CoinData>(jsonData);

                if (data != null)
                {
                    Debug.Log("File loaded successfully from Steam Cloud.");
                }
                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading from Steam Cloud: {e.Message}");
                return null;
            }
        }
        else
        {
            Debug.LogWarning($"Save file not found in Steam Cloud: {steamCloudFileName}");
            return null;
        }
    }

}
