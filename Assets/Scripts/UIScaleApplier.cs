using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Applies the Accessibility tab's UI Scale to every CanvasScaler in the game.
//
// Self-installing the same way HitFlash/CameraShaker are, so no scene or prefab has to be wired
// for it: a hidden DontDestroyOnLoad object is created on load, and it re-applies on every scene
// load and whenever the setting changes.
//
// CanvasScaler.scaleFactor only does anything in Constant Pixel Size mode, and every canvas here
// is Scale With Screen Size - so the scale is applied by dividing the reference resolution
// instead. A smaller reference resolution means fewer reference pixels across the screen, which
// means everything drawn at a fixed reference size gets bigger.
public class UIScaleApplier : MonoBehaviour
{
    private static UIScaleApplier instance;

    private readonly Dictionary<CanvasScaler, Vector2> baseReferenceResolutions = new();
    private readonly List<CanvasScaler> scratch = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null)
        {
            return;
        }

        GameObject host = new GameObject(nameof(UIScaleApplier)) { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(host);
        instance = host.AddComponent<UIScaleApplier>();
    }

    private void OnEnable()
    {
        GameplaySettings.Changed += Apply;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    private void OnDisable()
    {
        GameplaySettings.Changed -= Apply;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply();
    }

    private void Apply()
    {
        float scale = Mathf.Clamp(GameplaySettings.UIScale, 0.75f, 1.5f);

        scratch.Clear();
        scratch.AddRange(FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        foreach (CanvasScaler scaler in scratch)
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                continue;
            }

            // The authored resolution is captured the first time this scaler is seen, so repeated
            // applies compound off the original rather than off the last scaled value.
            if (!baseReferenceResolutions.TryGetValue(scaler, out Vector2 baseResolution))
            {
                baseResolution = scaler.referenceResolution;
                baseReferenceResolutions[scaler] = baseResolution;
            }

            scaler.referenceResolution = baseResolution / scale;
        }

        PruneDestroyed();
    }

    // Scalers die with their scenes; drop them so the cache does not grow across a session.
    private void PruneDestroyed()
    {
        scratch.Clear();
        foreach (KeyValuePair<CanvasScaler, Vector2> entry in baseReferenceResolutions)
        {
            if (entry.Key == null)
            {
                scratch.Add(entry.Key);
            }
        }

        foreach (CanvasScaler dead in scratch)
        {
            baseReferenceResolutions.Remove(dead);
        }

        scratch.Clear();
    }
}
