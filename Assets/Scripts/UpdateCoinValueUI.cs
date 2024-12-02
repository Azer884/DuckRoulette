using TMPro;
using UnityEngine;

public class UpdateCoinValueUI : MonoBehaviour 
{
    void OnEnable()
    {
        Coin.CoinChanged += UpdateCoinUI;
    }

    void OnDisable()
    {
        Coin.CoinChanged -= UpdateCoinUI;
    }
    private void Start() {
        UpdateCoinUI();
    }

    private void UpdateCoinUI()
    {
        transform.GetComponentInChildren<TextMeshProUGUI>().text = Coin.Instance.amount.ToString();
    }
}