using Unity.Cinemachine;
using UnityEngine;

public class CamPosChanger : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private int activePriority = 100;
    [SerializeField] private int inactivePriority = 0;

    private bool lastMenuActive;
    private int lastPlayerCount = -1;

    void Update()
    {
        RefreshCameraPriorities();
    }

    private void RefreshCameraPriorities()
    {
        bool menuActive = mainMenu != null && mainMenu.activeSelf;
        int playerCount = GridManager.Instance != null ? GridManager.Instance.CurrentCharacterCount : 0;

        if (menuActive == lastMenuActive && playerCount == lastPlayerCount)
            return;

        lastMenuActive = menuActive;
        lastPlayerCount = playerCount;

        int cameraCount = transform.childCount;
        if (cameraCount == 0)
            return;

        // Child 0 is the menu camera. When the lobby is open, pick one lobby camera based on player count.
        SetCameraPriority(0, menuActive ? activePriority : inactivePriority);

        for (int i = 1; i < cameraCount; i++)
        {
            CinemachineCamera camera = transform.GetChild(i).GetComponent<CinemachineCamera>();
            if (camera == null)
                continue;

            int selectedCameraIndex = playerCount > 0 ? Mathf.Clamp(playerCount, 1, cameraCount - 1) : -1;
            camera.Priority = !menuActive && i == selectedCameraIndex ? activePriority + i : inactivePriority;
        }
    }

    private void SetCameraPriority(int childIndex, int priority)
    {
        if (childIndex < 0 || childIndex >= transform.childCount)
            return;

        CinemachineCamera camera = transform.GetChild(childIndex).GetComponent<CinemachineCamera>();
        if (camera != null)
        {
            camera.Priority = priority;
        }
    }
}
