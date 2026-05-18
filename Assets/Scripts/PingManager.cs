using Unity.Netcode;
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

        // Prefer the global StatTracker if available
        if (StatTracker.Instance != null)
        {
            pingText.text = $"Ping: {StatTracker.Instance.currentPing:F0} ms";
            return;
        }

        // Fallback: try to get RTT from the active transport (if supported)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null && NetworkManager.Singleton.IsClient)
        {
            try
            {
                // Many transports return RTT in milliseconds as an unsigned long
                var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
                if (transport != null)
                {
                    ulong rtt = transport.GetCurrentRtt(NetworkManager.Singleton.LocalClientId);
                    pingText.text = $"Ping: {rtt} ms";
                    return;
                }
            }
            catch
            {
                // ignore and fallthrough to N/A
            }
        }

        // Nothing available
        pingText.text = "Ping: N/A";
    }
}
