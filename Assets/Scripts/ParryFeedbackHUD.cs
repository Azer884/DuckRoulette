using System.Collections;
using TMPro;
using UnityEngine;

// Local-only "+PARRY" success popup, shown to whichever player performed the ground parry.
// Same static-singleton/CanvasGroup-fade pattern as SpectateHUD: drop
// Assets/Prefabs/Ui/ParryFeedbackHUD.prefab into the game scene once, style it there.
public class ParryFeedbackHUD : MonoBehaviour
{
    public static ParryFeedbackHUD Instance { get; private set; }

    [SerializeField] private CanvasGroup bannerGroup;
    [SerializeField] private TextMeshProUGUI bannerText;

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.05f;
    [SerializeField] private float hold = 0.5f;
    [SerializeField] private float fadeOut = 0.35f;

    [Header("Punch scale")]
    [SerializeField] private float punchScale = 1.25f;
    [SerializeField] private float punchDuration = 0.15f;

    private Coroutine bannerRoutine;

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

        if (bannerGroup != null)
        {
            bannerGroup.alpha = 0f;
            bannerGroup.gameObject.SetActive(false);
        }
    }

    public static void Show(string label = "+PARRY")
    {
        if (Instance == null)
        {
            return;
        }
        Instance.ShowInternal(label);
    }

    private void ShowInternal(string label)
    {
        if (bannerGroup == null || bannerText == null)
        {
            return;
        }

        bannerText.text = label;
        bannerGroup.gameObject.SetActive(true);

        if (bannerRoutine != null)
        {
            StopCoroutine(bannerRoutine);
        }
        bannerRoutine = StartCoroutine(PlayBanner());
    }

    private IEnumerator PlayBanner()
    {
        Vector3 baseScale = Vector3.one;
        bannerText.rectTransform.localScale = baseScale * punchScale;

        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            bannerGroup.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        bannerGroup.alpha = 1f;

        float punchElapsed = 0f;
        while (punchElapsed < punchDuration)
        {
            punchElapsed += Time.deltaTime;
            bannerText.rectTransform.localScale = Vector3.Lerp(baseScale * punchScale, baseScale, punchElapsed / punchDuration);
            yield return null;
        }
        bannerText.rectTransform.localScale = baseScale;

        yield return new WaitForSeconds(hold);

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            bannerGroup.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        bannerGroup.alpha = 0f;
        bannerGroup.gameObject.SetActive(false);
    }
}
