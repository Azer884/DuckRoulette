using Unity.Cinemachine;
using UnityEngine;

public class CamPosChanger : MonoBehaviour
{
    [SerializeField] private GameObject lobbyMenu;
    [SerializeField] private int activePriority = 100;
    [SerializeField] private int inactivePriority = 0;

    private bool lastLobbyActive;
    private int lastPlayerCount = -1;

    void Update()
    {
        RefreshCameraPriorities();
    }

    private void RefreshCameraPriorities()
    {
        bool lobbyActive = lobbyMenu != null && lobbyMenu.activeSelf;
        int playerCount = GridManager.Instance != null ? GridManager.Instance.CurrentCharacterCount : 0;

        if (lobbyActive == lastLobbyActive && playerCount == lastPlayerCount)
            return;

        lastLobbyActive = lobbyActive;
        lastPlayerCount = playerCount;

        int cameraCount = transform.childCount;
        if (cameraCount == 0)
            return;

        // Children are indexed by player count: child 0 = solo, child 1 = duo (2 players), etc.
        // Solo is the default (main menu, join-lobby browser, anything else) - duo-and-up only
        // kicks in once the lobby screen itself is actually open, even with just one player in it.
        int selectedCameraIndex;
        if (!lobbyActive)
        {
            selectedCameraIndex = 0;
        }
        else
        {
            int effectivePlayerCount = Mathf.Max(playerCount, 2);
            selectedCameraIndex = Mathf.Clamp(effectivePlayerCount, 2, cameraCount) - 1;
        }

        for (int i = 0; i < cameraCount; i++)
        {
            CinemachineCamera camera = transform.GetChild(i).GetComponent<CinemachineCamera>();
            if (camera == null)
                continue;

            camera.Priority = i == selectedCameraIndex ? activePriority + i : inactivePriority;
        }
    }
}
