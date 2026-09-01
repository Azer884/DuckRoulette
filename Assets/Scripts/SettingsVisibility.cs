using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class SettingsVisibility : NetworkBehaviour
{
    [SerializeField] private GameObject settingsPanel, statsPanel;

    private InputAction returnAction;

    // Awake is called when the script instance is being loaded
    public override void OnNetworkSpawn()
    {
        settingsPanel.SetActive(true);
        settingsPanel.SetActive(false);

        statsPanel.SetActive(true);

        // Only the local player's own pause UI reacts to input.
        if (!IsOwner)
        {
            return;
        }

        // The in-game settings panel had no close-on-B path at all. Settings.cs owns that gamepad
        // handling but only exists in the Lobby scene - in GameScene this panel hangs off the
        // player prefab and is opened straight from the pause menu, so a controller player could
        // open it and then had no way back out without reaching for a cursor.
        InputSystem inputSystem = GetComponentInParent<InputSystem>();
        returnAction = inputSystem != null && inputSystem.inputActions != null
            ? inputSystem.inputActions.FindAction("Return")
            : null;

        if (returnAction != null)
        {
            returnAction.performed += HandleReturn;
        }
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (returnAction != null)
        {
            returnAction.performed -= HandleReturn;
            returnAction = null;
        }
    }

    // Same gamepad-only gate as Settings.HandleReturn in the Lobby: "Return" also carries a
    // Keyboard/Escape binding, and PauseMenu's own "Pause" action already handles Escape - reacting
    // to it here too would close the settings panel and unpause the game in the same frame.
    private void HandleReturn(InputAction.CallbackContext context)
    {
        if (context.control?.device is Gamepad && settingsPanel != null && settingsPanel.activeSelf)
        {
            settingsPanel.SetActive(false);
        }
    }
}
