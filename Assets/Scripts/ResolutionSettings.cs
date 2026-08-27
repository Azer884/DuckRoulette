using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

public class ResolutionSettings : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;

    private readonly List<Resolution> resolutions = new();

    private void OnEnable()
    {
        BuildOptions();
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    private void OnDisable()
    {
        dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    private void BuildOptions()
    {
        resolutions.Clear();
        var seen = new HashSet<string>();
        var options = new List<string>();

        foreach (var res in Screen.resolutions)
        {
            string key = res.width + "x" + res.height;
            if (!seen.Add(key)) continue;
            resolutions.Add(res);
            options.Add(res.width + " x " + res.height);
        }

        resolutions.Reverse();
        options.Reverse();

        var recommended = Screen.currentResolution;
        int recommendedIndex = resolutions.FindIndex(r => r.width == recommended.width && r.height == recommended.height);
        if (recommendedIndex >= 0)
        {
            options[recommendedIndex] += " (Recommended)";
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        int savedWidth = Screen.width;
        int savedHeight = Screen.height;
        if (SettingsManager.Instance != null)
        {
            savedWidth = int.Parse(SettingsManager.Instance.GetSetting("Graphics", "ResolutionWidth", Screen.width.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            savedHeight = int.Parse(SettingsManager.Instance.GetSetting("Graphics", "ResolutionHeight", Screen.height.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        }

        int index = resolutions.FindIndex(r => r.width == savedWidth && r.height == savedHeight);
        dropdown.SetValueWithoutNotify(index >= 0 ? index : 0);
        dropdown.RefreshShownValue();
    }

    private void OnDropdownChanged(int index)
    {
        if (SettingsManager.Instance == null || index < 0 || index >= resolutions.Count) return;

        var res = resolutions[index];
        SettingsManager.Instance.SetSetting("Graphics", "ResolutionWidth", res.width.ToString(CultureInfo.InvariantCulture));
        SettingsManager.Instance.SetSetting("Graphics", "ResolutionHeight", res.height.ToString(CultureInfo.InvariantCulture));
        SettingsManager.Instance.ApplyGraphicsSettings();
        SettingsManager.Instance.PersistSetting("Graphics", "ResolutionHeight", res.height.ToString(CultureInfo.InvariantCulture));
    }
}
