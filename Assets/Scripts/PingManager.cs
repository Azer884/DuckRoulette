using UnityEngine;
using TMPro;

public class PingManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI pingText;
    [SerializeField] private TextMeshProUGUI fpsText;

    private void OnEnable()
    {
        InvokeRepeating(nameof(UpdatePing), 1, 1);
        InvokeRepeating(nameof(UpdateFps), 1, 1);
    }

    private void OnDisable()
    {
        CancelInvoke();
    }

    private void UpdateFps()
    {
        // Update the UI Text
        if (fpsText == null)
            return;

        // Prefer the global StatTracker if available, otherwise compute locally
        if (StatTracker.Instance != null)
        {
            fpsText.text = $"FPS: {StatTracker.Instance.currentFPS:F0}";
        }
        else
        {
            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            fpsText.text = $"FPS: {fps:F0}";
        }
    }
    
    private void UpdatePing()
    {
        if (pingText == null)
            return;

        // Measured by NetworkPing's own round trip. The transport can't be asked: FacepunchTransport
        // always reports 0, which is why this used to read "0 ms" for everyone.
        float ping = NetworkPing.CurrentPingMs;
        pingText.text = ping >= 0f ? $"Ping: {ping:F0} ms" : "Ping: N/A";
    }
}
