using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System;
using Netcode.Transports.Facepunch;
using Steamworks;

public class PauseMenu : NetworkBehaviour
{

    private InputActionAsset inputActions;
    [SerializeField] private GameObject pauseMenu;
    private bool menuIsOpen = false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;

        inputActions = GetComponent<InputSystem>().inputActions;
    }

    // Update is called once per frame
    void Update()
    {
        if (inputActions.FindAction("Pause").triggered)
        {
            if (!menuIsOpen)
            {
                Pause();
            }
            else
            {
                Resume();
            }
        }
    }

    public void Leave()
    {
        if (!IsHost)
        {
            LeaveGame();
        }
        else
        {
            KickAllPlayersClientRpc();
        }
    }
    public void GoBackToLobby()
    {
        PlayerSpawner.Instance.isStarted = false;
        Cursor.lockState = CursorLockMode.Confined;

        if (IsHost)
        {
            // Host loads the lobby scene and then notifies clients
            NetworkManager.Singleton.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
            NotifyClientsToGoBackToLobbyClientRpc();
        }
    }

    [ClientRpc]
    private void NotifyClientsToGoBackToLobbyClientRpc()
    {
        // Clients load the lobby scene
        if (!IsHost)
        {
            SceneManager.LoadScene("Lobby");
        }
    }

    private void LeaveGame()
    {
        LeaveSteamLobby();

        PlayerSpawner.Instance.isStarted = false;
        Cursor.lockState = CursorLockMode.Confined;
        if (!IsHost)
        {
            SceneManager.LoadScene("Lobby");
        }
        else
        {
            NetworkManager.Singleton.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
        }
    
        NetworkManager.Singleton.Shutdown();
    }

    [ClientRpc]
    private void KickAllPlayersClientRpc()
    {
        LeaveGame();
    }

    private void LeaveSteamLobby()
    {
        if (SteamClient.IsValid && LobbySaver.instance.currentLobby != null)
        {
            LobbySaver.instance.currentLobby?.Leave();

            LobbyManager.instance.playerInfo.Remove(OwnerClientId);
            Debug.Log("Left Steam lobby successfully.");
        }
    }

    public void Settings()
    {

    }

    public void Resume()
    {
        RebindSaveLoad.Instance.input.enabled = true;

        pauseMenu.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        menuIsOpen = false;

    }

    public void Pause()
    {
        RebindSaveLoad.Instance.input.enabled = false;

        pauseMenu.SetActive(true);
        Cursor.lockState = CursorLockMode.Confined;
        menuIsOpen = true;
    }

    private void OnApplicationQuit() 
    {
        if (IsHost)
        {
            KickAllPlayersClientRpc();    
        }
    }
}
