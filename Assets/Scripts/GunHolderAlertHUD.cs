using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Private "you have the gun" notice for the gun holder only.
//
// The gun is handed off holstered and nobody else is told who has it, so the holder used to get
// nothing but a sound cue - easy to miss mid-walk while the shot clock was already running. This
// shouts it in the middle of the screen with the key that draws the gun, then shrinks into a small
// badge under the shot clock that stays up for as long as they hold it and says whether the gun
// is still hidden or out where everyone can see it.
//
// Same pattern as WeatherAlertHUD: a singleton on a prefab dropped into the game scene once,
// styled in the prefab (Assets/Prefabs/Ui/GunHolderAlertHUD.prefab), driven from here. Local
// only: it reads the replicated playerWithGun and the local player's own Shooting state.
[DisallowMultipleComponent]
public class GunHolderAlertHUD : MonoBehaviour
{
    public static GunHolderAlertHUD Instance { get; private set; }

    [Header("Widget")]
    [SerializeField, Tooltip("The whole alert. Centre-anchored, centred pivot; position, size and scale are driven.")]
    private RectTransform panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI subtitle;

    [Header("Placement (canvas units from the centre)")]
    [SerializeField] private Vector2 announcePosition = new(0f, 250f);
    [SerializeField] private Vector2 announceSize = new(900f, 170f);
    [SerializeField] private float announceTitleSize = 72f;
    [SerializeField] private float announceSubtitleSize = 30f;
    [SerializeField, Tooltip("Just under the shot clock, which sits at the top centre.")]
    private Vector2 dockedPosition = new(0f, 428f);
    [SerializeField] private Vector2 dockedSize = new(480f, 70f);
    [SerializeField] private float dockedTitleSize = 26f;
    [SerializeField] private float dockedSubtitleSize = 18f;

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.2f;
    [SerializeField] private float announceHold = 2.2f;
    [SerializeField] private float dockDuration = 0.6f;
    [SerializeField] private float fadeOut = 0.3f;
    [SerializeField] private float pulseScale = 0.07f;
    [SerializeField] private float pulseFrequency = 4f;

    [Header("Colors")]
    [SerializeField] private Color holsteredColor = new(1f, 0.5568628f, 0.023529412f, 1f);
    [SerializeField] private Color drawnColor = new(1f, 0.25f, 0.2f, 1f);

    [Header("Copy")]
    [SerializeField] private string titleText = "YOU HAVE THE GUN!";
    [SerializeField] private string holsteredHint = "{0} to draw it - nobody else knows";
    [SerializeField] private string drawnHint = "Gun drawn - everyone can see it";
    [SerializeField] private string dockedTitleText = "YOU HAVE THE GUN";

    private bool holding;
    private float stateTime;
    private float alpha;
    private float morph;          // 0 = shouting in the middle, 1 = docked badge
    private Shooting localShooting;
    private InputAction changeWeaponAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (panelGroup != null)
        {
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
        }

        if (panel != null)
        {
            panel.gameObject.SetActive(false);
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
        if (panel == null)
        {
            return;
        }

        bool nowHolding = IsLocalPlayerHolding();
        if (nowHolding && !holding)
        {
            // New hand-off: shout again, even if the badge from an earlier turn was still fading.
            stateTime = 0f;
            morph = 0f;
            alpha = 0f;
            panel.gameObject.SetActive(true);
        }

        holding = nowHolding;
        stateTime += Time.unscaledDeltaTime;

        if (holding)
        {
            alpha = Mathf.MoveTowards(alpha, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeIn));
            float dockStart = fadeIn + announceHold;
            morph = Mathf.Clamp01((stateTime - dockStart) / Mathf.Max(0.01f, dockDuration));
        }
        else
        {
            alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeOut));
            if (alpha <= 0f)
            {
                if (panel.gameObject.activeSelf)
                {
                    panel.gameObject.SetActive(false);
                }

                return;
            }
        }

        Apply();
    }

    private void Apply()
    {
        float eased = morph * morph * (3f - 2f * morph);
        bool drawn = localShooting != null && localShooting.enabled;
        Color color = drawn ? drawnColor : holsteredColor;

        panel.anchoredPosition = Vector2.Lerp(announcePosition, dockedPosition, eased);
        panel.sizeDelta = Vector2.Lerp(announceSize, dockedSize, eased);

        // Pulses while shouting, settles once docked.
        float pulse = 1f + Mathf.Sin(stateTime * pulseFrequency * Mathf.PI * 2f) * pulseScale * (1f - eased);
        panel.localScale = Vector3.one * pulse;

        if (panelGroup != null)
        {
            panelGroup.alpha = alpha;
        }

        if (title != null)
        {
            title.text = eased < 0.5f ? titleText : dockedTitleText;
            title.fontSize = Mathf.Lerp(announceTitleSize, dockedTitleSize, eased);
            title.color = color;
        }

        if (subtitle != null)
        {
            subtitle.fontSize = Mathf.Lerp(announceSubtitleSize, dockedSubtitleSize, eased);
            if (drawn)
            {
                subtitle.text = drawnHint;
            }
            else
            {
                string binding = InteractionPromptHUD.GetBindingLabel(changeWeaponAction);
                subtitle.text = string.Format(holsteredHint, string.IsNullOrEmpty(binding) ? "Change Weapon" : $"[{binding}]");
            }
        }
    }

    private bool IsLocalPlayerHolding()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || GameManager.Instance == null ||
            PlayerSpawner.Instance == null || !PlayerSpawner.Instance.isStarted)
        {
            return false;
        }

        ulong localId = manager.LocalClientId;
        if (GameManager.Instance.playerWithGun.Value != localId || !GameManager.Instance.IsPlayerAlive(localId))
        {
            return false;
        }

        if (localShooting == null)
        {
            NetworkObject player = manager.SpawnManager != null ? manager.SpawnManager.GetLocalPlayerObject() : null;
            if (player == null || !player.TryGetComponent(out localShooting))
            {
                return false;
            }

            if (player.TryGetComponent(out InputSystem input) && input.inputActions != null)
            {
                changeWeaponAction = input.inputActions.FindAction("Change Weapon");
            }
        }

        return true;
    }
}
