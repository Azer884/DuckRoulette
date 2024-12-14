using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using System;
using Steamworks;

public class PlayerInfo : MonoBehaviour
{
    public string steamName;
    public RawImage profilePic;
    public Image readyStatus;
    public ulong steamId;
    public bool isReady;
    public bool haveEoughCoins;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnButtonClick);
    }

    private void OnButtonClick()
    {
        ClickMenu.Instance.playerName.text = steamName;
        ClickMenu.Instance.kickButton.SetActive(LobbyManager.instance.isHost && steamId != (ulong)SteamClient.SteamId);
        transform.GetChild(1).gameObject.SetActive(true);
        ClickMenu.Instance.gameObject.SetActive(true);
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
