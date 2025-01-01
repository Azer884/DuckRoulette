using System.IO;
using IniParser;
using IniParser.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private static string settingsFilePath;
    private FileIniDataParser parser;
    private IniData data;
    public static SettingsManager Instance { get; private set; }

    void Awake()
    {
        // Implement Singleton Pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            return;
        }

        settingsFilePath = Path.Combine(Application.persistentDataPath, "Settings.ini");
        parser = new FileIniDataParser();
        LoadSettings();
    }

    public void LoadSettings()
    {
        if (File.Exists(settingsFilePath))
        {
            data = parser.ReadFile(settingsFilePath);
        }
        else
        {
            // Create default settings if the file does not exist
            data = new IniData();
            ResetDefaultSettings();
        }
    }

    public void SaveSettings()
    {
        parser.WriteFile(settingsFilePath, data);
    }

    public string GetSetting(string section, string key, string defaultValue = "")
    {
        return data[section][key] ?? defaultValue;
    }

    public void SetSetting(string section, string key, string value)
    {
        if (!data.Sections.ContainsSection(section))
            data.Sections.AddSection(section);

        data[section][key] = value;
    }

    private void SetDefaultSettings()
    {
        SetSetting("Graphics", "Resolution", "1920x1080");
        SetSetting("Graphics", "Fullscreen", "true");
        SetSetting("Graphics", "VSync", "true");
        SetSetting("Graphics", "QualityLevel", "2");

        SetSetting("Audio", "MasterVolume", "1.0");
        SetSetting("Audio", "MusicVolume", "0.8");
        SetSetting("Audio", "EffectsVolume", "0.8");
        SetSetting("Audio", "VoiceChatVolume", "1.0");
        SetSetting("Audio", "VoiceChatMode", "0");

        // Add other default settings as needed
    }

    public void ResetDefaultSettings()
    {
        SetDefaultSettings();
        SaveSettings();
    }

    public void SaveSlider(Slider slider, string section, string key)
    {
        SetSetting(section, key, slider.value.ToString());
        SaveSettings();
    }

    public void SaveDropdown(TMP_Dropdown dropdown, string section, string key)
    {
        SetSetting(section, key, dropdown.value.ToString());
        SaveSettings();
    }

    public void SaveToggle(Toggle toggle, string section, string key)
    {
        SetSetting(section, key, toggle.isOn ? "true" : "false");
        SaveSettings();
    }

    public void LoadSlider(Slider slider, string section, string key)
    {
        if (data.Sections.ContainsSection(section) && data[section].ContainsKey(key))
        {
            if (float.TryParse(data[section][key], out float value))
            {
                slider.value = value;
            }
            else
            {
                Debug.LogWarning($"Invalid float value for {section}.{key} in Settings.ini. Using slider's default value.");
            }
        }
    }

    public void LoadDropdown(TMP_Dropdown dropdown, string section, string key)
    {
        if (data.Sections.ContainsSection(section) && data[section].ContainsKey(key))
        {
            if (int.TryParse(data[section][key], out int value))
            {
                dropdown.value = value;
            }
            else
            {
                Debug.LogWarning($"Invalid int value for {section}.{key} in Settings.ini. Using dropdown's default value.");
            }
        }
    }

    public void LoadToggle(Toggle toggle, string section, string key)
    {
        if (data.Sections.ContainsSection(section) && data[section].ContainsKey(key))
        {
            if (bool.TryParse(data[section][key], out bool isOn))
            {
                toggle.isOn = isOn;
            }
            else
            {
                Debug.LogWarning($"Invalid bool value for {section}.{key} in Settings.ini. Using toggle's default value.");
            }
        }
    }
}
