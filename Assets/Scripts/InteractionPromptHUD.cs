using TMPro;
using UnityEngine;

// Bottom-of-screen interaction prompt, driven by Interact whenever its raycast is hovering a
// usable IInteractable. Same static-singleton pattern as SpectateHUD: drop
// Assets/PreFabs/Ui/InteractionPromptHUD.prefab into the game scene once, style it there.
public class InteractionPromptHUD : MonoBehaviour
{
    public static InteractionPromptHUD Instance { get; private set; }

    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshProUGUI promptText;

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

    public static void Show(string actionLabel, string bindingDisplayString)
    {
        if (Instance == null || Instance.promptRoot == null || Instance.promptText == null)
        {
            return;
        }

        Instance.promptRoot.SetActive(true);
        Instance.promptText.text = string.IsNullOrEmpty(bindingDisplayString)
            ? actionLabel
            : $"[{bindingDisplayString}] {actionLabel}";
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
}
