using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Bottom-of-screen interaction prompt, driven by Interact whenever its raycast is hovering a
// usable IInteractable. Same static-singleton pattern as SpectateHUD: drop
// Assets/PreFabs/Ui/InteractionPromptHUD.prefab into the game scene once, style it there.
public class InteractionPromptHUD : MonoBehaviour
{
    public static InteractionPromptHUD Instance { get; private set; }

    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Image promptIcon;
    [SerializeField] private ControllerIconSet gamepadIcons;

    // Multiple sources (Interact's raycast, TeamUp's proximity check) call Show/Hide every
    // frame on this same singleton. Whichever claims Show() first in a frame wins the frame:
    // a later Hide() from a source that found nothing of its own is a no-op instead of
    // stomping the prompt a sibling component just showed.
    private int lastShowFrame = -1;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (promptRoot != null)
        {
            promptRoot.SetActive(false);
        }
    }

    /// <summary>Shows the prompt, rendering the action's live binding as a controller button
    /// icon when the last input came from a gamepad, or as bracketed text otherwise (e.g.
    /// "[E] Pick Up") - matching how the controls settings menu displays bindings.</summary>
    public static void Show(string actionLabel, InputAction action)
    {
        if (Instance == null || Instance.promptRoot == null || Instance.promptText == null)
        {
            return;
        }

        Instance.promptRoot.SetActive(true);

        bool isGamepad = action != null && action.activeControl != null && action.activeControl.device is Gamepad;
        Sprite icon = isGamepad ? Instance.gamepadIcons.GetSprite(action.activeControl.name) : null;

        if (icon != null && Instance.promptIcon != null)
        {
            Instance.promptIcon.sprite = icon;
            Instance.promptIcon.gameObject.SetActive(true);
            Instance.promptText.text = actionLabel;
        }
        else
        {
            if (Instance.promptIcon != null)
            {
                Instance.promptIcon.gameObject.SetActive(false);
            }

            string bindingDisplayString = action != null
                ? action.GetBindingDisplayString(group: isGamepad ? "Gamepad" : "Keyboard")
                : null;
            Instance.promptText.text = string.IsNullOrEmpty(bindingDisplayString)
                ? actionLabel
                : $"[{bindingDisplayString}] {actionLabel}";
        }

        Instance.lastShowFrame = Time.frameCount;
    }

    public static void Hide()
    {
        if (Instance == null || Instance.promptRoot == null)
        {
            return;
        }
        if (Instance.lastShowFrame == Time.frameCount)
        {
            return;
        }
        Instance.promptRoot.SetActive(false);
    }

    // Mirrors the Input System Rebinding UI sample's GamepadIconsExample icon set/lookup
    // (Assets/Samples/Input System/.../GamepadIconsExample.cs), keyed the same way
    // (InputControl.name, e.g. "buttonSouth") so the same sprite assets can be reused.
    [System.Serializable]
    public struct ControllerIconSet
    {
        public Sprite buttonSouth;
        public Sprite buttonNorth;
        public Sprite buttonEast;
        public Sprite buttonWest;
        public Sprite leftShoulder;
        public Sprite rightShoulder;
        public Sprite leftTrigger;
        public Sprite rightTrigger;

        public readonly Sprite GetSprite(string controlPath)
        {
            switch (controlPath)
            {
                case "buttonSouth": return buttonSouth;
                case "buttonNorth": return buttonNorth;
                case "buttonEast": return buttonEast;
                case "buttonWest": return buttonWest;
                case "leftShoulder": return leftShoulder;
                case "rightShoulder": return rightShoulder;
                case "leftTrigger": return leftTrigger;
                case "rightTrigger": return rightTrigger;
                default: return null;
            }
        }
    }
}
