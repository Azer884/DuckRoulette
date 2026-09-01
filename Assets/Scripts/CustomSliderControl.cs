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
        if (rightStickInput == null) return;

        rightStickInput.action.performed += OnRightStickMoved;
        rightStickInput.action.canceled += OnRightStickReleased;
    }

    private void OnDisable()
    {
        if (rightStickInput == null) return;

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
            // Stepped and clamped against the slider's OWN range, not a hard-coded 0..1. The
            // sensitivity sliders in SettingsMenu run 0.01 to 5, so Mathf.Clamp01 made the stick
            // physically unable to push any of them past 1 - and at 1 the handle sat about a fifth
            // of the way along its track, which is the "slider isn't full when it hits 1" part.
            // Scaling the step by the range keeps the same end-to-end travel time on any slider
            // instead of taking five times as long on a 0..5 one.
            float range = targetSlider.maxValue - targetSlider.minValue;
            float newValue = targetSlider.value + rightStickValue.x * sliderSensitivity * range * Time.deltaTime;

            targetSlider.value = Mathf.Clamp(newValue, targetSlider.minValue, targetSlider.maxValue);
        }
    }

}
