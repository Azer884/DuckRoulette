using System.IO;
using IniParser;
using IniParser.Model;
using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private static string settingsFilePath;
    private FileIniDataParser parser;
    private IniData data;

    void Awake()
    {
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
            ResetDefautSettings();
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
    }

    public void ResetDefautSettings()
    {
        SetDefaultSettings();
        SaveSettings();
    }

    public void SaveSlider(Slider slider, string section, string key)
    {
        data[section][key] = slider.value.ToString();
        SaveSettings();
    }

    public void SaveDropdown(Dropdown dropdown, string section, string key)
    {
        data[section][key] = dropdown.value.ToString();
        SaveSettings();
    }

    public void SaveToggle(Toggle toggle, string section, string key)
    {
        data[section][key] = toggle.isOn ? "true" : "false";
        SaveSettings();
    }


    public void LoadSlider(Slider slider, string section, string key)
    {
        slider.value = float.Parse(data[section][key]);
    }

    public void LoadDropdown(Dropdown dropdown, string section, string key)
    {
        dropdown.value = int.Parse(data[section][key]);
    }

    public void LoadToggle(Toggle toggle, string section, string key)
    {
        toggle.isOn = bool.Parse(data[section][key]);
    }
}
