using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Fills the voice-settings dropdown with the machine's input devices. Option 0 is "Default",
// which maps to a null device name - Unity's Microphone API reads that as "whatever the system
// default is", so a player who never touches this keeps following their OS setting.
[RequireComponent(typeof(TMP_Dropdown))]
public class MicDeviceSettings : MonoBehaviour
{
    public const string Section = "Audio";
    public const string Key = "MicDevice";

    public static event Action<string> OnDeviceChanged;

    private TMP_Dropdown dropdown;

    // Index-aligned with the dropdown's options; entry 0 is null (system default).
    private readonly List<string> deviceNames = new();

    // Persisted as the device NAME rather than the dropdown index: the device list reorders when
    // hardware is plugged in or removed, so a saved index would silently select a different mic.
    // A saved device that is no longer present falls back to the system default.
    public static string SelectedDevice
    {
        get
        {
            if (SettingsManager.Instance == null) return null;

            string saved = SettingsManager.Instance.GetSetting(Section, Key, string.Empty);
            if (string.IsNullOrEmpty(saved)) return null;

            return Array.IndexOf(Microphone.devices, saved) >= 0 ? saved : null;
        }
    }

    private void OnEnable()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        StartCoroutine(Populate());
    }

    private void OnDisable()
    {
        if (dropdown != null)
        {
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }
    }

    private IEnumerator Populate()
    {
        yield return new WaitUntil(() => SettingsManager.Instance != null);

        dropdown.onValueChanged.RemoveListener(OnDropdownChanged);

        deviceNames.Clear();
        deviceNames.Add(null);

        List<string> options = new() { "Default" };
        foreach (string device in Microphone.devices)
        {
            deviceNames.Add(device);
            options.Add(device);
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        string saved = SettingsManager.Instance.GetSetting(Section, Key, string.Empty);
        int index = string.IsNullOrEmpty(saved) ? 0 : Mathf.Max(0, deviceNames.IndexOf(saved));
        dropdown.SetValueWithoutNotify(index);
        dropdown.RefreshShownValue();

        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    private void OnDropdownChanged(int index)
    {
        string device = index >= 0 && index < deviceNames.Count ? deviceNames[index] : null;

        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.PersistSetting(Section, Key, device ?? string.Empty);
        }

        OnDeviceChanged?.Invoke(device);
    }
}
