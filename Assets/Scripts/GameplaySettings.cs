using System.Globalization;
using UnityEngine;

// Read side of the Settings.ini "Game" and "Accessibility" sections.
//
// SettingsManager owns the file, the UI writes into it through ObjectSettings the same way the
// Graphics/Audio tabs do, and everything that has to *react* to those values (Movement, the screen
// effects, the HUD) reads them from here instead of parsing strings on its own. Values are cached
// and only re-parsed when SettingsManager says something changed, because several of these are read
// every frame (camera shake, look inversion).
public static class GameplaySettings
{
    public const string GameSection = "Game";
    public const string AccessibilitySection = "Accessibility";

    // Raised after the cache has been invalidated, for the one-shot appliers (field of view,
    // UI scale) that push a value somewhere rather than polling it.
    public static event System.Action Changed;

    private static bool cacheValid;

    private static float fieldOfView;
    private static bool invertLookY;
    private static float cameraShake;
    private static bool toggleCrouch;
    private static bool toggleSprint;
    private static bool showTaskList;

    private static bool reduceMotion;
    private static bool flashingEffects;
    private static float screenEffects;
    private static float uiScale;

    public const float MinFieldOfView = 60f;
    public const float MaxFieldOfView = 110f;
    public const float DefaultFieldOfView = 60f;

    public static float FieldOfView { get { EnsureCache(); return fieldOfView; } }
    public static bool InvertLookY { get { EnsureCache(); return invertLookY; } }
    public static bool ToggleCrouch { get { EnsureCache(); return toggleCrouch; } }
    public static bool ToggleSprint { get { EnsureCache(); return toggleSprint; } }
    public static bool ShowTaskList { get { EnsureCache(); return showTaskList; } }

    public static bool ReduceMotion { get { EnsureCache(); return reduceMotion; } }
    public static bool FlashingEffects { get { EnsureCache(); return flashingEffects; } }
    public static float UIScale { get { EnsureCache(); return uiScale; } }

    // Reduce Motion is the master switch for anything that moves the camera on the player's
    // behalf, so it wins over the Game tab's own shake slider rather than the two fighting.
    public static float CameraShakeScale
    {
        get
        {
            EnsureCache();
            return reduceMotion ? 0f : Mathf.Clamp01(cameraShake);
        }
    }

    // Master multiplier for the post-process punches (hit flash, death vignette, dizziness). At 0
    // they are off entirely; Flashing Effects gates the sharp ones separately.
    public static float ScreenEffectScale
    {
        get
        {
            EnsureCache();
            return Mathf.Clamp01(screenEffects);
        }
    }

    // Called by SettingsManager whenever the file is (re)loaded or a Game/Accessibility control
    // writes to it.
    public static void Invalidate()
    {
        cacheValid = false;
        Changed?.Invoke();
    }

    private static void EnsureCache()
    {
        if (cacheValid)
        {
            return;
        }

        // Without a SettingsManager (unit scenes, the very first frames of a boot) every value
        // falls back to its default and the cache is left invalid so the real file wins later.
        SettingsManager settings = SettingsManager.Instance;

        fieldOfView = Mathf.Clamp(GetFloat(settings, GameSection, "FieldOfView", DefaultFieldOfView), MinFieldOfView, MaxFieldOfView);
        invertLookY = GetBool(settings, GameSection, "InvertLookY", false);
        cameraShake = GetFloat(settings, GameSection, "CameraShake", 1f);
        toggleCrouch = GetBool(settings, GameSection, "ToggleCrouch", false);
        toggleSprint = GetBool(settings, GameSection, "ToggleSprint", false);
        showTaskList = GetBool(settings, GameSection, "ShowTaskList", true);

        reduceMotion = GetBool(settings, AccessibilitySection, "ReduceMotion", false);
        flashingEffects = GetBool(settings, AccessibilitySection, "FlashingEffects", true);
        screenEffects = GetFloat(settings, AccessibilitySection, "ScreenEffects", 1f);
        uiScale = Mathf.Clamp(GetFloat(settings, AccessibilitySection, "UIScale", 1f), 0.75f, 1.5f);

        cacheValid = settings != null;
    }

    private static float GetFloat(SettingsManager settings, string section, string key, float defaultValue)
    {
        if (settings == null)
        {
            return defaultValue;
        }

        string raw = settings.GetSetting(section, key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : defaultValue;
    }

    private static bool GetBool(SettingsManager settings, string section, string key, bool defaultValue)
    {
        if (settings == null)
        {
            return defaultValue;
        }

        string raw = settings.GetSetting(section, key, defaultValue ? "true" : "false");
        return bool.TryParse(raw, out bool value) ? value : defaultValue;
    }
}
