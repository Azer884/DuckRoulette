using Steamworks;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Coins live in the player's own Steam Cloud, so the game has no server to ask what they really
// own. What this does protect against is the common, easy cheat: opening Coin.Value in a text
// editor (or copying someone else's) and typing a bigger number.
//   - Every save is signed (HMAC-SHA256) together with the owner's SteamID. An edited amount, or a
//     file copied from another account, fails the check on load and is not trusted.
//   - Saves from before signing (no signature) still load once, capped at LegacyMaxCoins, and are
//     re-signed on the next save.
// It is not unbreakable - the key ships inside the game - but it takes decompiling the game rather
// than editing a text file. Truly cheat-proof coins need a server (e.g. Steam Inventory Service).
public static class SaveSystem
{
    private static readonly string steamCloudFileName = "Coin.Value"; // The filename saved in Steam Cloud

    /// <summary>Most an old, unsigned save may carry over.</summary>
    public const int LegacyMaxCoins = 1000;

    // Split so the key doesn't sit in the binary as one searchable string.
    private static readonly string[] KeyParts = { "dR-c0in", "$9fQ!wz", "7Lk#duck", "roul3tte" };

    public static void Save(Coin coin)
    {
        if (!SteamClient.IsValid) return;

        try
        {
            CoinData data = new(coin);
            data.steamId = SteamClient.SteamId.Value;
            data.signature = Sign(data.coinAmount, data.steamId);

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
        if (!SteamClient.IsValid) return null;

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
                    Verify(data, SteamClient.SteamId.Value);
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

    /// <summary>Checks a loaded save against its signature and the current account, fixing up
    /// coinAmount in place: tampered or foreign saves drop to 0, old unsigned ones are capped.
    /// Returns false when the save was not trusted as-is.</summary>
    public static bool Verify(CoinData data, ulong currentSteamId)
    {
        if (string.IsNullOrEmpty(data.signature))
        {
            // Pre-signing save: accept it, but not an arbitrary amount.
            int capped = Mathf.Clamp(data.coinAmount, 0, LegacyMaxCoins);
            bool untouched = capped == data.coinAmount;
            data.coinAmount = capped;
            return untouched;
        }

        if (data.steamId != currentSteamId || !FixedTimeEquals(data.signature, Sign(data.coinAmount, data.steamId)))
        {
            Debug.LogWarning("Coin save failed its integrity check (edited, or from another account); it was not trusted.");
            data.coinAmount = 0;
            return false;
        }

        data.coinAmount = Mathf.Max(0, data.coinAmount);
        return true;
    }

    public static string Sign(int coinAmount, ulong steamId)
    {
        byte[] key = Encoding.UTF8.GetBytes(string.Concat(KeyParts));
        byte[] payload = Encoding.UTF8.GetBytes($"{steamId}|{coinAmount}|v1");
        using var hmac = new HMACSHA256(key);
        byte[] hash = hmac.ComputeHash(payload);

        StringBuilder hex = new(hash.Length * 2);
        foreach (byte b in hash)
        {
            hex.Append(b.ToString("x2"));
        }
        return hex.ToString();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
