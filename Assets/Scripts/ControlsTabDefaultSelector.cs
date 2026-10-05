using UnityEngine;
using UnityEngine.UI;

/// Controls tab always defaulted to the Keyboard sub-panel on open, even for a gamepad
/// player - there was no device check at all. On enable, pick the Keyboard or Controller
/// toggle based on the player's current control scheme so the right panel shows first.
public class ControlsTabDefaultSelector : MonoBehaviour
{
    [SerializeField] private Toggle keyboardToggle;
    [SerializeField] private Toggle controllerToggle;

    private void OnEnable()
    {
        bool isGamepad = RebindSaveLoad.Instance != null && RebindSaveLoad.Instance.currentControlScheme == "Gamepad";

        if (isGamepad && controllerToggle != null)
        {
            controllerToggle.isOn = true;
        }
        else if (!isGamepad && keyboardToggle != null)
        {
            keyboardToggle.isOn = true;
        }
    }
}
