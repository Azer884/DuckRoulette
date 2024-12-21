using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ScrollThroughToggles : MonoBehaviour
{
    [SerializeField] private Toggle[] toggles;
    [SerializeField] private InputActionReference scrollInput;
    private int index;

    private void Awake() {
        scrollInput.action.Enable();
    }

    private void OnEnable()
    {
        scrollInput.action.performed += OnClick;
    }


    private void OnDisable()
    {
        // Unsubscribe from the events and disable the action
        scrollInput.action.performed -= OnClick;
    }
    private void OnClick(InputAction.CallbackContext context)
    {
        index += (int)context.ReadValue<Vector2>().x;
        index %= toggles.Length;
        if (index < 0)
        {
            index = toggles.Length - 1;
        }

        toggles[index].isOn = true;
        EventSystem.current.SetSelectedGameObject(toggles[index].gameObject);
    }
}
