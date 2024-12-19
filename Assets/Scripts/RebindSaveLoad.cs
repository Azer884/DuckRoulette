using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine;
using Steamworks; // Add this line

public class RebindSaveLoad : MonoBehaviour
{
    public static RebindSaveLoad Instance;
    public InputActionAsset actions;
    public PlayerInput input;
    public Gamepad gamepad;
    public string currentControlScheme;
    private const string rebindFileName = "rebinds.json"; // Cloud file name

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void OnEnable()
    {
        // Load rebinds from Steam Cloud if availabl
        if (SteamClient.IsValid && SteamRemoteStorage.FileExists(rebindFileName))
        {
            var rebindBytes = SteamRemoteStorage.FileRead(rebindFileName);
            var rebinds = System.Text.Encoding.UTF8.GetString(rebindBytes);
            
            if (!string.IsNullOrEmpty(rebinds))
            {
                actions.LoadBindingOverridesFromJson(rebinds);
                Debug.Log("Loaded rebinds from Steam Cloud: " + rebinds);
            }
        }
        else
        {
            // Fallback to local PlayerPrefs if Steam is not available
            var rebinds = PlayerPrefs.GetString("rebinds");
            if (!string.IsNullOrEmpty(rebinds))
                actions.LoadBindingOverridesFromJson(rebinds);

            Debug.Log("Loaded rebinds from PlayerPrefs: " + rebinds);
        }

        gamepad = Gamepad.current;
        input = GetComponent<PlayerInput>();
        input.onControlsChanged += SwitchControls;
    }

    public void RumbleGamepad(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        if (currentControlScheme != "Gamepad")
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

    private IEnumerator SetMotorSpeed(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        yield return new WaitForSeconds(startDelay);

        gamepad.SetMotorSpeeds(lowFrequency, highFrequency);

        yield return new WaitForSeconds(duration);

        gamepad.SetMotorSpeeds(0f, 0f);
    }

    public void OnDisable()
    {
        var rebinds = actions.SaveBindingOverridesAsJson();

        if (SteamClient.IsValid)
        {
            // Convert the rebinds string to a byte array for Steam Cloud
            var rebindBytes = System.Text.Encoding.UTF8.GetBytes(rebinds);
            
            // Save to Steam Cloud
            SteamRemoteStorage.FileWrite(rebindFileName, rebindBytes);

            Debug.Log("Saved rebinds to Steam Cloud: " + rebinds);
        }
        else
        {
            // Fallback to PlayerPrefs if Steam is not available
            PlayerPrefs.SetString("rebinds", rebinds);

            Debug.Log("Saved rebinds to PlayerPrefs: " + rebinds);
        }

        input.onControlsChanged -= SwitchControls;
    }

}
