using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine;
using Steamworks;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class RebindSaveLoad : MonoBehaviour
{
    public static RebindSaveLoad Instance;
    public InputActionAsset actions;
    public PlayerInput input;
    public Gamepad gamepad;
    public string currentControlScheme;
    private const string rebindFileName = "rebinds.json"; // Cloud file name

    private NetworkObject playerObj;
    private Coroutine waitForPlayerSpawnCoroutine;

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

    private void OnEnable()
    {
        string rebinds = "";

        // Load rebinds from Steam Cloud or PlayerPrefs
        if (SteamClient.IsValid && SteamRemoteStorage.FileExists(rebindFileName))
        {
            var rebindBytes = SteamRemoteStorage.FileRead(rebindFileName);

            if (rebindBytes != null && rebindBytes.Length > 0)
            {
                rebinds = System.Text.Encoding.UTF8.GetString(rebindBytes);
                Debug.Log("Loaded rebinds from Steam Cloud: " + rebinds);
            }
            else
            {
                Debug.LogWarning("Steam Cloud rebind file was empty or null.");
            }
        }
        else
        {
            rebinds = PlayerPrefs.GetString("rebinds", "");
            Debug.Log("Loaded rebinds from PlayerPrefs: " + rebinds);
        }

        if (!string.IsNullOrEmpty(rebinds))
            actions.LoadBindingOverridesFromJson(rebinds);

        gamepad = Gamepad.current;
        input = GetComponent<PlayerInput>();
        // onControlsChanged only fires on a runtime switch between schemes - if the player
        // launches already on a gamepad and never switches, currentControlScheme was never set
        // and rumble silently never fired. Seed it from the current scheme up front.
        currentControlScheme = input.currentControlScheme;
        input.onControlsChanged += SwitchControls;

        RestartWaitForPlayerSpawn();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }


    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RestartWaitForPlayerSpawn();
    }

    // Cancels any still-running wait and unsubscribes from the previous playerObj before
    // starting a new wait, so subscriptions can't stack across scene loads (double rumble).
    private void RestartWaitForPlayerSpawn()
    {
        if (waitForPlayerSpawnCoroutine != null)
        {
            StopCoroutine(waitForPlayerSpawnCoroutine);
        }

        UnsubscribeFromPlayerObj();
        waitForPlayerSpawnCoroutine = StartCoroutine(WaitForPlayerSpawn());
    }

    private void UnsubscribeFromPlayerObj()
    {
        if (playerObj != null)
        {
            if (playerObj.TryGetComponent<Shooting>(out var shooting))
                shooting.OnGunShot -= OnGunShot;

            if (playerObj.TryGetComponent<Slap>(out var slap))
            {
                slap.OnSlap -= OnSlap;
                slap.OnSlapRecived -= OnSlapRecived;
            }
        }

        playerObj = null;
    }

    private IEnumerator WaitForPlayerSpawn()
    {
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null || NetworkManager.Singleton.LocalClient.PlayerObject == null)
            yield return null;

        playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;

        if (playerObj.TryGetComponent<Shooting>(out var shooting))
            shooting.OnGunShot += OnGunShot;

        if (playerObj.TryGetComponent<Slap>(out var slap))
        {
            slap.OnSlap += OnSlap;
            slap.OnSlapRecived += OnSlapRecived;
        }

        waitForPlayerSpawnCoroutine = null;
    }

    private void OnDisable()
    {
        // Save rebinds
        var rebinds = actions.SaveBindingOverridesAsJson();

        if (SteamClient.IsValid)
        {
            var rebindBytes = System.Text.Encoding.UTF8.GetBytes(rebinds);
            SteamRemoteStorage.FileWrite(rebindFileName, rebindBytes);
            Debug.Log("Saved rebinds to Steam Cloud: " + rebinds);
        }
        else
        {
            PlayerPrefs.SetString("rebinds", rebinds);
            Debug.Log("Saved rebinds to PlayerPrefs: " + rebinds);
        }

        input.onControlsChanged -= SwitchControls;

        if (waitForPlayerSpawnCoroutine != null)
        {
            StopCoroutine(waitForPlayerSpawnCoroutine);
            waitForPlayerSpawnCoroutine = null;
        }

        if (_activeRumbleRoutine != null)
        {
            StopCoroutine(_activeRumbleRoutine);
            _activeRumbleRoutine = null;
            gamepad?.SetMotorSpeeds(0f, 0f);
        }

        UnsubscribeFromPlayerObj();

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void SwitchControls(PlayerInput input)
    {
        currentControlScheme = input.currentControlScheme;
        Debug.Log("Control scheme changed to: " + currentControlScheme);
    }

    private void OnGunShot()
    {
        RumbleGamepad(0.8f, 1f, 0f, 0.4f);
    }

    private void OnSlap()
    {
        RumbleGamepad(0.5f, 0.8f, 0.2f, 0.3f);
    }

    private void OnSlapRecived()
    {
        RumbleGamepad(0.25f, 0.8f, 0.2f, 0.3f);
    }

    private Coroutine _activeRumbleRoutine;

    public void RumbleGamepad(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        if (currentControlScheme != "Gamepad")
            return;

        gamepad = Gamepad.current;

        if (gamepad != null)
        {
            // Without this, two rumbles fired close together (e.g. a slap right after a gunshot)
            // race each other's coroutines - whichever's duration ends first calls
            // SetMotorSpeeds(0, 0) and silently cuts off the other one early.
            if (_activeRumbleRoutine != null)
            {
                StopCoroutine(_activeRumbleRoutine);
            }

            _activeRumbleRoutine = StartCoroutine(SetMotorSpeed(lowFrequency, highFrequency, startDelay, duration));
        }
    }

    private IEnumerator SetMotorSpeed(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        yield return new WaitForSeconds(startDelay);

        if (gamepad != null)
        {
            gamepad.SetMotorSpeeds(lowFrequency, highFrequency);
        }

        yield return new WaitForSeconds(duration);

        if (gamepad != null)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
        }

        _activeRumbleRoutine = null;
    }
}
