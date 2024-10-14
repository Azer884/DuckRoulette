using UnityEngine;

[System.Serializable]
public class CoinData
{
    public int coinAmount;

    public CoinData (Coin coin)
    {
        coinAmount = coin.amount;
    }
}
