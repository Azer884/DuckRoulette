using Steamworks;
using UnityEngine;

public class Coin : MonoBehaviour 
{
    public static Coin Instance;

    public const int DefaultAmount = 100;

    /// <summary>Biggest single change UpdateCoinAmount accepts. A match pays out a few dozen at
    /// most; anything larger is a bug or a cheat.</summary>
    public const int MaxSingleChange = 500;

    // The balance is never held as a plain int: memory scanners (Cheat Engine) find and freeze a
    // value by searching for the number shown on screen. It is stored XOR-masked with a random key
    // plus a checksum; a value edited in memory fails the checksum and snaps back to the last one
    // this script set itself.
    private int maskedAmount;
    private int mask;
    private int checksum;
    private int lastTrusted = DefaultAmount;
    private bool initialized;

    public int amount
    {
        get
        {
            if (!initialized)
            {
                return DefaultAmount;
            }

            int value = maskedAmount ^ mask;
            if (Checksum(value) != checksum)
            {
                Debug.LogWarning("Coin balance was modified outside the game; restoring it.");
                Store(lastTrusted);
                return lastTrusted;
            }
            return value;
        }
        set => Store(value);
    }

    private void Store(int value)
    {
        mask = Random.Range(int.MinValue, int.MaxValue);
        maskedAmount = value ^ mask;
        checksum = Checksum(value);
        lastTrusted = value;
        initialized = true;
    }

    private int Checksum(int value) => unchecked((value * 0x5bd1e995) ^ (mask >> 3) ^ 0x2F6A1C3D);

    public delegate void OnCoinChanged();
    public static event OnCoinChanged CoinChanged;

    #region SaveAndLoad
    public void SaveCoin()
    {
        SaveSystem.Save(this);
    }

    public void LoadCoin()
    {
        CoinData data = SaveSystem.LoadCoin();

        if (data != null) // Check if the save data was loaded successfully
        {
            amount = data.coinAmount; // Set the coin amount from the loaded data
            CoinChanged?.Invoke(); // Notify the UI or other listeners about the change
        }
        else if (SteamClient.IsValid)
        {
            Debug.LogWarning("using default amount.");
            amount = DefaultAmount; // Default coin amount if no save data is found
        }
        else
        {
            Debug.LogWarning("Open Steam!");
        }
    }


    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Makes sure this object persists across scenes
        }
        else
        {
            Destroy(gameObject); // Destroys duplicates if they exist
        }
    }

    void Start()
    {
        LoadCoin();
    }

    void OnApplicationQuit()
    {
        SaveCoin();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveCoin();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveCoin();
        }
    }
    #endregion

    public void UpdateCoinAmount(int value)
    {
        if (Mathf.Abs(value) > MaxSingleChange)
        {
            Debug.LogWarning($"Rejected coin change of {value} (limit {MaxSingleChange}).");
            return;
        }

        // Can't go below zero; long math so a huge balance can't wrap around.
        amount = (int)System.Math.Clamp((long)amount + value, 0L, int.MaxValue);
        CoinChanged?.Invoke(); // Notify that the coin amount has changed
    }
}