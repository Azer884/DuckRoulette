using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerInfo : MonoBehaviour
{
    public TMP_Text playerName;
    public string steamName;
    public RawImage profilePic;
    public ulong steamId;
    public bool isReady;

    private void Start()
    {
        playerName.text = steamName;
    }
}
