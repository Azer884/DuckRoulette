using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System;

public class PlayerSpawner : NetworkBehaviour
{

    public static PlayerSpawner Instance;
    [SerializeField]private GameObject player;
    public bool isStarted;

    void Start() 
    {
        if(Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
       NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneLoaded;
    }

    private void SceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if(isStarted)return;
        if (IsHost && sceneName == "GameScene")
        {
            foreach (ulong id in clientsCompleted)
            {
                GameObject player0 = Instantiate(player);
                player0.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);

            }
        }
        isStarted = true;
    }
}