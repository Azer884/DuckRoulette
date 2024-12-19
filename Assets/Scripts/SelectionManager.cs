using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SelectionManager : MonoBehaviour
{
    [SerializeField] private Selectable firstSelected; // First selectable object
    [SerializeField] private InputActionReference navRef; // Input reference for navigation

    private GameObject lastSelected; // Tracks the last selected object
    private HashSet<GameObject> registeredButtons = new HashSet<GameObject>(); // Tracks registered buttons

    void OnEnable()
    {
        StartCoroutine(SelectAfterDelay());
        navRef.action.performed += OnNavigate;

        // Register all children with EventTriggers
        RegisterChildButtons(transform);
    }

    void OnDisable()
    {
        navRef.action.performed -= OnNavigate;
    }

    private IEnumerator SelectAfterDelay()
    {
        yield return null; // Wait for UI initialization
        if (firstSelected != null)
        {
            SelectButton(firstSelected.gameObject);
        }
    }

    private void SelectButton(GameObject button)
    {
        if (button != null)
        {
            EventSystem.current.SetSelectedGameObject(button);
            UpdateLastSelected(button);
        }
    }

    public void UpdateLastSelected(GameObject selected)
    {
        if (selected != null && registeredButtons.Contains(selected))
        {
            lastSelected = selected;
            Debug.Log($"Last Selected Updated: {lastSelected.name}");
        }
    }

    private void RegisterChildButtons(Transform parent)
    {
        foreach (Transform child in parent)
        {
            // Check if the child has a Button component
            if (child.TryGetComponent(out Button button))
            {
                RegisterButton(button.gameObject);
            }
            // Register toggles
            else if (child.TryGetComponent(out Toggle toggle))
            {
                RegisterButton(toggle.gameObject);
            }
            // Register TMP_Dropdown
            else if (child.TryGetComponent(out TMP_Dropdown dropdown))
            {
                RegisterButton(dropdown.gameObject);
            }
            // Recursively check child objects
            RegisterChildButtons(child);
        }
    }

    public void RegisterButton(GameObject button)
    {
        if (button != null && registeredButtons.Add(button)) // Only add if not already registered
        {
            if (button.TryGetComponent(out EventTrigger trigger))
            {
                // Add a Select event to update the last selected button
                EventTrigger.Entry selectEntry = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.Select
                };
                selectEntry.callback.AddListener((eventData) => UpdateLastSelected(button));
                trigger.triggers.Add(selectEntry);
            }
        }
    }

    public void UnregisterButton(GameObject button)
    {
        if (button != null)
        {
            registeredButtons.Remove(button);
        }
    }

    void OnNavigate(InputAction.CallbackContext context)
    {
        if (EventSystem.current.currentSelectedGameObject == null && lastSelected != null)
        {
            // Reselect the last selected object if none is currently selected
            SelectButton(lastSelected);
        }
    }
}
