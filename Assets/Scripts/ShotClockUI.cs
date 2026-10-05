using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Weather;

// Player-facing feedback for RoundManager's per-round shot clock (previously a
// NetworkVariable<float> with no UI reading it at all - the gun holder got force-shot with
// zero warning that a timer was even running).
//
// All visuals live on Assets/PreFabs/Ui/ShotClockUI.prefab - drop that prefab into the game
// scene once and edit colors/layout/fonts there like any other UI. This script only drives it.
public class ShotClockUI : MonoBehaviour
{
    /// <summary>The live shot clock, so a HUD element that sits alongside it (the weather bar)
    /// can find it without searching the scene every frame.</summary>
    public static ShotClockUI Instance { get; private set; }

    [SerializeField] private GameObject root;
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI turnLabel;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new(0.95f, 0.95f, 0.95f, 0.95f);
    [SerializeField] private Color yourTurnColor = new(1f, 0.75f, 0.15f, 1f);
    [SerializeField] private Color urgentColor = new(1f, 0.25f, 0.2f, 1f);
    [SerializeField] private float urgentThreshold = 5f;

    [Header("Rotation")]
    [SerializeField, Tooltip("Degrees the fill sweeps from full (0) to empty - a half turn, not a full one.")]
    private float fullSweepDegrees = 180f;

    [Header("Background day/night")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Color dayBackgroundColor = Color.black;
    [SerializeField] private Color nightBackgroundColor = Color.white;

    [Header("Number fade")]
    [SerializeField, Tooltip("Seconds the number takes to fade out/in when a second bar " +
        "(hiding spot or storm) joins or leaves this one.")]
    private float numberFadeDuration = 0.4f;

    private int _lastTickSecond = -1;
    private CanvasGroup timerTextGroup;
    private bool? lastHideNumber;
    private Coroutine numberFadeRoutine;

    /// <summary>The clock widget's rect. Non-null even while hidden, so check
    /// <see cref="IsShowing"/> too before positioning against it.</summary>
    public RectTransform Widget => root != null ? root.transform as RectTransform : null;

    /// <summary>True while the clock is actually on screen.</summary>
    public bool IsShowing => root != null && root.activeInHierarchy;

    private void Awake()
    {
        Instance = this;

        if (root != null)
        {
            root.SetActive(false);
        }

        // Not who has the gun stays hidden on purpose - the whole game is not knowing that.
        if (turnLabel != null)
        {
            turnLabel.gameObject.SetActive(false);
        }

        if (timerText != null)
        {
            timerTextGroup = timerText.GetComponent<CanvasGroup>();
            if (timerTextGroup == null)
            {
                timerTextGroup = timerText.gameObject.AddComponent<CanvasGroup>();
            }
        }
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
        if (root == null || fillImage == null || timerText == null)
        {
            return;
        }

        if (RoundManager.Instance == null || GameManager.Instance == null ||
            NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
            PlayerSpawner.Instance == null || !PlayerSpawner.Instance.isStarted)
        {
            if (root.activeSelf)
            {
                root.SetActive(false);
            }
            return;
        }

        // Stays visible for the whole match once a round has started, instead of toggling off
        // between rounds (the ~5s gap while the gun hands off) - it was popping in and out every
        // single turn, which read as "the timer disappeared".
        if (!root.activeSelf)
        {
            root.SetActive(true);
        }

        float remaining = RoundManager.Instance.RemainingTime;
        float duration = Mathf.Max(0.01f, RoundManager.Instance.RoundDuration);
        float ratio = Mathf.Clamp01(remaining / duration);

        // Rotates CCW as the round runs out instead of draining a fill wedge - full at 0deg,
        // empty at -fullSweepDegrees.
        float angle = Mathf.Lerp(0f, fullSweepDegrees, 1f - ratio);
        fillImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);

        // Numbers only ever belong on this, the always-on bar - the moment a second bar (hiding
        // spot or storm) joins it, this one goes back to being a plain bar too. Fades rather than
        // snaps off, so the number doesn't just vanish the instant the storm bar appears.
        bool secondBarShowing = HidingSpotTimerUI.Instance != null && HidingSpotTimerUI.Instance.IsShowing;
        bool stormBarShowing = WeatherSystem.Instance != null && WeatherSystem.Instance.Phase == WeatherPhase.Storm;
        bool hideNumber = secondBarShowing || stormBarShowing;

        if (lastHideNumber != hideNumber)
        {
            lastHideNumber = hideNumber;
            if (numberFadeRoutine != null)
            {
                StopCoroutine(numberFadeRoutine);
            }
            numberFadeRoutine = StartCoroutine(FadeNumber(hideNumber));
        }

        if (!hideNumber)
        {
            timerText.text = Mathf.CeilToInt(remaining).ToString();
        }

        if (backgroundImage != null)
        {
            bool isNight = WeatherSystem.Instance != null && WeatherSystem.Instance.IsNight;
            bool badWeather = WeatherSystem.Instance != null && WeatherSystem.Instance.Phase != WeatherPhase.Clear;
            float whiteLerp = isNight || badWeather ? 1f : 0f;
            Color bgColor = Color.Lerp(dayBackgroundColor, nightBackgroundColor, whiteLerp);
            bgColor.a = backgroundImage.color.a;
            backgroundImage.color = bgColor;
        }

        ulong gunHolder = GameManager.Instance.playerWithGun.Value;
        bool isMyTurn = gunHolder == NetworkManager.Singleton.LocalClientId;
        bool urgent = remaining <= urgentThreshold;

        Color color = urgent ? urgentColor : (isMyTurn ? yourTurnColor : normalColor);
        fillImage.color = color;
        timerText.color = color;

        root.transform.localScale = urgent
            ? Vector3.one * (1f + Mathf.PingPong(Time.time * 4f, 0.12f))
            : Vector3.one;

        TickAudio(remaining, urgent);
    }

    private IEnumerator FadeNumber(bool hide)
    {
        if (timerTextGroup == null)
        {
            if (timerText != null)
            {
                timerText.gameObject.SetActive(!hide);
            }
            yield break;
        }

        // Fading in: the text object has to be active again before the alpha ramps up, or
        // nothing renders at all while it climbs from 0.
        if (!hide)
        {
            timerText.gameObject.SetActive(true);
        }

        float from = timerTextGroup.alpha;
        float to = hide ? 0f : 1f;
        float t = 0f;

        while (t < numberFadeDuration)
        {
            t += Time.deltaTime;
            timerTextGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / numberFadeDuration));
            yield return null;
        }

        timerTextGroup.alpha = to;

        // Fully faded out: deactivate so it stops needing an Update pass (and stops occupying a
        // layout slot) while hidden.
        if (hide)
        {
            timerText.gameObject.SetActive(false);
        }

        numberFadeRoutine = null;
    }

    // One tick per whole second, tracked by the second the clock is currently showing rather
    // than a timer of our own, so it can never drift out of sync with the number on screen.
    private void TickAudio(float remaining, bool urgent)
    {
        if (!RoundManager.Instance.IsRoundActive)
        {
            _lastTickSecond = -1;
            return;
        }

        int second = Mathf.CeilToInt(remaining);
        if (second == _lastTickSecond || second <= 0 || SFXManager.Instance == null)
        {
            return;
        }

        _lastTickSecond = second;
        SFXManager.Instance.PlayUI(urgent
            ? SFXManager.Instance.shotClockUrgentTickClip
            : SFXManager.Instance.shotClockTickClip);
    }
}
