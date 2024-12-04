using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TogglesHandler : MonoBehaviour
{
    public Color whiteColor, orangeColor;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TextColorChanger();
    }

    // Update is called once per frame
    public void TextColorChanger()
    {
        foreach (Transform child in transform)
        {
            if (child.GetComponent<Toggle>().isOn)
            {
                child.GetComponentInChildren<TextMeshProUGUI>().color = orangeColor;
            }
            else
            {
                child.GetComponentInChildren<TextMeshProUGUI>().color = whiteColor;
            }
        }
    }
}
