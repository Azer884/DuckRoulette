using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine;

public class RebindSaveLoad : MonoBehaviour
{
    public static RebindSaveLoad Instance;
    public InputActionAsset actions;
    private PlayerInput input;
    public Gamepad gamepad;
    private string currentControlScheme;


    public void OnEnable()
    {
        var rebinds = PlayerPrefs.GetString("rebinds");
        if (!string.IsNullOrEmpty(rebinds))
            actions.LoadBindingOverridesFromJson(rebinds);
        actions.Enable();
        gamepad = Gamepad.current;
        input = GetComponent<PlayerInput>();
        input.onControlsChanged += SwitchControls;
    }

    public void RumbleGamepad(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        if(currentControlScheme != "Gamepad")
            return;
        gamepad = Gamepad.current;

        if (gamepad != null)
        {
            StartCoroutine(SetMotorSpeed(lowFrequency, highFrequency, startDelay, duration));
        }
    }

    private void SwitchControls(PlayerInput input)
    {
        currentControlScheme = input.currentControlScheme;
        Debug.Log(currentControlScheme);
    }


    private IEnumerator SetMotorSpeed(float lowFrequency, float highFrequency, float startDelay,float duration)
    {
        yield return new WaitForSeconds(startDelay);

        gamepad.SetMotorSpeeds(lowFrequency, highFrequency);

        yield return new WaitForSeconds(duration);

        gamepad.SetMotorSpeeds(0f, 0f);
    }

    public void OnDisable()
    {
        var rebinds = actions.SaveBindingOverridesAsJson();
        PlayerPrefs.SetString("rebinds", rebinds);

        input.onControlsChanged -= SwitchControls;
        actions.Disable();
    }
}
