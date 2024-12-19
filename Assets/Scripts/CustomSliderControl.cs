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
    }
    private void OnEnable()
    {
        // Enable input actions
        if (rightStickInput != null) rightStickInput.action.Enable();

        rightStickInput.action.performed += OnRightStickMoved;
        rightStickInput.action.canceled += OnRightStickReleased;
    }

    private void OnDisable()
    {
        // Disable input actions
        if (rightStickInput != null) rightStickInput.action.Disable();

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
            targetSlider.value += rightStickValue.x * sliderSensitivity * Time.deltaTime;
            targetSlider.value = Mathf.Clamp01(targetSlider.value);
        }
    }
}
