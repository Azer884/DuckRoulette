using UnityEngine;

[System.Serializable]
public class CoinData
{
    // Field name is part of the save format already in players' Steam Cloud - never rename it.
    public int coinAmount;

    // Added with signed saves (see SaveSystem). Old files have neither; they still load.
    public ulong steamId;
    public string signature;

    public CoinData (Coin coin)
    {
        coinAmount = coin.amount;
    }
}
