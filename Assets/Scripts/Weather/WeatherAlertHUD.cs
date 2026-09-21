using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Weather
{
    /// <summary>
    /// Player-facing notice that the weather has turned, in one widget that changes shape rather
    /// than two that appear and disappear.
    ///
    /// It announces itself big and red in the middle of the screen, shaking, then slides up into
    /// the top-right corner and shrinks into a small label over a bar that drains as the event runs
    /// out. Nothing is hidden and re-shown between those states - position, size, font size,
    /// colour and each part's alpha are all lerped on one 0..1 morph value, so the alarm becomes
    /// the timer instead of being replaced by it.
    ///
    /// Once docked it is only a bar - no number and no label. It sits directly to the right of the
    /// gun's shot clock so the two read as one row of status, and "this is running out" needs no
    /// reading at all.
    ///
    /// Same pattern as ParryFeedbackHUD and SpectateHUD: a singleton on a prefab dropped into the
    /// game scene, styled in the prefab, driven from here. Local only - it reads the already
    /// replicated phase and remaining time, so there is nothing to send.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherAlertHUD : MonoBehaviour
    {
        public static WeatherAlertHUD Instance { get; private set; }

        private enum State
        {
            Hidden,
            Announcing,
            Docking,
            Docked,
            FadingOut,
        }

        [Header("Widget")]
        [SerializeField, Tooltip("The whole alert. Its anchored position, size and scale are " +
            "driven; it must be centre-anchored with a centred pivot.")]
        private RectTransform panel;

        [SerializeField] private CanvasGroup panelGroup;

        [SerializeField, Tooltip("The headline: STORM / RAIN. Shrinks into the bar's label.")]
        private TextMeshProUGUI title;

        [SerializeField, Tooltip("The explanation line. Faded out as the widget docks.")]
        private TextMeshProUGUI subtitle;

        [SerializeField, Tooltip("The bar's container. Faded in as the widget docks.")]
        private CanvasGroup barGroup;

        [SerializeField, Tooltip("Horizontal filled image that drains as the event runs out.")]
        private Image barFill;

        [Header("Announce placement")]
        [SerializeField, Tooltip("Where the alert shouts from, in canvas units from the canvas centre.")]
        private Vector2 announcePosition = new(0f, 180f);

        [SerializeField] private Vector2 announceSize = new(900f, 170f);
        [SerializeField] private float announceTitleFontSize = 80f;

        [Header("Docked placement")]
        [SerializeField, Tooltip("Gap in canvas units between the shot clock's right edge and the bar.")]
        private float dockGap = 16f;

        [SerializeField, Tooltip("The docked bar. Its height is the bar itself, not a panel.")]
        private Vector2 dockedSize = new(150f, 16f);

        [SerializeField, Tooltip("Where the bar goes when the shot clock is not on screen, " +
            "measured from the canvas centre.")]
        private Vector2 fallbackDockPosition = new(240f, 470f);

        [SerializeField, Range(0.05f, 1f), Tooltip("How much of the slide the headline survives. " +
            "It is faded out well before the bar arrives, so nothing is written beside the gun timer.")]
        private float titleFadeOutMorph = 0.55f;

        [Header("Timing")]
        [SerializeField, Tooltip("Seconds the alert shouts in the middle before it starts moving.")]
        private float announceHold = 2.4f;

        [SerializeField] private float fadeIn = 0.22f;

        [SerializeField, Tooltip("Seconds the slide-and-shrink takes.")]
        private float dockDuration = 0.9f;

        [SerializeField] private float fadeOut = 0.5f;

        [Header("Alarm")]
        [SerializeField, Tooltip("Alarm red for the headline while it is shouting.")]
        private Color alarmColor = new(1f, 0.17f, 0.15f, 1f);

        [SerializeField, Tooltip("Colour the label and bar settle into once docked, so a badge " +
            "that stays up for the whole event is not screaming the entire time.")]
        private Color dockedColor = new(1f, 0.32f, 0.26f, 1f);

        [SerializeField, Tooltip("The bar flashes to this in the last few seconds.")]
        private Color expiringColor = new(1f, 0.85f, 0.25f, 1f);

        [SerializeField] private float expiringThreshold = 5f;

        [SerializeField, Tooltip("Pixels of shake at the peak of the announcement.")]
        private float shakeAmplitude = 10f;

        [SerializeField] private float shakeFrequency = 26f;

        [SerializeField, Tooltip("How far the headline pulses in scale while shouting.")]
        private float pulseScale = 0.09f;

        [SerializeField] private float pulseFrequency = 4.5f;

        [Header("Copy")]
        [SerializeField] private string stormTitle = "STORM";
        [SerializeField] private string stormSubtitle = "Hail incoming - keep your head down";

        private State state = State.Hidden;
        private float stateTime;
        private float morph;          // 0 = shouting in the middle, 1 = docked drain bar
        private float alpha;
        private RectTransform canvasRect;
        private readonly Vector3[] clockCorners = new Vector3[4];

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            Canvas canvas = GetComponentInParent<Canvas>();
            canvasRect = canvas != null ? canvas.transform as RectTransform : null;

            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        private void OnEnable() => WeatherSystem.PhaseChanged += OnPhaseChanged;

        private void OnDisable() => WeatherSystem.PhaseChanged -= OnPhaseChanged;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnPhaseChanged(WeatherPhase phase)
        {
            // Only a storm is worth an alarm. A shower is ambience; it is shown by the rain
            // itself, not announced. Rain after a storm counts as the storm ending.
            if (phase != WeatherPhase.Storm)
            {
                // Nothing to announce on the first Clear of a match; only wind down if we were up.
                if (state != State.Hidden)
                {
                    EnterState(State.FadingOut);
                }

                return;
            }

            if (title != null)
            {
                title.text = stormTitle;
            }

            if (subtitle != null)
            {
                subtitle.text = stormSubtitle;
            }

            // A rain-to-storm escalation is worth shouting about again, so restart the whole
            // sequence rather than quietly relabelling the docked bar.
            EnterState(State.Announcing);
        }

        private void EnterState(State next)
        {
            state = next;
            stateTime = 0f;

            if (next == State.Announcing)
            {
                morph = 0f;
                if (panel != null)
                {
                    panel.gameObject.SetActive(true);
                }
            }
        }

        private void LateUpdate()
        {
            if (panel == null || state == State.Hidden)
            {
                return;
            }

            stateTime += Time.deltaTime;

            switch (state)
            {
                case State.Announcing:
                    alpha = fadeIn <= 0f ? 1f : Mathf.Min(1f, stateTime / fadeIn);
                    morph = 0f;
                    if (stateTime >= announceHold)
                    {
                        EnterState(State.Docking);
                    }

                    break;

                case State.Docking:
                    alpha = 1f;
                    // Smoothstep rather than linear: the widget eases out of the middle and settles
                    // into the corner instead of arriving at full speed.
                    morph = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stateTime / Mathf.Max(0.01f, dockDuration)));
                    if (morph >= 1f)
                    {
                        EnterState(State.Docked);
                    }

                    break;

                case State.Docked:
                    alpha = 1f;
                    morph = 1f;
                    break;

                case State.FadingOut:
                    morph = 1f;
                    alpha = fadeOut <= 0f ? 0f : 1f - Mathf.Clamp01(stateTime / fadeOut);
                    if (alpha <= 0f)
                    {
                        EnterState(State.Hidden);
                        panel.gameObject.SetActive(false);
                        return;
                    }

                    break;
            }

            ApplyLayout();
            ApplyContent();
        }

        private void ApplyLayout()
        {
            Vector2 position = Vector2.Lerp(announcePosition, GetDockPosition(), morph);
            Vector2 size = Vector2.Lerp(announceSize, dockedSize, morph);

            // The shake and the pulse only exist while it is shouting, and both fade out with the
            // morph so the docked bar sits perfectly still.
            float alarm = 1f - morph;
            if (alarm > 0.001f)
            {
                float t = Time.unscaledTime * shakeFrequency;
                position += new Vector2(
                    (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f) * (shakeAmplitude * alarm);
            }

            panel.anchoredPosition = position;
            panel.sizeDelta = size;

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI * 2f) * pulseScale * alarm;
            panel.localScale = Vector3.one * pulse;

            if (panelGroup != null)
            {
                panelGroup.alpha = alpha;
            }
        }

        private void ApplyContent()
        {
            WeatherSystem weather = WeatherSystem.Instance;
            float remaining = weather != null ? weather.PhaseSecondsRemaining : 0f;
            float total = weather != null ? weather.PhaseTotalSeconds : 0f;
            bool expiring = remaining > 0f && remaining <= expiringThreshold;

            if (title != null)
            {
                // The headline belongs to the announcement only. Once the widget is on its way to
                // the shot clock it fades out entirely, so what lands beside the gun timer is a
                // bar and nothing else.
                float titleAlpha = Mathf.Clamp01(1f - morph / Mathf.Max(0.01f, titleFadeOutMorph));

                // The red itself throbs - a flat colour reads as a label, a throbbing one reads
                // as an alarm. The fade goes INTO this colour's alpha: setting `color` after
                // `alpha` overwrote the fade with a=1, which is why STORM stayed written next to
                // the bar.
                float throb = 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI));
                Color titleColor = alarmColor * throb;
                titleColor.a = titleAlpha;
                title.color = titleColor;

                bool showTitle = titleAlpha > 0.001f;
                if (title.enabled != showTitle)
                {
                    title.enabled = showTitle;
                }
            }

            if (subtitle != null)
            {
                // Gone well before the slide finishes; only the bar survives it.
                float subtitleAlpha = Mathf.Clamp01(1f - morph * 2.2f);
                subtitle.alpha = subtitleAlpha;
                bool showSubtitle = subtitleAlpha > 0.001f;
                if (subtitle.enabled != showSubtitle)
                {
                    subtitle.enabled = showSubtitle;
                }
            }

            if (barGroup != null)
            {
                // Comes in as the headline goes out, so there is never a frame with neither.
                barGroup.alpha = Mathf.Clamp01((morph - 0.35f) / 0.4f);
            }

            if (barFill != null)
            {
                barFill.fillAmount = total > 0.01f ? Mathf.Clamp01(remaining / total) : 0f;

                Color barColor = dockedColor;
                if (expiring)
                {
                    // Flash rather than just change colour, so the last seconds are noticed even
                    // out of the corner of the eye.
                    float flash = Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 3f));
                    barColor = Color.Lerp(dockedColor, expiringColor, flash);
                }

                barColor.a = 1f;
                barFill.color = barColor;
            }

            // The event is over: wind down rather than waiting for a Clear that a despawn might
            // never deliver.
            if (state == State.Docked && weather != null && weather.Phase == WeatherPhase.Clear)
            {
                EnterState(State.FadingOut);
            }
        }

        // Read off the live shot clock rect every frame rather than hard-coded, so the bar stays
        // glued to the clock's right edge through the clock's own urgent pulse and at any
        // resolution or canvas scale.
        private Vector2 GetDockPosition()
        {
            ShotClockUI clock = ShotClockUI.Instance;
            if (canvasRect == null || clock == null || !clock.IsShowing || clock.Widget == null)
            {
                return fallbackDockPosition;
            }

            clock.Widget.GetWorldCorners(clockCorners);
            // 2 = top-right, 3 = bottom-right.
            Vector3 rightEdge = (clockCorners[2] + clockCorners[3]) * 0.5f;

            // Both canvases are Screen Space - Overlay, so a null camera is the correct argument
            // for both conversions.
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, rightEdge);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local))
            {
                return fallbackDockPosition;
            }

            // Centred pivot, so push out by the gap plus half the bar's width.
            return local + new Vector2(dockGap + dockedSize.x * 0.5f, 0f);
        }
    }
}
