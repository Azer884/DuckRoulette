using UnityEngine;

public class SettingsVisibility : MonoBehaviour
{
    [SerializeField] private GameObject settingsPanel, statsPanel;
    // Awake is called when the script instance is being loaded
    void Awake()
    {
        settingsPanel.SetActive(true);
        settingsPanel.SetActive(false);

        statsPanel.SetActive(true);
    }
}
