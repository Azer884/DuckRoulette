using System.Globalization;
using System.IO;
using IniParser;
using IniParser.Model;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private const string SettingsFileName = "Settings.ini";

    private string _settingsFilePath;
    private FileIniDataParser _parser;
    private IniData _data;

    public static SettingsManager Instance { get; private set; }

    [Header("Audio")]
    public AudioMixer audioMixer;

    [Header("Mouse")]
    public float MouseSensitivityX { get; private set; } = 1f;
    public float MouseSensitivityY { get; private set; } = 1f;
    public float ControllerSensitivityX { get; private set; } = 1f;
    public float ControllerSensitivityY { get; private set; } = 1f;

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

        LoadSettings();
    }

    public void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            _data = _parser.ReadFile(_settingsFilePath);
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

        SetSetting("Mouse", "SensitivityX", "1.0");
        SetSetting("Mouse", "SensitivityY", "1.0");
        SetSetting("Controller", "ControllerSensitivityX", "1.0");
        SetSetting("Controller", "ControllerSensitivityY", "1.0");
    }

    public void ApplyMouseSettings()
    {
        float legacySensitivity = GetFloatSetting("Mouse", "Sensitivity", 1f);

        MouseSensitivityX = GetFloatSetting("Mouse", "SensitivityX", legacySensitivity);
        MouseSensitivityY = GetFloatSetting("Mouse", "SensitivityY", legacySensitivity);
        ControllerSensitivityX = GetFloatSetting("Controller", "ControllerSensitivityX", 1f);
        ControllerSensitivityY = GetFloatSetting("Controller", "ControllerSensitivityY", 1f);
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
            return;
        }

        ApplyMixerVolume("MasterVolume", GetFloatSetting("Audio", "MasterVolume", 1f));
        ApplyMixerVolume("MusicVolume", GetFloatSetting("Audio", "MusicVolume", 0.8f));
        ApplyMixerVolume("SFXVolume", GetFloatSetting("Audio", "EffectsVolume", 0.8f));
        ApplyMixerVolume("VCVolume", GetFloatSetting("Audio", "VoiceChatVolume", 1f));
    }


    private bool IsTrueSetting(string value)
    {
        return string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", System.StringComparison.Ordinal);
    }

    private void ApplyMixerVolume(string exposedParam, float value)
    {
        float clamped = Mathf.Clamp(value, 0.0001f, 1f);
        audioMixer.SetFloat(exposedParam, Mathf.Log10(clamped) * 20f);
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

    public void SaveSlider(Slider slider, string section, string key)
    {
        SetSetting(section, key, slider.value.ToString(CultureInfo.InvariantCulture));
        SaveSettings();
    }

    public void SaveDropdown(TMP_Dropdown dropdown, string section, string key)
    {
        SetSetting(section, key, dropdown.value.ToString(CultureInfo.InvariantCulture));
        SaveSettings();
    }

    public void SaveToggle(Toggle toggle, string section, string key)
    {
        SetSetting(section, key, toggle.isOn ? "true" : "false");
        SaveSettings();
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
