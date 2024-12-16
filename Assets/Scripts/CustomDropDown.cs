using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CustomDropDown : MonoBehaviour, IDeselectHandler
{
    [SerializeField] private GameObject dropDownMenu; // Dropdown menu container
    [SerializeField] private TextMeshProUGUI displayText; // Text to show the selected option
    [SerializeField] private ToggleGroup toggleGroup; // Toggle group containing options

    private bool isOpened;

    void OnEnable()
    {
        // Register the button click listener
        GetComponent<Button>().onClick.AddListener(ToggleDropDown);

        // Add listener to all toggles in the group
        foreach (var toggle in toggleGroup.GetComponentsInChildren<Toggle>())
        {
            toggle.onValueChanged.AddListener(delegate { OnOptionSelected(toggle); });
        }
    }

    private void ToggleDropDown()
    {
        if (!isOpened)
        {
            dropDownMenu.SetActive(true); // Show dropdown menu
            isOpened = true;
        }
        else
        {
            CloseDropDown();
        }
    }

    private void CloseDropDown()
    {
        if (dropDownMenu.GetComponent<Animator>() != null)
        {
            dropDownMenu.GetComponent<Animator>().Play("Close"); // Play close animation
            Invoke(nameof(HideDropDown), 0.5f); // Adjust delay based on animation length
        }
        else
        {
            HideDropDown();
        }
    }

    private void HideDropDown()
    {
        dropDownMenu.SetActive(false);
        isOpened = false;
    }

    private void OnOptionSelected(Toggle toggle)
    {
        if (toggle.isOn) // Ensure the selected toggle is active
        {
            displayText.text = toggle.GetComponentInChildren<TextMeshProUGUI>().text; // Update the display text
            CloseDropDown(); // Close the dropdown after selection
        }
    }

    // This method is triggered when the button loses focus
    public void OnDeselect(BaseEventData eventData)
    {
        if (isOpened)
        {
            CloseDropDown(); // Close dropdown when button is deselected
        }
    }

    void OnDisable()
    {
        // Unregister all listeners
        GetComponent<Button>().onClick.RemoveListener(ToggleDropDown);

        foreach (var toggle in toggleGroup.GetComponentsInChildren<Toggle>())
        {
            toggle.onValueChanged.RemoveAllListeners();
        }
    }
}
