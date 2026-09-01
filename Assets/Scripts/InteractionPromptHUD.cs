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
    /// icon when the player is on a gamepad, or as bracketed text otherwise (e.g. "[E] Pick Up") -
    /// matching how the controls settings menu displays bindings.</summary>
    public static void Show(string actionLabel, InputAction action)
    {
        if (Instance == null || Instance.promptRoot == null || Instance.promptText == null)
        {
            return;
        }

        Instance.promptRoot.SetActive(true);

        // Device choice comes from InputDeviceTracker (what the player last actually used), not
        // from action.activeControl. activeControl is only non-null while THIS action is being
        // driven, so an idle Interact action always reported null: isGamepad was false on every
        // frame the prompt was actually on screen, the icon branch below was unreachable, and a
        // controller player was permanently told to press the keyboard key.
        bool isGamepad = InputDeviceTracker.IsGamepad;
        string group = isGamepad ? InputDeviceTracker.GamepadGroup : InputDeviceTracker.KeyboardGroup;

        // Resolved from the action's bindings for the same reason - the control the player would
        // press, rather than one they happen to be holding right now.
        Sprite icon = isGamepad
            ? Instance.gamepadIcons.GetSprite(InputDeviceTracker.ResolveControlName(action, group))
            : null;

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

            string bindingDisplayString = GetBindingLabel(action);
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

    /// <summary>Display name of this action's binding on the device currently in use ("A",
    /// "Space", ...). Shared with the text-only prompts (e.g. SpectateHUD's hint line) so every
    /// on-screen instruction names the same device.</summary>
    public static string GetBindingLabel(InputAction action)
    {
        if (action == null)
        {
            return null;
        }

        string group = InputDeviceTracker.CurrentGroup;
        string label = action.GetBindingDisplayString(group: group);

        // An action with no binding in the active scheme (e.g. a keyboard-only action while the
        // player is on a pad) would otherwise render as an empty "[] Do Thing".
        return string.IsNullOrEmpty(label) ? action.GetBindingDisplayString() : label;
    }

    /// <summary>Bracketed "[A] Next"-style instruction for text-only HUDs.</summary>
    public static string FormatHint(string actionLabel, InputAction action)
    {
        string binding = GetBindingLabel(action);
        return string.IsNullOrEmpty(binding) ? actionLabel : $"[{binding}] {actionLabel}";
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

        // Added alongside the original eight, which only covered the face buttons and bumpers:
        // Inputs.inputactions also binds Talk to dpad/down, Pause to start and Options to select,
        // and Move/Look/Scroll to the sticks. Those all fell through to null and silently
        // rendered as text on a controller.
        public Sprite dpadUp;
        public Sprite dpadDown;
        public Sprite dpadLeft;
        public Sprite dpadRight;
        public Sprite dpad;
        public Sprite leftStick;
        public Sprite rightStick;
        public Sprite startButton;
        public Sprite selectButton;

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
                case "dpad/up": return dpadUp != null ? dpadUp : dpad;
                case "dpad/down": return dpadDown != null ? dpadDown : dpad;
                case "dpad/left": return dpadLeft != null ? dpadLeft : dpad;
                case "dpad/right": return dpadRight != null ? dpadRight : dpad;
                case "dpad": return dpad;
                case "leftStick": return leftStick;
                case "leftStickPress": return leftStick;
                case "rightStick": return rightStick;
                case "rightStickPress": return rightStick;
                case "start": return startButton;
                case "select": return selectButton;
                default: return null;
            }
        }
    }
}
