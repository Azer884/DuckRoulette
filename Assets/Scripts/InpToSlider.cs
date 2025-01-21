using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InpToSlider : MonoBehaviour
{
    [SerializeField] private Slider sliderSens;
    [SerializeField] private TMP_InputField sliderSensInp;
    [SerializeField] private string sectionName = "MasterVolume";

    private bool isUpdating = false; // Flag to prevent recursive updates

    private void OnEnable() {
        sliderSens.onValueChanged.AddListener(OnSliderValueChange);
        sliderSensInp.onEndEdit.AddListener(OnInpValueChange);

        OnInpValueChange(sliderSensInp.text);
        OnSliderValueChange(sliderSens.value);
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
            if (!string.IsNullOrWhiteSpace(sectionName))
            {
                SettingsManager.Instance.audioMixer.SetFloat(sectionName, Mathf.Log10(number) * 20);
            }
            // Clamp the value between 0 and 1
            number = Mathf.Clamp(number, 0f, 1f);

            // Adjust for the specific rules
            if (number > 0.99f) number = 1f;

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

        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            SettingsManager.Instance.audioMixer.SetFloat(sectionName, Mathf.Log10(value) * 20);
        }
        // Clamp the slider value between 0 and 1
        value = Mathf.Clamp(value, 0f, 1f);

        // Adjust for specific rules
        if (value > 0.99f) value = 1f;

        // Update the input field and format the value
        sliderSensInp.text = value.ToString("F2");
        sliderSens.value = value;

        isUpdating = false;
    }

    private void OnDisable() {
        sliderSens.onValueChanged.RemoveListener(OnSliderValueChange);
        sliderSensInp.onEndEdit.RemoveListener(OnInpValueChange);
    }
}
