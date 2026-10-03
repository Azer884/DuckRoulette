using System.Globalization;
using System.IO;
using IniParser;
using IniParser.Model;
using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private const string SettingsFileName = "Settings.ini";

    // Default look sensitivity for a fresh Settings.ini, a reset, and every fallback that runs
    // without one. The sliders in SettingsMenu.prefab range 0.01-5.
    public const float DefaultMouseSensitivity = 1.5f;
    public const float DefaultControllerSensitivity = 1.5f;

    // Bumped whenever a default changes in a way existing players should pick up. See
    // MigrateDefaults for what each step does.
    private const int CurrentDefaultsVersion = 3;

    // Range of the sensitivity sliders in SettingsMenu.prefab.
    public const float MinSensitivity = 0.01f;
    public const float MaxSensitivity = 5f;

    // Before v3 the gamepad Look binding carried a x20 scale processor, so a stored controller
    // sensitivity turned 20 times faster than it does now.
    private const float LegacyControllerLookScale = 20f;
    private const string MetaSection = "Meta";
    private const string DefaultsVersionKey = "DefaultsVersion";

    private string _settingsFilePath;
    private FileIniDataParser _parser;
    private IniData _data;
    private Coroutine _pendingSaveCoroutine;

    public static SettingsManager Instance { get; private set; }

    [Header("Audio")]
    public AudioMixer audioMixer;

    [Header("Mouse")]
    public float MouseSensitivityX { get; private set; } = DefaultMouseSensitivity;
    public float MouseSensitivityY { get; private set; } = DefaultMouseSensitivity;
    public float ControllerSensitivityX { get; private set; } = DefaultControllerSensitivity;
    public float ControllerSensitivityY { get; private set; } = DefaultControllerSensitivity;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _settingsFilePath = Path.Combine(Application.persistentDataPath, SettingsFileName);
        _parser = new FileIniDataParser();

    }

    private void Start()
    {
        LoadSettings();
    }

    public void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            _data = _parser.ReadFile(_settingsFilePath);

            if (MigrateDefaults())
            {
                WriteSettingsToDisk();
            }
        }
        else
        {
            _data = new IniData();
            SetDefaultSettings();
            SaveSettings();
            return;
        }

        ApplyAllSettings();
    }

    public void SaveSettings()
    {
        if (_data == null)
        {
            _data = new IniData();
        }

        _parser.WriteFile(_settingsFilePath, _data);
        ApplyAllSettings();
    }

    public string GetSetting(string section, string key, string defaultValue = "")
    {
        if (_data != null && _data.Sections.ContainsSection(section) && _data[section].ContainsKey(key))
        {
            return _data[section][key];
        }

        return defaultValue;
    }

    public void SetSetting(string section, string key, string value)
    {
        if (_data == null)
        {
            _data = new IniData();
        }

        if (!_data.Sections.ContainsSection(section))
        {
            _data.Sections.AddSection(section);
        }

        _data[section][key] = value;
    }

    private void SetDefaultSettings()
    {
        int defaultWidth = Screen.currentResolution.width > 0 ? Screen.currentResolution.width : Screen.width;
        int defaultHeight = Screen.currentResolution.height > 0 ? Screen.currentResolution.height : Screen.height;

        SetSetting("Graphics", "ResolutionWidth", defaultWidth.ToString(CultureInfo.InvariantCulture));
        SetSetting("Graphics", "ResolutionHeight", defaultHeight.ToString(CultureInfo.InvariantCulture));
        SetSetting("Graphics", "Fullscreen", "true");
        SetSetting("Graphics", "VSync", "true");
        SetSetting("Graphics", "QualityLevel", Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Mathf.Max(0, QualitySettings.names.Length - 1)).ToString(CultureInfo.InvariantCulture));

        SetSetting("Audio", "MasterVolume", "1.0");
        SetSetting("Audio", "MusicVolume", "0.8");
        SetSetting("Audio", "EffectsVolume", "0.8");
        SetSetting("Audio", "VoiceChatVolume", "1.0");
        SetSetting("Audio", "VoiceChatMode", "0");
        SetSetting("Audio", "MicDevice", "");

        string mouseSensitivity = DefaultMouseSensitivity.ToString(CultureInfo.InvariantCulture);
        string controllerSensitivity = DefaultControllerSensitivity.ToString(CultureInfo.InvariantCulture);
        SetSetting("Mouse", "SensitivityX", mouseSensitivity);
        SetSetting("Mouse", "SensitivityY", mouseSensitivity);
        SetSetting("Controller", "ControllerSensitivityX", controllerSensitivity);
        SetSetting("Controller", "ControllerSensitivityY", controllerSensitivity);

        SetSetting("Game", "FieldOfView", "60");
        SetSetting("Game", "CameraShake", "1.0");
        SetSetting("Game", "InvertLookY", "false");
        SetSetting("Game", "ToggleCrouch", "false");
        SetSetting("Game", "ToggleSprint", "false");
        SetSetting("Game", "ShowTaskList", "true");
        SetSetting("Game", "ShowFps", GameplaySettings.DefaultShowFps ? "true" : "false");
        SetSetting("Game", "ShowPing", GameplaySettings.DefaultShowPing ? "true" : "false");

        SetSetting("Accessibility", "ReduceMotion", "false");
        SetSetting("Accessibility", "FlashingEffects", "true");
        SetSetting("Accessibility", "ScreenEffects", "1.0");
        SetSetting("Accessibility", "UIScale", "1.0");

        SetSetting(MetaSection, DefaultsVersionKey, CurrentDefaultsVersion.ToString(CultureInfo.InvariantCulture));
    }

    // Brings a Settings.ini written by an older build up to the current defaults. A value is only
    // replaced while it still equals the old default it was written with, so anything the player
    // actually chose is kept. Returns true when the file needs saving.
    private bool MigrateDefaults()
    {
        int version = GetIntSetting(MetaSection, DefaultsVersionKey, 1);
        if (version >= CurrentDefaultsVersion)
        {
            return false;
        }

        if (version < 2)
        {
            // v2: higher default look sensitivity, FPS and ping shown by default.
            string mouseSensitivity = DefaultMouseSensitivity.ToString(CultureInfo.InvariantCulture);
            string controllerSensitivity = DefaultControllerSensitivity.ToString(CultureInfo.InvariantCulture);
            ReplaceIfOldDefault("Mouse", "SensitivityX", 1f, mouseSensitivity);
            ReplaceIfOldDefault("Mouse", "SensitivityY", 1f, mouseSensitivity);
            ReplaceIfOldDefault("Controller", "ControllerSensitivityX", 1f, controllerSensitivity);
            ReplaceIfOldDefault("Controller", "ControllerSensitivityY", 1f, controllerSensitivity);

            if (!IsTrueSetting(GetSetting(GameplaySettings.GameSection, "ShowFps", "false")))
            {
                SetSetting(GameplaySettings.GameSection, "ShowFps", GameplaySettings.DefaultShowFps ? "true" : "false");
            }
            if (!IsTrueSetting(GetSetting(GameplaySettings.GameSection, "ShowPing", "false")))
            {
                SetSetting(GameplaySettings.GameSection, "ShowPing", GameplaySettings.DefaultShowPing ? "true" : "false");
            }
        }

        if (version < 3)
        {
            // v3: the x20 gamepad scale moved out of the input binding. A controller sensitivity
            // the player picked is scaled up to keep the same turn speed; one at the default
            // (including one just reset above) already means the new, intended speed.
            ScaleUnlessDefault("Controller", "ControllerSensitivityX", DefaultControllerSensitivity, LegacyControllerLookScale);
            ScaleUnlessDefault("Controller", "ControllerSensitivityY", DefaultControllerSensitivity, LegacyControllerLookScale);
        }

        SetSetting(MetaSection, DefaultsVersionKey, CurrentDefaultsVersion.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private void ScaleUnlessDefault(string section, string key, float defaultValue, float scale)
    {
        float value = GetFloatSetting(section, key, defaultValue);
        if (!Mathf.Approximately(value, defaultValue))
        {
            float scaled = Mathf.Clamp(value * scale, MinSensitivity, MaxSensitivity);
            SetSetting(section, key, scaled.ToString(CultureInfo.InvariantCulture));
        }
    }

    // A missing key also counts as the old default - the Apply* fallbacks used to resolve it to that.
    private void ReplaceIfOldDefault(string section, string key, float oldDefault, string newValue)
    {
        if (Mathf.Approximately(GetFloatSetting(section, key, oldDefault), oldDefault))
        {
            SetSetting(section, key, newValue);
        }
    }

    public void ApplyMouseSettings()
    {
        float legacySensitivity = GetFloatSetting("Mouse", "Sensitivity", DefaultMouseSensitivity);

        MouseSensitivityX = GetFloatSetting("Mouse", "SensitivityX", legacySensitivity);
        MouseSensitivityY = GetFloatSetting("Mouse", "SensitivityY", legacySensitivity);
        ControllerSensitivityX = GetFloatSetting("Controller", "ControllerSensitivityX", DefaultControllerSensitivity);
        ControllerSensitivityY = GetFloatSetting("Controller", "ControllerSensitivityY", DefaultControllerSensitivity);
    }
    public void ResetDefaultSettings()
    {
        _data = new IniData();
        SetDefaultSettings();
        SaveSettings();
    }
    public void ApplyAllSettings()
    {
        ApplyGraphicsSettings();
        ApplyAudioSettings();
        ApplyMouseSettings();
        GameplaySettings.Invalidate();
    }
    public void ApplyGraphicsSettings()
    {
        bool fullscreen = IsTrueSetting(GetSetting("Graphics", "Fullscreen", "true"));
        bool vSync = IsTrueSetting(GetSetting("Graphics", "VSync", "true"));

        int width = GetIntSetting("Graphics", "ResolutionWidth", Screen.currentResolution.width > 0 ? Screen.currentResolution.width : Screen.width);
        int height = GetIntSetting("Graphics", "ResolutionHeight", Screen.currentResolution.height > 0 ? Screen.currentResolution.height : Screen.height);
        int qualityLevel = GetIntSetting("Graphics", "QualityLevel", QualitySettings.GetQualityLevel());

        if (width > 0 && height > 0)
        {
            Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        QualitySettings.vSyncCount = vSync ? 1 : 0;

        if (QualitySettings.names.Length > 0)
        {
            qualityLevel = Mathf.Clamp(qualityLevel, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(qualityLevel, true);
        }
    }

    public void ApplyAudioSettings()
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("SettingsManager: AudioMixer is not assigned - cannot apply audio settings on start.");
            return;
        }

        ApplyMixerVolume("MasterVolume", GetFloatSetting("Audio", "MasterVolume", 1f));
        ApplyMixerVolume("MusicVolume", GetFloatSetting("Audio", "MusicVolume", 0.8f));
        ApplyMixerVolume("SFXVolume", GetFloatSetting("Audio", "EffectsVolume", 0.8f));
        ApplyMixerVolume("VCVolume", GetFloatSetting("Audio", "VoiceChatVolume", 1f));
    }

    private IEnumerator WaitForAudioListenerThenApply(float timeoutSeconds)
    {
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < timeoutSeconds)
        {
            if (FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length > 0)
            {
                // Apply settings once a listener is present
                ApplyMixerVolume("MasterVolume", GetFloatSetting("Audio", "MasterVolume", 1f));
                ApplyMixerVolume("MusicVolume", GetFloatSetting("Audio", "MusicVolume", 0.8f));
                ApplyMixerVolume("SFXVolume", GetFloatSetting("Audio", "EffectsVolume", 0.8f));
                ApplyMixerVolume("VCVolume", GetFloatSetting("Audio", "VoiceChatVolume", 1f));
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("SettingsManager: No AudioListener found within timeout; audio settings were not applied at audible time.");
    }


    private bool IsTrueSetting(string value)
    {
        return string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", System.StringComparison.Ordinal);
    }

    private void ApplyMixerVolume(string exposedParam, float value)
    {
        float clamped = Mathf.Clamp(value, 0.0001f, 1f);
        float db = Mathf.Log10(clamped) * 20f;

        // SetFloat returns a bool indicating whether the parameter was found/applied.
        audioMixer.SetFloat(exposedParam, db);
    }


    private int GetIntSetting(string section, string key, int defaultValue)
    {
        string value = GetSetting(section, key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
    }

    private float GetFloatSetting(string section, string key, float defaultValue)
    {
        string value = GetSetting(section, key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
    }

    // For controls (e.g. InpToSlider) that need to persist a value without owning a Slider/Dropdown/Toggle.
    public void PersistSetting(string section, string key, string value)
    {
        SetSetting(section, key, value);
        RequestDeferredSave();
    }

    public void SaveSlider(Slider slider, string section, string key)
    {
        SetSetting(section, key, slider.value.ToString(CultureInfo.InvariantCulture));
        ApplySettingsForSection(section);
        RequestDeferredSave();
    }

    public void SaveDropdown(TMP_Dropdown dropdown, string section, string key)
    {
        SetSetting(section, key, dropdown.value.ToString(CultureInfo.InvariantCulture));
        ApplySettingsForSection(section);
        RequestDeferredSave();
    }

    public void SaveToggle(Toggle toggle, string section, string key)
    {
        SetSetting(section, key, toggle.isOn ? "true" : "false");
        ApplySettingsForSection(section);
        RequestDeferredSave();
    }

    private void ApplySettingsForSection(string section)
    {
        switch (section)
        {
            case "Audio":
                ApplyAudioSettings();
                // Voice chat mode lives here and is read through GameplaySettings' cache.
                GameplaySettings.Invalidate();
                break;
            case "Graphics":
                ApplyGraphicsSettings();
                break;
            case "Mouse":
            case "Controller":
                ApplyMouseSettings();
                break;
            // The Game and Accessibility tabs are read on demand by whatever they affect
            // (Movement, CameraShaker, the screen effects, the HUD) - there is nothing to push,
            // only a cache to drop so the next read sees the new value.
            case GameplaySettings.GameSection:
            case GameplaySettings.AccessibilitySection:
                GameplaySettings.Invalidate();
                break;
            default:
                ApplyAllSettings();
                break;
        }
    }

    // Coalesces rapid-fire changes (e.g. dragging a slider fires onValueChanged every frame)
    // into a single disk write instead of writing Settings.ini on every tick.
    private void RequestDeferredSave()
    {
        if (_pendingSaveCoroutine != null)
        {
            StopCoroutine(_pendingSaveCoroutine);
        }

        if (gameObject.activeInHierarchy)
        {
            _pendingSaveCoroutine = StartCoroutine(SaveAfterDelay());
        }
        else
        {
            WriteSettingsToDisk();
        }
    }

    private IEnumerator SaveAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.3f);
        _pendingSaveCoroutine = null;
        WriteSettingsToDisk();
    }

    private void WriteSettingsToDisk()
    {
        if (_data == null)
        {
            _data = new IniData();
        }

        _parser.WriteFile(_settingsFilePath, _data);
    }

    private void OnDisable()
    {
        if (_pendingSaveCoroutine != null)
        {
            _pendingSaveCoroutine = null;
            WriteSettingsToDisk();
        }
    }

    public void LoadSlider(Slider slider, string section, string key)
    {
        string rawValue = GetSetting(section, key, slider.value.ToString(CultureInfo.InvariantCulture));

        if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            slider.value = value;
        }
        else
        {
            Debug.LogWarning($"Invalid float value for {section}.{key} in Settings.ini. Using slider's default value.");
        }
    }

    public void LoadDropdown(TMP_Dropdown dropdown, string section, string key)
    {
        string rawValue = GetSetting(section, key, dropdown.value.ToString(CultureInfo.InvariantCulture));

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            dropdown.value = value;
        }
        else
        {
            Debug.LogWarning($"Invalid int value for {section}.{key} in Settings.ini. Using dropdown's default value.");
        }
    }

    public void LoadToggle(Toggle toggle, string section, string key)
    {
        string rawValue = GetSetting(section, key, toggle.isOn ? "true" : "false");

        if (bool.TryParse(rawValue, out bool isOn))
        {
            toggle.isOn = isOn;
        }
        else
        {
            Debug.LogWarning($"Invalid bool value for {section}.{key} in Settings.ini. Using toggle's default value.");
        }
    }
}
