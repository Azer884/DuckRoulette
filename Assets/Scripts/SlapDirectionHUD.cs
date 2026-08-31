using System.Collections;
using UnityEngine;

// Local-only "you got slapped from over there" compass arrow, shown to whichever player just
// got hit. Same static-singleton/CanvasGroup-fade pattern as ParryFeedbackHUD/SpectateHUD: drop
// Assets/Prefabs/Ui/SlapDirectionHUD.prefab into the game scene once, style it there - assign
// the arrow/indicator sprite on arrowImage yourself, this script only drives rotation/fade.
public class SlapDirectionHUD : MonoBehaviour
{
    public static SlapDirectionHUD Instance { get; private set; }

    [SerializeField] private CanvasGroup indicatorGroup;
    [SerializeField] private RectTransform arrowRect;

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.05f;
    [SerializeField] private float hold = 0.6f;
    [SerializeField] private float fadeOut = 0.4f;

    private Transform mainCameraTransform;
    private Coroutine indicatorRoutine;
    private Vector3 trackedWorldPosition;
    private bool isTracking;

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

        if (indicatorGroup != null)
        {
            indicatorGroup.alpha = 0f;
            indicatorGroup.gameObject.SetActive(false);
        }
    }

    /// <summary>Points the indicator toward `attackerWorldPosition` (fixed in world space - it
    /// keeps re-aiming at this same spot every frame as the local player turns/moves) and fades
    /// it in/out.</summary>
    public static void Show(Vector3 attackerWorldPosition)
    {
        if (Instance == null)
        {
            return;
        }
        Instance.ShowInternal(attackerWorldPosition);
    }

    private void ShowInternal(Vector3 attackerWorldPosition)
    {
        if (indicatorGroup == null || arrowRect == null)
        {
            return;
        }

        trackedWorldPosition = attackerWorldPosition;
        isTracking = true;
        UpdateArrowRotation();

        indicatorGroup.gameObject.SetActive(true);

        if (indicatorRoutine != null)
        {
            StopCoroutine(indicatorRoutine);
        }
        indicatorRoutine = StartCoroutine(PlayIndicator());
    }

    // Re-aims every frame instead of once at trigger time - the attacker's spot is fixed in
    // world space, but the local player keeps moving/turning underneath it for the whole time
    // the indicator is shown, so the on-screen angle has to keep being recomputed to match.
    private void Update()
    {
        if (isTracking)
        {
            UpdateArrowRotation();
        }
    }

    private void UpdateArrowRotation()
    {
        if (mainCameraTransform == null)
        {
            mainCameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (mainCameraTransform == null)
            {
                return;
            }
        }

        Vector3 toAttacker = trackedWorldPosition - mainCameraTransform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 camForward = mainCameraTransform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        // Signed angle around world up so "left of screen" and "right of screen" come out with
        // the correct sign regardless of which way the camera itself is currently facing.
        float angle = Vector3.SignedAngle(camForward, toAttacker.normalized, Vector3.up);
        arrowRect.localEulerAngles = new Vector3(0f, 0f, -angle);
    }

    private IEnumerator PlayIndicator()
    {
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            indicatorGroup.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        indicatorGroup.alpha = 1f;

        yield return new WaitForSeconds(hold);

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            indicatorGroup.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        indicatorGroup.alpha = 0f;
        indicatorGroup.gameObject.SetActive(false);
        isTracking = false;
    }
}
