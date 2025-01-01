using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ClickByInput : MonoBehaviour
{
    public InputActionReference scrollInput; // Input action reference
    [SerializeField] private UnityEvent onClickEvent;

    private void Awake() {
        scrollInput.action.Enable();
    }

    private void OnEnable()
    {
        // Enable the action and subscribe to the events
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
    }
}
