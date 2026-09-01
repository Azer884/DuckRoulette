using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Tracks which device the player last actually used, so on-screen prompts can decide between a
// controller button icon and a keyboard/mouse key name.
//
// The prompts used to answer that question with InputAction.activeControl, which is only non-null
// while that specific action is being driven - an idle Interact action always reported null, so
// every prompt fell back to the keyboard branch and the gamepad icons on the HUD prefab could
// never be reached. Device detection has to be global (what did the player touch last, on any
// action) rather than per-action, which is what this provides.
//
// Note the deliberate UnityEngine.InputSystem.InputSystem qualification below: this project has
// its own MonoBehaviour named InputSystem in the global namespace (Player/InputSystem.cs) that
// otherwise shadows the package's static class.
public static class InputDeviceTracker
{
    // Match the control scheme names in Assets/Scripts/InputSys/Inputs.inputactions - these are
    // the groups binding lookups and GetBindingDisplayString are filtered by.
    public const string GamepadGroup = "Gamepad";
    public const string KeyboardGroup = "Keyboard";

    /// <summary>True when the last actuated control came from a gamepad.</summary>
    public static bool IsGamepad { get; private set; }

    /// <summary>Control scheme group matching the device in use, for binding lookups.</summary>
    public static string CurrentGroup => IsGamepad ? GamepadGroup : KeyboardGroup;

    /// <summary>Raised when the player switches between gamepad and keyboard/mouse.</summary>
    public static event Action<bool> DeviceChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Best guess before the player has touched anything: a pad-only machine starts on pad.
        IsGamepad = Gamepad.current != null && Keyboard.current == null && Mouse.current == null;

        // Unsubscribe first: this is a static event and "Enter Play Mode without domain reload"
        // keeps both it and this class's statics alive between play sessions.
        UnityEngine.InputSystem.InputSystem.onActionChange -= HandleActionChange;
        UnityEngine.InputSystem.InputSystem.onActionChange += HandleActionChange;
    }

    private static void HandleActionChange(object actionObject, InputActionChange change)
    {
        // Started/Performed only: a Canceled callback fires as a control returns to its resting
        // value, which for a released gamepad button would otherwise re-assert "gamepad" after the
        // player has already moved to the mouse.
        if (change != InputActionChange.ActionStarted && change != InputActionChange.ActionPerformed)
        {
            return;
        }

        if (!(actionObject is InputAction action))
        {
            return;
        }

        InputControl control = action.activeControl;
        if (control == null)
        {
            return;
        }

        SetIsGamepad(control.device is Gamepad);
    }

    private static void SetIsGamepad(bool value)
    {
        if (IsGamepad == value)
        {
            return;
        }

        IsGamepad = value;
        DeviceChanged?.Invoke(value);
    }

    /// <summary>
    /// The control name a prompt should draw for this action on the device in use - e.g.
    /// "buttonSouth" or "dpad/down" - resolved from the action's own bindings rather than from
    /// <see cref="InputAction.activeControl"/>, so it works while the action is idle. Returns the
    /// effective path, so a rebound control resolves to whatever the player rebound it to.
    /// </summary>
    public static string ResolveControlName(InputAction action, string group)
    {
        if (action == null || string.IsNullOrEmpty(group))
        {
            return null;
        }

        foreach (InputBinding binding in action.bindings)
        {
            // A composite's own entry (e.g. "WASD" 2DVector) carries no path of its own; its parts
            // do, but a single-button prompt has nothing useful to show for a multi-part composite.
            if (binding.isComposite || binding.isPartOfComposite)
            {
                continue;
            }

            // Bindings in this asset can carry an empty leading group (";Gamepad"), so match on
            // the parsed group list rather than on string equality with the whole field.
            if (!BindingBelongsToGroup(binding, group))
            {
                continue;
            }

            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            // "<Gamepad>/dpad/down" -> "dpad/down", "<Gamepad>/buttonSouth" -> "buttonSouth".
            int deviceEnd = path.IndexOf('>');
            return deviceEnd >= 0 && deviceEnd + 2 <= path.Length - 1
                ? path.Substring(deviceEnd + 2)
                : path;
        }

        return null;
    }

    private static bool BindingBelongsToGroup(InputBinding binding, string group)
    {
        if (string.IsNullOrEmpty(binding.groups))
        {
            return false;
        }

        foreach (string candidate in binding.groups.Split(InputBinding.Separator))
        {
            if (string.Equals(candidate, group, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
