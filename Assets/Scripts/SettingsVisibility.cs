using Unity.Netcode;
using UnityEngine;

public class SettingsVisibility : NetworkBehaviour
{
    [SerializeField] private GameObject settingsPanel, statsPanel;
    // Awake is called when the script instance is being loaded
    public override void OnNetworkSpawn()
    {
        settingsPanel.SetActive(true);
        settingsPanel.SetActive(false);

        statsPanel.SetActive(true);
    }
}
