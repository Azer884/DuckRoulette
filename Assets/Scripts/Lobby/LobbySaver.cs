using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks.Data;

public class LobbySaver : MonoBehaviour
{
    public Lobby? currentLobby;
    public static LobbySaver instance;

    private void Awake() {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // Makes sure this object persists across scenes
        }
        else
        {
            Destroy(gameObject); // Destroys duplicates if they exist
        }
    }
}
