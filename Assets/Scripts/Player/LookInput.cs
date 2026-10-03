using UnityEngine;
using UnityEngine.InputSystem;

// Turns the "Look" action into degrees of rotation for this frame, the same way for the player
// camera and the hiding-spot camera. The action itself carries no scale processors: the mouse
// binding reports raw counts moved this frame and the gamepad binding a stick deflection in [-1, 1].
//
// The two need different maths. A mouse delta is already a distance - it accumulates however many
// frames it is split across - so it maps straight to degrees and must NOT be multiplied by
// deltaTime (doing so made mouse sensitivity drop as the framerate rose). A stick deflection is a
// rate, so it is degrees per second and does need deltaTime.
public static class LookInput
{
    public static bool IsGamepad(InputAction lookAction)
    {
        InputControl activeControl = lookAction != null ? lookAction.activeControl : null;
        return activeControl != null && activeControl.device is Gamepad;
    }

    public static Vector2 ReadDegrees(InputAction lookAction)
    {
        if (lookAction == null)
        {
            return Vector2.zero;
        }

        bool isGamepad = IsGamepad(lookAction);
        Vector2 look = lookAction.ReadValue<Vector2>();
        PlayerMovementTuning tuning = PlayerMovementTuning.Active;
        SettingsManager settings = SettingsManager.Instance;

        if (isGamepad)
        {
            float sensitivityX = settings != null ? settings.ControllerSensitivityX : SettingsManager.DefaultControllerSensitivity;
            float sensitivityY = settings != null ? settings.ControllerSensitivityY : SettingsManager.DefaultControllerSensitivity;
            float scale = tuning.gamepadLookDegreesPerSecond * Time.deltaTime;
            return new Vector2(look.x * sensitivityX * scale, look.y * sensitivityY * scale);
        }

        float mouseSensitivityX = settings != null ? settings.MouseSensitivityX : SettingsManager.DefaultMouseSensitivity;
        float mouseSensitivityY = settings != null ? settings.MouseSensitivityY : SettingsManager.DefaultMouseSensitivity;
        return new Vector2(
            look.x * mouseSensitivityX * tuning.mouseLookDegreesPerCount,
            look.y * mouseSensitivityY * tuning.mouseLookDegreesPerCount);
    }
}
