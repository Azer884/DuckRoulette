using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InpToSlider : MonoBehaviour
{
    [SerializeField] private Slider sliderSens;
    [SerializeField] private TMP_InputField sliderSensInp;
    [SerializeField] private string sectionName = "MasterVolume";

    // sectionName holds the AudioMixer's exposed parameter name, which doesn't always match
    // the Settings.ini key SettingsManager reads on load - map it so changes actually persist.
    private static readonly Dictionary<string, string> MixerParamToIniKey = new()
    {
        { "MasterVolume", "MasterVolume" },
        { "MusicVolume", "MusicVolume" },
        { "SFXVolume", "EffectsVolume" },
        { "VCVolume", "VoiceChatVolume" },
    };

    private bool isUpdating = false; // Flag to prevent recursive updates

    private void OnEnable() {
        sliderSens.onValueChanged.AddListener(OnSliderValueChange);
        sliderSensInp.onEndEdit.AddListener(OnInpValueChange);

        LoadPersistedValue();
    }

    // Reflects the saved setting onto the UI (and mixer) without persisting anything - the old
    // code called OnInpValueChange/OnSliderValueChange here, which persist whatever the slider
    // currently displays. Nothing actually loads the saved value onto this slider first, so on
    // every enable (e.g. reopening the settings panel) that overwrote the real saved volume with
    // whatever default/stale value happened to be showing.
    private void LoadPersistedValue()
    {
        float value = sliderSens.value;

        if (SettingsManager.Instance != null && MixerParamToIniKey.TryGetValue(sectionName, out string iniKey))
        {
            string persisted = SettingsManager.Instance.GetSetting("Audio", iniKey, null);
            if (persisted != null && float.TryParse(persisted, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                value = parsed;
            }
        }

        isUpdating = true;

        value = Mathf.Clamp(value, sliderSens.minValue, sliderSens.maxValue);
        sliderSens.value = value;
        sliderSensInp.text = value.ToString("F2");

        if (!string.IsNullOrWhiteSpace(sectionName) && SettingsManager.Instance != null)
        {
            SettingsManager.Instance.audioMixer.SetFloat(sectionName, Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20);
        }

        isUpdating = false;
    }

    private void OnInpValueChange(string value)
    {
        if (isUpdating) return; // Prevent recursive calls
        isUpdating = true;

        // Replace multiple leading zeros with a single zero
        value = value.TrimStart('0'); 
        if (string.IsNullOrEmpty(value) || value == ".") value = "0"; 

        // Prepend "0" if the input starts with a dot
        if (value.StartsWith(".")) value = "0" + value;

        // Restrict to one digit before the decimal and two digits after
        if (float.TryParse(value, out float number))
        {
            if (!string.IsNullOrWhiteSpace(sectionName) && SettingsManager.Instance != null)
            {
                SettingsManager.Instance.audioMixer.SetFloat(sectionName, Mathf.Log10(number) * 20);
            }
            // Clamp the value to slider's min and max
            number = Mathf.Clamp(number, sliderSens.minValue, sliderSens.maxValue);
            PersistVolume(number);

            // Format the value to ensure proper display
            value = number.ToString("F2");
            sliderSens.value = number; // Update slider value
        }
        else
        {
            // Reset to current slider value if input is invalid
            value = sliderSens.value.ToString("F2");
        }

        // Update the input field
        sliderSensInp.text = value;

        isUpdating = false;
    }

    private void OnSliderValueChange(float value)
    {
        if (isUpdating) return; // Prevent recursive calls
        isUpdating = true;

        if (!string.IsNullOrWhiteSpace(sectionName) && SettingsManager.Instance != null)
        {
            SettingsManager.Instance.audioMixer.SetFloat(sectionName, Mathf.Log10(value) * 20);
        }
        // Clamp the slider value to its min and max range
        value = Mathf.Clamp(value, sliderSens.minValue, sliderSens.maxValue);
        PersistVolume(value);

        // Update the input field and format the value
        sliderSensInp.text = value.ToString("F2");
        sliderSens.value = value;

        isUpdating = false;
    }

    private void PersistVolume(float value)
    {
        if (SettingsManager.Instance == null || string.IsNullOrWhiteSpace(sectionName))
        {
            return;
        }

        if (MixerParamToIniKey.TryGetValue(sectionName, out string iniKey))
        {
            SettingsManager.Instance.PersistSetting("Audio", iniKey, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void OnDisable() {
        sliderSens.onValueChanged.RemoveListener(OnSliderValueChange);
        sliderSensInp.onEndEdit.RemoveListener(OnInpValueChange);
    }
}
