using System.Collections;
using Steamworks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Right-side alert for an incoming team-up request: profile picture, "Accept with [E]" / "Decline
// with [X]" prompts (text or a controller glyph, matching InteractionPromptHUD's device logic),
// and a ring timer that counts the time left to answer. Driven by TeamUp on the responder's client.
//
// Same static-singleton pattern as InteractionPromptHUD: drop Assets/Prefabs/Ui/TeamUpRequestHUD.prefab
// into the game scene once and style it there.
public class TeamUpRequestHUD : MonoBehaviour
{
    public static TeamUpRequestHUD Instance { get; private set; }

    [SerializeField] private GameObject root;

    [Header("Profile picture")]
    [SerializeField] private RawImage profilePic;
    [SerializeField, Tooltip("Shown behind/instead of the profile pic until the avatar download completes.")]
    private Texture2D placeholderAvatar;

    [Header("Prompts")]
    [SerializeField] private TextMeshProUGUI acceptText;
    [SerializeField] private Image acceptIcon;
    [SerializeField] private TextMeshProUGUI declineText;
    [SerializeField] private Image declineIcon;
    [SerializeField] private InteractionPromptHUD.ControllerIconSet gamepadIcons;
    [SerializeField] private string acceptFormat = "[{0}] Accept";
    [SerializeField] private string declineFormat = "[{0}] Decline";

    [Header("Timer ring")]
    [SerializeField, Tooltip("Filled image (Radial 360) that drains as the request runs out.")]
    private Image timerFill;
    [SerializeField, Tooltip("The ring's background/track - scaled along with the fill so the " +
        "whole ring pulses as one piece instead of just the wedge inside it.")]
    private Image timerTrack;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Color timerNormalColor = new(0.95f, 0.95f, 0.95f, 0.95f);
    [SerializeField] private Color timerUrgentColor = new(0.95f, 0.3f, 0.25f, 1f);
    [SerializeField, Range(0.05f, 0.5f), Tooltip("Fraction of the total time left at which the ring starts flashing red and pulsing.")]
    private float urgentFraction = 0.3f;
    [SerializeField] private float urgentPulseScale = 0.14f;
    [SerializeField, Tooltip("Lower = slower pulse/scale.")]
    private float urgentPulseFrequency = 2.2f;
    [SerializeField, Tooltip("Lower = slower colour flash.")]
    private float urgentFlashFrequency = 2.6f;

    [Header("Animation - entrance")]
    [SerializeField, Tooltip("How far right each element starts from its authored position.")]
    private float slideDistance = 80f;
    [SerializeField] private float slideDuration = 0.22f;
    [SerializeField, Tooltip("Delay between the pdp, the buttons and the timer sliding in.")]
    private float elementStagger = 0.08f;

    private float duration;
    private float remaining;
    private RectTransform pdpRect, buttonsRect, timerRect, timerTrackRect;
    private CanvasGroup pdpGroup, buttonsGroup, timerGroup;
    private Vector2 pdpBase, buttonsBase, timerBase;
    private Coroutine introRoutine;
    private uint avatarRequestToken;

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

        if (profilePic != null)
        {
            pdpRect = profilePic.rectTransform;
            pdpGroup = GetOrAddGroup(profilePic.gameObject);
            pdpBase = pdpRect.anchoredPosition;
        }

        if (acceptText != null)
        {
            // Accept/decline text+icon share one parent rect, so they slide/fade together.
            buttonsRect = acceptText.transform.parent as RectTransform;
            if (buttonsRect != null)
            {
                buttonsGroup = GetOrAddGroup(buttonsRect.gameObject);
                buttonsBase = buttonsRect.anchoredPosition;
            }
        }

        if (timerFill != null)
        {
            timerRect = timerFill.rectTransform;
            timerGroup = GetOrAddGroup(timerFill.gameObject);
            timerBase = timerRect.anchoredPosition;

            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Radial360;
        }

        if (timerTrack != null)
        {
            timerTrackRect = timerTrack.rectTransform;
        }

        if (root != null)
        {
            root.SetActive(false);
        }
    }

    public static void Show(string playerName, ulong requesterClientId, InputAction acceptAction, InputAction rejectAction, float seconds)
    {
        if (Instance == null || Instance.root == null)
        {
            return;
        }

        Instance.Open(requesterClientId, acceptAction, rejectAction, seconds);
    }

    public static void Hide()
    {
        if (Instance == null || Instance.root == null)
        {
            return;
        }

        if (Instance.introRoutine != null)
        {
            Instance.StopCoroutine(Instance.introRoutine);
            Instance.introRoutine = null;
        }

        Instance.root.SetActive(false);
    }

    private void Open(ulong requesterClientId, InputAction acceptAction, InputAction rejectAction, float seconds)
    {
        duration = Mathf.Max(0.01f, seconds);
        remaining = duration;

        ApplyPrompt(acceptText, acceptIcon, acceptAction, acceptFormat);
        ApplyPrompt(declineText, declineIcon, rejectAction, declineFormat);

        if (profilePic != null)
        {
            profilePic.texture = placeholderAvatar;
        }
        LoadAvatar(requesterClientId);

        root.SetActive(true);
        ApplyTimer();

        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
        }
        introRoutine = StartCoroutine(PlayIntro());
    }

    private void ApplyPrompt(TextMeshProUGUI label, Image icon, InputAction action, string format)
    {
        bool isGamepad = InputDeviceTracker.IsGamepad;
        Sprite glyph = null;

        if (isGamepad && action != null)
        {
            string group = InputDeviceTracker.GamepadGroup;
            glyph = gamepadIcons.GetSprite(InputDeviceTracker.ResolveControlName(action, group));
        }

        if (glyph != null && icon != null)
        {
            icon.sprite = glyph;
            icon.gameObject.SetActive(true);
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }
        }
        else
        {
            if (icon != null)
            {
                icon.gameObject.SetActive(false);
            }

            if (label != null)
            {
                label.gameObject.SetActive(true);
                string binding = InteractionPromptHUD.GetBindingLabel(action);
                label.text = string.IsNullOrEmpty(binding) ? string.Empty : string.Format(format, binding);
            }
        }
    }

    // The requester's Steam avatar: Username.steamId replicates across the whole match (see
    // Username.cs), so this works from GameScene without any lobby-only lookup.
    private void LoadAvatar(ulong requesterClientId)
    {
        avatarRequestToken++;
        uint token = avatarRequestToken;

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(requesterClientId, out var client) ||
            client.PlayerObject == null ||
            !client.PlayerObject.TryGetComponent(out Username username))
        {
            return;
        }

        ulong steamId = username.steamId.Value;
        if (steamId == 0)
        {
            return;
        }

        FetchAvatarAsync(steamId, token);
    }

    private async void FetchAvatarAsync(ulong steamId, uint token)
    {
        var image = await SteamFriends.GetLargeAvatarAsync(steamId);

        // The request might have closed (or a newer one started) while the avatar was in flight.
        if (token != avatarRequestToken || profilePic == null || !image.HasValue)
        {
            return;
        }

        profilePic.texture = SteamFriendsManager.GetTextureFromImage(image.Value);
    }

    // Unscaled time: the request is on a real-time clock on the server, pausing does not stop it.
    private void Update()
    {
        if (root == null || !root.activeSelf)
        {
            return;
        }

        remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
        ApplyTimer();
    }

    private void ApplyTimer()
    {
        float fraction = remaining / duration;
        bool urgent = fraction <= urgentFraction;

        if (timerFill != null)
        {
            timerFill.fillAmount = fraction;
        }

        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(remaining).ToString();
        }

        if (!urgent)
        {
            if (timerFill != null)
            {
                timerFill.color = timerNormalColor;
            }
            if (timerText != null)
            {
                timerText.color = timerNormalColor;
            }
            if (timerRect != null)
            {
                timerRect.localScale = Vector3.one;
            }
            if (timerTrackRect != null)
            {
                timerTrackRect.localScale = Vector3.one;
            }
            return;
        }

        // Last stretch: flash red and pulse the scale up slightly, so the ring itself (not just
        // a number) demands a glance.
        float flash = Mathf.Abs(Mathf.Sin(Time.unscaledTime * urgentFlashFrequency * Mathf.PI));
        Color flashColor = Color.Lerp(timerNormalColor, timerUrgentColor, flash);

        if (timerFill != null)
        {
            timerFill.color = flashColor;
        }
        if (timerText != null)
        {
            timerText.color = flashColor;
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * urgentPulseFrequency * Mathf.PI * 2f) * urgentPulseScale;

        if (timerRect != null)
        {
            timerRect.localScale = Vector3.one * pulse;
        }
        if (timerTrackRect != null)
        {
            timerTrackRect.localScale = Vector3.one * pulse;
        }
    }

    // The pdp, the buttons and the timer slide in from the right one after another, rather than
    // all together, so the alert reads as three things arriving rather than one box popping in.
    private IEnumerator PlayIntro()
    {
        Coroutine pdpRoutine = pdpRect != null ? StartCoroutine(SlideIn(pdpRect, pdpGroup, pdpBase)) : null;
        yield return new WaitForSecondsRealtime(elementStagger);

        Coroutine buttonsRoutine = buttonsRect != null ? StartCoroutine(SlideIn(buttonsRect, buttonsGroup, buttonsBase)) : null;
        yield return new WaitForSecondsRealtime(elementStagger);

        if (timerRect != null)
        {
            StartCoroutine(SlideIn(timerRect, timerGroup, timerBase));
        }

        introRoutine = null;
    }

    private IEnumerator SlideIn(RectTransform rect, CanvasGroup group, Vector2 basePosition)
    {
        Vector2 from = basePosition + new Vector2(slideDistance, 0f);
        float t = 0f;

        if (group != null) group.alpha = 0f;
        rect.anchoredPosition = from;

        while (t < slideDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = EaseOut(Mathf.Clamp01(t / slideDuration));
            if (group != null) group.alpha = k;
            rect.anchoredPosition = Vector2.LerpUnclamped(from, basePosition, k);
            yield return null;
        }

        if (group != null) group.alpha = 1f;
        rect.anchoredPosition = basePosition;
    }

    private static CanvasGroup GetOrAddGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = target.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        return group;
    }

    private static float EaseOut(float k)
    {
        float inv = 1f - k;
        return 1f - inv * inv * inv;
    }
}
