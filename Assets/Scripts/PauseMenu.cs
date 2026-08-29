using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System;

public class PauseMenu : NetworkBehaviour
{

    private InputActionAsset inputActions;
    private InputAction pauseAction;
    [SerializeField] private GameObject pauseMenu, crosshair;
    public GameObject endGamePanel, playerStatsObj;
    private bool menuIsOpen = false;
    private bool ended = false;
    public static event Action OnPause;
    public static event Action OnUnPause;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
        ended = false;

        inputActions = GetComponent<InputSystem>().inputActions;
        pauseAction = inputActions.FindAction("Pause");
    }


    // Update is called once per frame
    void Update()
    {
        if (pauseAction.triggered && !ended)
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.LeaveGame();
        }
    }

    public void Resume()
    {
        RebindSaveLoad.Instance.input.enabled = true;

        pauseMenu.SetActive(false);
            crosshair.transform.parent.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.Locked;
        menuIsOpen = false;
        OnUnPause?.Invoke();
    }

    public void Pause()
    {
        RebindSaveLoad.Instance.input.enabled = false;

        pauseMenu.SetActive(true);
        crosshair.transform.parent.gameObject.SetActive(false);
        // CursorLockMode.Confined re-applies a native Windows cursor clip region; going straight
        // from Locked to Confined in a standalone build leaves that clip stuck (cursor visible but
        // frozen) until the app loses and regains focus (alt+tab). None fully releases the clip
        // instead, which is the standard fix for this exact Unity/Windows issue.
        Cursor.lockState = CursorLockMode.None;
        menuIsOpen = true;
        OnPause?.Invoke();
    }
    public void End()
    {
        RebindSaveLoad.Instance.input.enabled = false;

        endGamePanel.SetActive(true);
        crosshair.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        ended = true;

        // InteractionPromptHUD is DontDestroyOnLoad and only ever hidden by Interact/TeamUp
        // reacting to OnPause, which End() never fired - so a prompt visible the instant the game
        // ended stayed stuck on screen, and kept getting re-shown every frame afterward since
        // neither script's own raycast/proximity check stops just because the round is over.
        // OnPause permanently suppresses both (Update()'s `!ended` guard above means Resume()
        // can never undo it after this).
        OnPause?.Invoke();
        InteractionPromptHUD.Hide();
    }

    private void OnApplicationQuit() 
    {
        Leave();
    }
}
