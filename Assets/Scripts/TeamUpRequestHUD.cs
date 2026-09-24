using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Left-side alert for an incoming team-up request: "Press [E] to team up with Name", with a bar
// that drains over the time left to answer. Driven by TeamUp on the responder's client.
//
// Same static-singleton pattern as InteractionPromptHUD: drop Assets/Prefabs/Ui/TeamUpRequestHUD.prefab
// into the game scene once and style it there.
public class TeamUpRequestHUD : MonoBehaviour
{
    public static TeamUpRequestHUD Instance { get; private set; }

    [SerializeField] private GameObject root;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField, Tooltip("Optional second line naming the reject key.")]
    private TextMeshProUGUI hintText;
    [SerializeField, Tooltip("Filled image that drains as the request runs out.")]
    private Image timerFill;

    [SerializeField] private string messageFormat = "Press {0} to team up with <b>{1}</b>";
    [SerializeField] private string hintFormat = "{0} to decline";
    [SerializeField] private Color timerFullColor = new(0.45f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color timerEmptyColor = new(0.95f, 0.3f, 0.25f, 1f);

    [Header("Animation")]
    [SerializeField] private float slideDistance = 60f;
    [SerializeField] private float slideDuration = 0.2f;

    private float duration;
    private float remaining;
    private float slideT;
    private RectTransform rootRect;
    private CanvasGroup group;
    private Vector2 basePosition;

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

        if (root != null)
        {
            rootRect = root.transform as RectTransform;
            if (rootRect != null)
            {
                basePosition = rootRect.anchoredPosition;
            }

            group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.AddComponent<CanvasGroup>();
            }

            group.interactable = false;
            group.blocksRaycasts = false;
            root.SetActive(false);
        }
    }

    public static void Show(string playerName, InputAction acceptAction, InputAction rejectAction, float seconds)
    {
        if (Instance == null || Instance.root == null)
        {
            return;
        }

        Instance.Open(playerName, acceptAction, rejectAction, seconds);
    }

    public static void Hide()
    {
        if (Instance == null || Instance.root == null)
        {
            return;
        }

        Instance.root.SetActive(false);
    }

    private void Open(string playerName, InputAction acceptAction, InputAction rejectAction, float seconds)
    {
        duration = Mathf.Max(0.01f, seconds);
        remaining = duration;
        slideT = 0f;

        string accept = InteractionPromptHUD.GetBindingLabel(acceptAction);
        accept = string.IsNullOrEmpty(accept) ? "Interact" : $"[{accept}]";

        if (messageText != null)
        {
            messageText.text = string.Format(messageFormat, accept, playerName);
        }

        if (hintText != null)
        {
            string reject = InteractionPromptHUD.GetBindingLabel(rejectAction);
            hintText.text = string.IsNullOrEmpty(reject) ? string.Empty : string.Format(hintFormat, $"[{reject}]");
        }

        root.SetActive(true);
        Apply();
    }

    // Unscaled time: the request is on a real-time clock on the server, pausing does not stop it.
    private void Update()
    {
        if (root == null || !root.activeSelf)
        {
            return;
        }

        remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
        slideT = Mathf.Min(1f, slideT + Time.unscaledDeltaTime / Mathf.Max(0.01f, slideDuration));
        Apply();
    }

    private void Apply()
    {
        float fraction = remaining / duration;

        if (timerFill != null)
        {
            timerFill.fillAmount = fraction;
            timerFill.color = Color.Lerp(timerEmptyColor, timerFullColor, fraction);
        }

        float eased = 1f - (1f - slideT) * (1f - slideT) * (1f - slideT);
        if (group != null)
        {
            group.alpha = eased;
        }

        if (rootRect != null)
        {
            rootRect.anchoredPosition = basePosition + new Vector2(-slideDistance * (1f - eased), 0f);
        }
    }
}
