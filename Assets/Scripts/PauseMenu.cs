using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PauseMenu : NetworkBehaviour
{

    private InputActionAsset inputActions;
    [SerializeField] private GameObject pauseMenu, crosshair;
    public GameObject endGamePanel, playerStatsObj;
    private bool menuIsOpen = false;
    private bool ended = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
        ended = false;

        inputActions = GetComponent<InputSystem>().inputActions;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnect;
    }
    private void OnDisable() 
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnect;
    }


    // Update is called once per frame
    void Update()
    {
        if (inputActions.FindAction("Pause").triggered && !ended)
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

    private void OnDisconnect(ulong clientId)
    {
        if (clientId == 0)
        {
            GameManager.Instance.LeaveGame();
        }
    }

    public void Leave()
    {
        GameManager.Instance.LeaveGame();
    }

    public void Resume()
    {
        RebindSaveLoad.Instance.input.enabled = true;

        pauseMenu.SetActive(false);
        crosshair.SetActive(true);
        Cursor.lockState = CursorLockMode.Locked;
        menuIsOpen = false;

    }

    public void Pause()
    {
        RebindSaveLoad.Instance.input.enabled = false;

        pauseMenu.SetActive(true);
        crosshair.SetActive(false);
        Cursor.lockState = CursorLockMode.Confined;
        menuIsOpen = true;
    }
    public void End()
    {
        RebindSaveLoad.Instance.input.enabled = false;

        endGamePanel.SetActive(true);
        crosshair.SetActive(false);
        Cursor.lockState = CursorLockMode.Confined;
        ended = true;
    }

    private void OnApplicationQuit() 
    {
        Leave();
    }
}
