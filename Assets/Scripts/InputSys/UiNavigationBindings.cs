using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// Takes the right stick off UI selection navigation.
//
// The EventSystems in this project drive their InputSystemUIInputModule from the Input System
// package's own DefaultInputActions asset, whose "Navigate" composite binds BOTH sticks:
// leftStick/up|down|left|right AND rightStick/up|down|left|right. So pushing the right stick moved
// the highlighted UI element - while the right stick is also what CustomSliderControl uses to drag
// a slider and what ScrollToSelected uses to scroll a list. Adjusting a settings slider therefore
// fought the selection jumping off that slider at the same time.
//
// That asset lives in the read-only package cache, so it is fixed here at runtime instead: an
// empty override path disables an individual binding without touching the asset on disk, and it is
// re-applied per scene load because each scene carries its own EventSystem. leftStick and the d-pad
// are untouched, so gamepad menu navigation still works exactly as before.
public static class UiNavigationBindings
{
    private const string DisabledControl = "rightStick";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // Unsubscribe first: this is a static event and "Enter Play Mode without domain reload"
        // keeps the subscription alive between play sessions.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        Apply();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply();
    }

    private static void Apply()
    {
        var modules = UnityEngine.Object.FindObjectsByType<InputSystemUIInputModule>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (InputSystemUIInputModule module in modules)
        {
            if (module == null || module.move == null)
            {
                continue;
            }

            DisableRightStickBindings(module.move.action);
        }
    }

    private static void DisableRightStickBindings(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];

            // The composite's own entry carries no path - only its parts bind real controls.
            if (binding.isComposite)
            {
                continue;
            }

            // Already disabled by a previous pass (this runs again on every scene load, and the
            // actions asset instance is shared, so most passes after the first find nothing to do).
            if (binding.overridePath == string.Empty)
            {
                continue;
            }

            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path) ||
                path.IndexOf(DisabledControl, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            action.ApplyBindingOverride(i, new InputBinding { overridePath = string.Empty });
        }
    }
}
