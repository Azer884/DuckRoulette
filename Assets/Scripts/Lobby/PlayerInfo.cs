using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using System;

public class PlayerInfo : MonoBehaviour
{
    public string steamName;
    public RawImage profilePic;
    public Image readyStatus;
    public ulong steamId;
    public bool isReady;
    public bool haveEoughCoins;

    private void Start()
    {
        GetComponent<Button>().onClick.AddListener(OnButtonClick);
    }

    private void OnButtonClick()
    {
        transform.GetChild(1).gameObject.SetActive(true);
        ClickMenu.Instance.gameObject.SetActive(true);
        ClickMenu.Instance.playerName.text = steamName;
    }

    void Update()
    {
        // Detect left mouse button click
        if (Input.GetMouseButtonDown(0))
        {
            transform.GetChild(1).gameObject.SetActive(ClickMenu.Instance.IsPointerOverUIObject(gameObject));
            ClickMenu.Instance.gameObject.SetActive(ClickMenu.Instance.IsPointerOverUIObject(gameObject));
    }
    }
}
