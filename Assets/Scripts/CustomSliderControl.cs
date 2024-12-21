using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CustomSliderControl : MonoBehaviour
{
    [SerializeField] private InputActionReference rightStickInput;
    [SerializeField] private float sliderSensitivity = 0.5f;
    private Vector2 rightStickValue;
    private Slider targetSlider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        targetSlider = GetComponent<Slider>();
        if (rightStickInput != null) rightStickInput.action.Enable();
    }
    private void OnEnable()
    {

        rightStickInput.action.performed += OnRightStickMoved;
        rightStickInput.action.canceled += OnRightStickReleased;
    }

    private void OnDisable()
    {
        rightStickInput.action.performed -= OnRightStickMoved;
        rightStickInput.action.canceled -= OnRightStickReleased;
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
        if (targetSlider == null) return;

        if (Mathf.Abs(rightStickValue.x) > 0.1f && EventSystem.current.currentSelectedGameObject == gameObject) // Deadzone threshold
        {
            // Modify the slider value based on right stick input
            float newValue = targetSlider.value + rightStickValue.x * sliderSensitivity * Time.deltaTime;

            // Clamp the new value between 0 and 1 and assign it back to the slider
            targetSlider.value = Mathf.Clamp01(newValue);
        }
    }

}
