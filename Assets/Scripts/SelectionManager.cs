using System.Collections;
using System.Collections.Generic;
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
        }
    }

    private void RegisterChildButtons(Transform parent)
    {
        foreach (Transform child in parent)
        {
            // Check if the child has a Button component and EventTrigger
            if (child.TryGetComponent(out Button button) && child.GetComponent<EventTrigger>() != null)
            {
                RegisterButton(button.gameObject);
            }

            // Recursively check child objects
            RegisterChildButtons(child);
        }
    }

    public void RegisterButton(GameObject button)
    {
        if (button != null && registeredButtons.Add(button)) // Only add if not already registered
        {
            // Listen to existing EventTrigger if available
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger != null)
            {
                foreach (var existingEntry in trigger.triggers) // Renamed "entry" to "existingEntry"
                {
                    if (existingEntry.eventID == EventTriggerType.Select)
                    {
                        // Event already handled; no need to add again
                        return;
                    }
                }

                // Add a Select event to update the last selected button
                EventTrigger.Entry newEntry = new EventTrigger.Entry // Renamed "entry" to "newEntry"
                {
                    eventID = EventTriggerType.Select
                };
                newEntry.callback.AddListener((eventData) => UpdateLastSelected(button));
                trigger.triggers.Add(newEntry);
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
