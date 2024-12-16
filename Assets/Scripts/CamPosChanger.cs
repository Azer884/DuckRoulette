using Unity.Cinemachine;
using UnityEngine;
using System.Collections;

public class CamPosChanger : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private Transform players;
    [SerializeField] private float delayDuration = 0.2f; // Duration of the delay in seconds

    private bool isSwitchingCameras = false;

    void Update()
    {
        if (!isSwitchingCameras)
        {
            StartCoroutine(DelayedCameraSwitch());
        }
    }

    private IEnumerator DelayedCameraSwitch()
    {
        isSwitchingCameras = true;

        // Wait for the delay duration before switching cameras
        yield return new WaitForSeconds(delayDuration);

        transform.GetChild(0).GetComponent<CinemachineCamera>().Priority = mainMenu.activeSelf ? 10 : 0;

        for (int i = 1; i < 5; i++)
        {
            transform.GetChild(i).GetComponent<CinemachineCamera>().Priority = PriorityValue(i + 1, 10 + i * 5, 9);
        }

        isSwitchingCameras = false;
    }

    private int PriorityValue(int childIndex, int activeValue, int inactiveValue)
    {
        return mainMenu.activeSelf
            ? players.GetChild(childIndex).childCount > 0
                ? activeValue
                : inactiveValue
            : 0;
    }
}
