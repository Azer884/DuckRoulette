using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The gun readout on the local player's own HUD (Player.prefab/.../GunUI): "0/6" or "1/6" loaded
// state, a prompt that reads "Reload [R]" until the gun is loaded and then "Trigger [RMB]" (or the
// matching controller glyph on a gamepad), and a 6-chamber wheel that spins on every reload with
// the live round's chamber swapping from its empty sprite to its full one.
//
// Lives on the player prefab itself (one instance per player, like InteractionPromptHUD's prompt
// but per-owner rather than a scene singleton) since it only ever has anything to say about this
// player's own gun. Driven by the Shooting component on the same GameObject.
public class GunUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Shooting shooting;

    [Header("Shots readout")]
    [SerializeField] private TextMeshProUGUI shotsText;
    [SerializeField] private string shotsFormat = "Shots: {0}/6";

    [Header("Action prompt")]
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Image promptIcon;
    [SerializeField] private InteractionPromptHUD.ControllerIconSet gamepadIcons;
    [SerializeField] private string reloadLabel = "Reload";
    [SerializeField] private string triggerLabel = "Trigger";

    [Header("Reload wheel")]
    [SerializeField, Tooltip("The wheel that spins on reload. Its own rotation carries the spin - " +
        "the six chamber Images underneath stay at their authored angles.")]
    private RectTransform gunWheel;
    [SerializeField, Tooltip("The chamber that holds the live round - swaps to Full Chamber Sprite " +
        "once reloaded, back to Empty Chamber Sprite once it's fired or a new round starts.")]
    private Image liveChamberEmptySprite;
    [SerializeField] private Image liveChamberFullSprite;
    [SerializeField] private float spinDuration = 0.4f;
    [SerializeField, Tooltip("Full turns the wheel spins through during a reload, on top of " +
        "whatever fraction lands it back at its resting rotation.")]
    private int spinExtraTurns = 2;

    private InputAction reloadAction, triggerAction;
    private float wheelSpinT = -1f;
    private float wheelStartAngle;
    private float wheelTargetAngle;
    private bool wasReloaded;

    private void Awake()
    {
        if (shooting == null)
        {
            shooting = GetComponent<Shooting>();
        }

        InputSystem inputSystem = GetComponent<InputSystem>();
        if (inputSystem != null)
        {
            reloadAction = inputSystem.inputActions.FindAction("Reload");
            triggerAction = inputSystem.inputActions.FindAction("Trigger");
        }

        if (liveChamberFullSprite != null)
        {
            liveChamberFullSprite.gameObject.SetActive(false);
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        if (shooting != null)
        {
            shooting.OnReloaded += HandleReloaded;
        }
    }

    private void OnDisable()
    {
        if (shooting != null)
        {
            shooting.OnReloaded -= HandleReloaded;
        }
    }

    private void HandleReloaded()
    {
        // Spin forward from wherever the wheel currently sits, landing back at its resting angle
        // plus a few extra full turns - so every reload reads as a spin, not a snap, however many
        // reloads have already happened this life.
        wheelStartAngle = gunWheel != null ? gunWheel.localEulerAngles.z : 0f;
        wheelTargetAngle = wheelStartAngle + 360f * spinExtraTurns;
        wheelSpinT = 0f;
    }

    private void Update()
    {
        if (shooting == null || !shooting.enabled || !shooting.HasGun)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        bool reloaded = shooting.IsReloaded;
        ApplyShotsText(reloaded);
        ApplyPrompt(reloaded);
        ApplyLiveChamber(reloaded);
        ApplyWheelSpin();

        wasReloaded = reloaded;
    }

    private void SetVisible(bool visible)
    {
        if (root != null && root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
    }

    private void ApplyShotsText(bool reloaded)
    {
        if (shotsText != null)
        {
            shotsText.text = string.Format(shotsFormat, reloaded ? 1 : 0);
        }
    }

    private void ApplyPrompt(bool reloaded)
    {
        InputAction action = reloaded ? triggerAction : reloadAction;
        string label = reloaded ? triggerLabel : reloadLabel;

        bool isGamepad = InputDeviceTracker.IsGamepad;
        Sprite glyph = null;
        if (isGamepad && action != null)
        {
            string group = InputDeviceTracker.GamepadGroup;
            glyph = gamepadIcons.GetSprite(InputDeviceTracker.ResolveControlName(action, group));
        }

        if (glyph != null && promptIcon != null)
        {
            promptIcon.sprite = glyph;
            promptIcon.gameObject.SetActive(true);
            if (promptText != null)
            {
                promptText.gameObject.SetActive(false);
            }
        }
        else
        {
            if (promptIcon != null)
            {
                promptIcon.gameObject.SetActive(false);
            }

            if (promptText != null)
            {
                promptText.gameObject.SetActive(true);
                string binding = InteractionPromptHUD.GetBindingLabel(action);
                promptText.text = string.IsNullOrEmpty(binding) ? label : $"{label} [{binding}]";
            }
        }
    }

    // Reloaded -> the live chamber shows full. Fired (or never reloaded) -> it shows empty. Both
    // sprites sit at the same authored angle (see the prefab's EmptyChamber/FullChamber pair), so
    // only one is ever visible at a time.
    private void ApplyLiveChamber(bool reloaded)
    {
        if (liveChamberEmptySprite != null)
        {
            liveChamberEmptySprite.gameObject.SetActive(!reloaded);
        }
        if (liveChamberFullSprite != null)
        {
            liveChamberFullSprite.gameObject.SetActive(reloaded);
        }
    }

    private void ApplyWheelSpin()
    {
        if (gunWheel == null || wheelSpinT < 0f)
        {
            return;
        }

        wheelSpinT += Time.deltaTime / Mathf.Max(0.01f, spinDuration);
        if (wheelSpinT >= 1f)
        {
            gunWheel.localEulerAngles = new Vector3(0f, 0f, wheelTargetAngle);
            wheelSpinT = -1f;
            return;
        }

        float eased = EaseOut(wheelSpinT);
        float angle = Mathf.LerpUnclamped(wheelStartAngle, wheelTargetAngle, eased);
        gunWheel.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private static float EaseOut(float k)
    {
        float inv = 1f - k;
        return 1f - inv * inv * inv;
    }
}
