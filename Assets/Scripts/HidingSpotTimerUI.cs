using UnityEngine;
using UnityEngine.UI;
using Weather;

// The hiding-spot countdown ring that sits under the round shot clock (Assets/PreFabs/Ui/
// ShotClockUI.prefab's HiddingBar/2ndBar and HiddingBar/3rdBar). Unlike the round timer it carries
// no number - it reduces by rotating the whole bar image, the same "half-dial" look as the round
// clock's own Widget/Fill, just tinted to match whichever hiding spot the local player is in.
//
// 2ndBar is the normal slot. When a storm is also running, WeatherAlertHUD's own bar docks at
// that same slot instead (see WeatherAlertHUD.GetDockPosition), so this bumps itself down to
// 3rdBar for the duration rather than overlapping it.
public class HidingSpotTimerUI : MonoBehaviour
{
    /// <summary>So WeatherAlertHUD can dock its own storm bar at this same slot instead of its
    /// old clock-relative fallback - the two bars take turns at one position rather than sharing
    /// the screen.</summary>
    public static HidingSpotTimerUI Instance { get; private set; }

    [SerializeField, Tooltip("Parent CanvasGroup of both bars - authored at alpha 0 in the " +
        "prefab, so it must be driven here or neither bar ever actually renders regardless of " +
        "its own active state.")]
    private CanvasGroup groupAlpha;
    [SerializeField] private GameObject secondBar;
    [SerializeField] private Image secondBarFill;
    [SerializeField] private Image secondBarTrack;

    [SerializeField] private GameObject thirdBar;
    [SerializeField] private Image thirdBarFill;
    [SerializeField] private Image thirdBarBackground;

    [Header("Rotation")]
    [SerializeField, Tooltip("Degrees the bar sweeps from full (0) to empty - a half turn, not a full one.")]
    private float fullSweepDegrees = 180f;

    [Header("Background day/night")]
    [SerializeField] private Color dayBackgroundColor = Color.black;
    [SerializeField] private Color nightBackgroundColor = Color.white;
    [SerializeField, Tooltip("Preserves the track/background's own alpha - only RGB is lerped.")]
    private bool preserveBackgroundAlpha = true;

    [Header("Urgent shake")]
    [SerializeField] private float urgentThreshold = 3f;
    [SerializeField] private float shakeAmplitude = 0.08f;
    [SerializeField] private float shakeFrequency = 18f;

    private float hideDuration = -1f;

    /// <summary>The slot-2 bar's rect, so WeatherAlertHUD can dock there. Non-null even while the
    /// bar itself is hidden (no hiding spot occupied) - only its position/size are needed.</summary>
    public RectTransform SecondBarRect => secondBar != null ? secondBar.transform as RectTransform : null;

    /// <summary>True while either bar is actually on screen - ShotClockUI hides its own round-timer
    /// number while this is true, matching "a number only ever shows while one bar is up".</summary>
    public bool IsShowing { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        HidingSpot active = HidingSpot.LocalActive;

        if (active == null)
        {
            SetVisible(false, false);
            hideDuration = -1f;
            IsShowing = false;
            if (groupAlpha != null)
            {
                groupAlpha.alpha = 0f;
            }
            return;
        }

        IsShowing = true;
        if (groupAlpha != null)
        {
            groupAlpha.alpha = 1f;
        }

        // hideDuration only changes when a new spot starts being tracked - cached rather than
        // read every frame off the component, since RemainingHideTime is the only thing that
        // actually ticks.
        if (hideDuration < 0f)
        {
            hideDuration = Mathf.Max(0.01f, active.hideDuration);
        }

        bool stormActive = WeatherSystem.Instance != null && WeatherSystem.Instance.Phase == WeatherPhase.Storm;
        SetVisible(!stormActive, stormActive);

        float remaining = active.RemainingHideTime;
        float fraction = Mathf.Clamp01(remaining / hideDuration);
        bool urgent = remaining <= urgentThreshold;
        bool isNight = WeatherSystem.Instance != null && WeatherSystem.Instance.IsNight;
        bool badWeather = WeatherSystem.Instance != null &&
            (WeatherSystem.Instance.Phase == WeatherPhase.Rain || WeatherSystem.Instance.Phase == WeatherPhase.Storm);
        float whiteLerp = isNight || badWeather ? 1f : 0f;

        if (stormActive)
        {
            Apply(thirdBarFill, thirdBarBackground, active.barColor, fraction, urgent, whiteLerp);
        }
        else
        {
            Apply(secondBarFill, secondBarTrack, active.barColor, fraction, urgent, whiteLerp);
        }
    }

    private void Apply(Image fill, Image background, Color tint, float fraction, bool urgent, float whiteLerp)
    {
        if (fill == null)
        {
            return;
        }

        fill.color = tint;

        // Rotates CCW as time runs out - full at 0deg, empty at -fullSweepDegrees.
        float angle = Mathf.Lerp(0f, fullSweepDegrees, 1f - fraction);
        Vector3 shake = Vector3.zero;
        if (urgent)
        {
            shake = new Vector3(
                (Mathf.PerlinNoise(Time.unscaledTime * shakeFrequency, 0f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0f, Time.unscaledTime * shakeFrequency) - 0.5f) * 2f,
                0f) * shakeAmplitude * 100f;
        }

        fill.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
        fill.rectTransform.anchoredPosition = shake;

        if (background != null)
        {
            Color baseColor = Color.Lerp(dayBackgroundColor, nightBackgroundColor, whiteLerp);
            if (preserveBackgroundAlpha)
            {
                baseColor.a = background.color.a;
            }
            background.color = baseColor;
        }
    }

    private void SetVisible(bool second, bool third)
    {
        if (secondBar != null && secondBar.activeSelf != second)
        {
            secondBar.SetActive(second);
        }
        if (thirdBar != null && thirdBar.activeSelf != third)
        {
            thirdBar.SetActive(third);
        }
    }
}
