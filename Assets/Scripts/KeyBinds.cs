using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KeyBinds : MonoBehaviour
{
    [SerializeField] private Slider sliderSens;
    [SerializeField] private TMP_InputField sliderSensInp;

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
            // Clamp the value between 0 and 1
            number = Mathf.Clamp(number, 0f, 1f);

            // Adjust for the specific rules
            if (number > 0.99f) number = 1f;
            if (number < 0.01f) number = 0f;

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

        // Clamp the slider value between 0 and 1
        value = Mathf.Clamp(value, 0f, 1f);

        // Adjust for specific rules
        if (value > 0.99f) value = 1f;
        if (value < 0.01f) value = 0f;

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
