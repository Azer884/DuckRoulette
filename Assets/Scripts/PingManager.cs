using Unity.Netcode;
using UnityEngine;
using TMPro;
using Steamworks.Data;

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
        if (fpsText != null && StatTracker.Instance != null)
        {
            fpsText.text = $"FPS: {StatTracker.Instance.currentFPS:F0}";
        }
    }
    
    private void UpdatePing()
    {
        if (pingText != null && StatTracker.Instance != null)
        {
            pingText.text = $"Ping: {StatTracker.Instance.currentPing:F0} ms";
        }
    }
}
