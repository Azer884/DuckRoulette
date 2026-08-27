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

    private GameObject arrow;

    private void Awake()
    {
        arrow = transform.Find("Arrow").gameObject;
        GetComponent<Button>().onClick.AddListener(OnButtonClick);
    }

    private void OnButtonClick()
    {
        ulong _clientId = 0;
        foreach (var _player in LobbyManager.instance.playerInfo)
        {
            if (_player.Value == gameObject)
            {
                _clientId = _player.Key;
                break;
            }
        }

        ClickMenu.Instance.playerName.text = steamName;
        ClickMenu.Instance.targetSteamId = steamId;
        ClickMenu.Instance.targetClientId = _clientId;
        ClickMenu.Instance.targetName = steamName;
        ClickMenu.Instance.kickButton.SetActive(LobbyManager.instance.isHost && steamId != (ulong)SteamClient.SteamId);
        ClickMenu.Instance.messageButton.SetActive(steamId != (ulong)SteamClient.SteamId);
        ClickMenu.Instance.moreButton.SetActive(true);
        arrow.SetActive(true);
        ClickMenu.Instance.gameObject.SetActive(true);
    }

    void Update()
    {
        // Detect left mouse button click
        if (Input.GetMouseButtonDown(0))
        {
            bool overThisCard = ClickMenu.Instance.IsPointerOverUIObject(gameObject);
            arrow.SetActive(overThisCard);
            ClickMenu.Instance.gameObject.SetActive(overThisCard);
        }
    }
}
