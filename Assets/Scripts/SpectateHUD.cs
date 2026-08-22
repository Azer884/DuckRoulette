using System.Collections;
using TMPro;
using UnityEngine;

// Presentation layer for spectate mode, driven by DeathTrigger. Previously spectating gave a
// player zero on-screen information: no idea who killed them, who they're currently watching,
// or that a "next target" control even exists.
//
// All visuals live on Assets/PreFabs/Ui/SpectateHUD.prefab - drop that prefab into the game
// scene once (same as MessageBox) and edit colors/layout/fonts there like any other UI. This
// script only drives it, via the static methods DeathTrigger calls.
public class SpectateHUD : MonoBehaviour
{
    public static SpectateHUD Instance { get; private set; }

    [SerializeField] private CanvasGroup deathBannerGroup;
    [SerializeField] private TextMeshProUGUI deathBannerText;

    [SerializeField] private GameObject spectateInfoRoot;
    [SerializeField] private TextMeshProUGUI spectatingNameText;
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("Death banner timing")]
    [SerializeField] private float fadeIn = 0.25f;
    [SerializeField] private float hold = 1.75f;
    [SerializeField] private float fadeOut = 0.75f;

    private Coroutine _deathBannerRoutine;

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

        if (deathBannerGroup != null)
        {
            deathBannerGroup.alpha = 0f;
            deathBannerGroup.gameObject.SetActive(false);
        }
        if (spectateInfoRoot != null)
        {
            spectateInfoRoot.SetActive(false);
        }
    }

    public static void ShowDeathBanner(string killerName)
    {
        if (Instance == null)
        {
            return;
        }
        Instance.ShowDeathBannerInternal(killerName);
    }

    private void ShowDeathBannerInternal(string killerName)
    {
        if (deathBannerText == null || deathBannerGroup == null)
        {
            return;
        }

        deathBannerText.text = string.IsNullOrEmpty(killerName)
            ? "YOU DIED"
            : $"ELIMINATED BY {killerName.ToUpperInvariant()}";

        deathBannerGroup.gameObject.SetActive(true);
        if (_deathBannerRoutine != null)
        {
            StopCoroutine(_deathBannerRoutine);
        }
        _deathBannerRoutine = StartCoroutine(FadeDeathBanner());
    }

    private IEnumerator FadeDeathBanner()
    {
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            deathBannerGroup.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        deathBannerGroup.alpha = 1f;

        yield return new WaitForSeconds(hold);

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            deathBannerGroup.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        deathBannerGroup.alpha = 0f;
        deathBannerGroup.gameObject.SetActive(false);
    }

    public static void ShowSpectating(string targetName, string hint)
    {
        if (Instance == null)
        {
            return;
        }

        if (Instance.spectateInfoRoot != null)
        {
            Instance.spectateInfoRoot.SetActive(true);
        }
        if (Instance.spectatingNameText != null)
        {
            Instance.spectatingNameText.text = string.IsNullOrEmpty(targetName) ? "SPECTATING" : $"SPECTATING {targetName.ToUpperInvariant()}";
        }
        if (Instance.hintText != null)
        {
            Instance.hintText.text = hint;
        }
    }

    public static void HideSpectating()
    {
        if (Instance == null || Instance.spectateInfoRoot == null)
        {
            return;
        }
        Instance.spectateInfoRoot.SetActive(false);
    }
}
