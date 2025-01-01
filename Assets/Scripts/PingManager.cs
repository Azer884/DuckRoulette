using Unity.Netcode;
using UnityEngine;
using TMPro;
using System.Collections;
using Unity.VisualScripting; // For text display, optional

public class StatsManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI pingText;
    [SerializeField] private TextMeshProUGUI fpsText;

    private void OnEnable()
    {
        StartCoroutine(UpdateAfterDelay(1));
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
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            float rtt = NetworkManager.Singleton.ServerTime.TimeAsFloat - NetworkManager.Singleton.LocalTime.TimeAsFloat;
            int ping = Mathf.RoundToInt(rtt * 1000f); // Convert seconds to milliseconds
            if (pingText != null)
            {
                pingText.text = $"Ping: {ping} ms";
            }
        }
    }

    private IEnumerator UpdateAfterDelay(float delay)
    {
        while (true)
        {
            UpdateFps();
            UpdatePing();

            yield return new WaitForSeconds(delay);
        }
    }
}
