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
    private Movement movement;
    private Shooting shooting;
    private Slap slap;
    private HideGun hideGun;
    private bool currentMove, currentShoot, currentSlap, currentHideGun;
    [SerializeField] private GameObject pauseMenu;
    private bool menuIsOpen = false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;

        movement = GetComponent<Movement>();
        shooting = GetComponent<Shooting>();
        slap = GetComponent<Slap>();
        hideGun = GetComponent<HideGun>();

        inputActions = GetComponent<InputSystem>().inputActions;
    }

    // Update is called once per frame
    void Update()
    {
        if (inputActions.FindAction("Pause").triggered)
        {
            if (!menuIsOpen)
            {
                currentMove = movement.enabled;
                currentShoot = shooting.enabled;
                currentSlap = slap.enabled;
                currentHideGun = hideGun.enabled;

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
            Debug.Log("Left Steam lobby successfully.");
        }
    }

    public void Settings()
    {

    }

    public void Resume()
    {
        pauseMenu.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        menuIsOpen = false;

        movement.enabled = currentMove;
        shooting.enabled = currentShoot;
        slap.enabled = currentSlap;
        hideGun.enabled = currentHideGun;
    }

    public void Pause()
    {
        movement.enabled = false;
        shooting.enabled = false;
        slap.enabled = false;
        hideGun.enabled = false;

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
