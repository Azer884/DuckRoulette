using Unity.Netcode;
using UnityEngine;
using TMPro;

public class StatsManager : MonoBehaviour
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
        int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
        // Update the UI Text
        if (fpsText != null)
        {
            fpsText.text = $"FPS: {fps}";
        }
    }
    private void UpdatePing()
    {
        pingText.text = $"Ping: {NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)} ms";
    }
}
