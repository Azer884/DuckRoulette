using Steamworks;
using UnityEngine;

public class Coin : MonoBehaviour 
{
    public static Coin Instance;
    public int amount = 100;

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
            amount = 100; // Default coin amount if no save data is found
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
        amount += value;
        CoinChanged?.Invoke(); // Notify that the coin amount has changed
    }
}