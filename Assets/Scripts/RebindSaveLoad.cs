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
        // Load rebinds from Steam Cloud or PlayerPrefs
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
            var rebinds = PlayerPrefs.GetString("rebinds");
            if (!string.IsNullOrEmpty(rebinds))
                actions.LoadBindingOverridesFromJson(rebinds);

            Debug.Log("Loaded rebinds from PlayerPrefs: " + rebinds);
        }

        gamepad = Gamepad.current;
        input = GetComponent<PlayerInput>();
        input.onControlsChanged += SwitchControls;

        // Wait for player to spawn
        StartCoroutine(WaitForPlayerSpawn());

        // Optional: if you want to re-check when scenes change
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(WaitForPlayerSpawn());
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

        if (playerObj != null)
        {
            var shooting = playerObj.GetComponent<Shooting>();
            if (shooting != null)
                shooting.OnGunShot -= OnGunShot;

            var slap = playerObj.GetComponent<Slap>();
            if (slap != null)
            {
                slap.OnSlap -= OnSlap;
                slap.OnSlapRecived -= OnSlapRecived;
            }
        }

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

    private IEnumerator SetMotorSpeed(float lowFrequency, float highFrequency, float startDelay, float duration)
    {
        yield return new WaitForSeconds(startDelay);

        gamepad.SetMotorSpeeds(lowFrequency, highFrequency);

        yield return new WaitForSeconds(duration);

        gamepad.SetMotorSpeeds(0f, 0f);
    }
}
