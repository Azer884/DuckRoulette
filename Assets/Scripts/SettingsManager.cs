using System.IO;
using IniParser;
using IniParser.Model;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private static string _settingsFilePath;
    private FileIniDataParser _parser;
    private IniData _data;
    public static SettingsManager Instance { get; private set; }

    public AudioMixer audioMixer;
    public bool startInactive = true;
    private bool _initialized = false;

    void Start()
    {
        // Implement Singleton Pattern
        if (Instance == null &&  !_initialized)
        {
            Instance = this;
        }

        if (!_initialized)
        {
            _settingsFilePath = Path.Combine(Application.persistentDataPath, "Settings.ini");
            _parser = new FileIniDataParser();
        }
        LoadSettings();
        if (!_initialized)
        {
            _initialized = true;
            gameObject.SetActive(!startInactive);
        }
    }

    public void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            _data = _parser.ReadFile(_settingsFilePath);

            if (float.TryParse(GetSetting("Audio", "MasterVolume"), out float value))
            {
                audioMixer.SetFloat("MasterVolume", Mathf.Log10(value) * 20);
            }
            if (float.TryParse(GetSetting("Audio", "MusicVolume"), out value))
            {
                audioMixer.SetFloat("MusicVolume", Mathf.Log10(value) * 20);
            }
            if (float.TryParse(GetSetting("Audio", "EffectsVolume"), out value))
            {
                audioMixer.SetFloat("SFXVolume", Mathf.Log10(value) * 20);
            }
            if (float.TryParse(GetSetting("Audio", "VoiceChatVolume"), out value))
            {
                audioMixer.SetFloat("VCVolume", Mathf.Log10(value) * 20);
            }
        }
        else
        {
            // Create default settings if the file does not exist
            _data = new IniData();
            ResetDefaultSettings();
        }
    }

    public void SaveSettings()
    {
        _parser.WriteFile(_settingsFilePath, _data);
    }

    public string GetSetting(string section, string key, string defaultValue = "")
    {
        return _data[section][key] ?? defaultValue;
    }

    public void SetSetting(string section, string key, string value)
    {
        if (!_data.Sections.ContainsSection(section))
            _data.Sections.AddSection(section);

        _data[section][key] = value;
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
        if (_data.Sections.ContainsSection(section) && _data[section].ContainsKey(key))
        {
            if (float.TryParse(_data[section][key], out float value))
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
        if (_data.Sections.ContainsSection(section) && _data[section].ContainsKey(key))
        {
            if (int.TryParse(_data[section][key], out int value))
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
        if (_data.Sections.ContainsSection(section) && _data[section].ContainsKey(key))
        {
            if (bool.TryParse(_data[section][key], out bool isOn))
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
