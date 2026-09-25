using UnityEngine;
using TMPro;

// Small FPS / ping readout in the top-right corner of the match HUD. Each one is shown only while
// its toggle is on in Settings > Game (Show FPS / Show Ping), both off by default.
public class PingManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI pingText;
    [SerializeField] private TextMeshProUGUI fpsText;

    [Header("Visibility")]
    [SerializeField, Tooltip("Hidden while Show Ping is off. Defaults to the ping text's object.")]
    private GameObject pingRoot;
    [SerializeField, Tooltip("Hidden while Show FPS is off. Defaults to the FPS text's object.")]
    private GameObject fpsRoot;

    [Header("Ping colors")]
    [SerializeField] private Color goodColor = new(0.45f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color okColor = new(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color badColor = new(0.95f, 0.35f, 0.3f, 1f);
    [SerializeField] private float okPingMs = 80f;
    [SerializeField] private float badPingMs = 150f;

    private int framesThisInterval;
    private float intervalTime;

    private GameObject PingRoot => pingRoot != null ? pingRoot : pingText != null ? pingText.gameObject : null;
    private GameObject FpsRoot => fpsRoot != null ? fpsRoot : fpsText != null ? fpsText.gameObject : null;

    private void OnEnable()
    {
        GameplaySettings.Changed += ApplyVisibility;
        ApplyVisibility();
        InvokeRepeating(nameof(UpdatePing), 0.5f, 1f);
    }

    private void OnDisable()
    {
        GameplaySettings.Changed -= ApplyVisibility;
        CancelInvoke();
    }

    private void ApplyVisibility()
    {
        if (PingRoot != null) PingRoot.SetActive(GameplaySettings.ShowPing);
        if (FpsRoot != null) FpsRoot.SetActive(GameplaySettings.ShowFps);
    }

    // Averaged over half a second rather than 1 / the last frame's time, which jumps around too
    // much to read.
    private void Update()
    {
        framesThisInterval++;
        intervalTime += Time.unscaledDeltaTime;
        if (intervalTime < 0.5f)
        {
            return;
        }

        float fps = framesThisInterval / intervalTime;
        framesThisInterval = 0;
        intervalTime = 0f;

        if (fpsText != null && fpsText.isActiveAndEnabled)
        {
            fpsText.text = $"{fps:F0} <size=70%><color=#FF8E06>FPS</color></size>";
        }
    }

    private void UpdatePing()
    {
        if (pingText == null || !pingText.isActiveAndEnabled)
            return;

        // Measured by NetworkPing's own round trip. The transport can't be asked: FacepunchTransport
        // always reports 0, which is why this used to read "0 ms" for everyone.
        float ping = NetworkPing.CurrentPingMs;
        if (ping < 0f)
        {
            pingText.text = "-- <size=70%><color=#FF8E06>MS</color></size>";
            return;
        }

        Color color = ping >= badPingMs ? badColor : ping >= okPingMs ? okColor : goodColor;
        pingText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{ping:F0}</color> <size=70%><color=#FF8E06>MS</color></size>";
    }
}
