using System;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ScrollToSelected : MonoBehaviour
{
    [SerializeField] private RectTransform content; // Reference to the scrollable content
    [SerializeField] private InputActionReference rightStickInput; // Input action for the right stick
    [SerializeField] private float scrollSpeed = 750; // Speed multiplier for scrolling
    [SerializeField] private float deadZone = 0.2f; // Dead zone threshold for stick drift
    [SerializeField] private bool vertical;

    private Vector2 rightStickValue; // Stores the current right stick input

    private void OnEnable()
    {
        // Enable the action and subscribe to the events
        rightStickInput.action.Enable();
        rightStickInput.action.performed += OnRightStickMoved;
        rightStickInput.action.canceled += OnRightStickReleased;
    }

    private void OnDisable()
    {
        // Unsubscribe from the events and disable the action
        rightStickInput.action.performed -= OnRightStickMoved;
        rightStickInput.action.canceled -= OnRightStickReleased;
        rightStickInput.action.Disable();
    }

    private void OnRightStickMoved(InputAction.CallbackContext context)
    {
        // Store the current right stick value
        rightStickValue = context.ReadValue<Vector2>();
    }

    private void OnRightStickReleased(InputAction.CallbackContext context)
    {
        // Reset the right stick value when input is released
        rightStickValue = Vector2.zero;
    }

    private void Update()
    {
        // Apply a dead zone to the stick input
        if (Mathf.Abs(rightStickValue.y) > deadZone && IsChildSelected() && vertical)
        {
            // Scroll the content based on the Y value of the right stick
            content.anchoredPosition += Vector2.down * rightStickValue.y * scrollSpeed * Time.deltaTime;
        }
        else if (Mathf.Abs(rightStickValue.x) > deadZone && IsChildSelected() && !vertical)
        {
            // Scroll the content based on the Y value of the right stick
            content.anchoredPosition += Vector2.left * rightStickValue.x * scrollSpeed * Time.deltaTime;
        }
    }

    // Checks if any child of 'content' is currently selected
    private bool IsChildSelected()
    {
        GameObject currentSelected = EventSystem.current.currentSelectedGameObject;

        if (currentSelected == null) return false;

        // Check if the currently selected object is a child of the content
        return currentSelected.transform.IsChildOf(content);
    }
}
