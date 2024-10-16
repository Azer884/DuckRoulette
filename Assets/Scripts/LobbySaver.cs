using UnityEngine;
using Steamworks.Data;

public class LobbySaver : MonoBehaviour
{
    public Lobby? currentLobby;
    public static LobbySaver instance;

    private void Awake() {
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}