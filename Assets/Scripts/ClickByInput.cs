using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ClickByInput : MonoBehaviour
{
    [SerializeField] private InputActionReference scrollInput; // Input action reference
    [SerializeField] private UnityEvent onClickEvent;          // Event to assign functions via Inspector

    private void OnEnable()
    {
        // Enable the action and subscribe to the events
        scrollInput.action.Enable();
        scrollInput.action.performed += OnClick;
    }

    private void OnClick(InputAction.CallbackContext context)
    {
        // Invoke the UnityEvent to run the assigned function(s)
        onClickEvent?.Invoke();
    }

    private void OnDisable()
    {
        // Unsubscribe from the events and disable the action
        scrollInput.action.performed -= OnClick;
        scrollInput.action.Disable();
    }
}
