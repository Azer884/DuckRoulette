using Unity.Cinemachine;
using UnityEngine;

public class CamPosChanger : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;

    // Update is called once per frame
    void Update()
    {
        if (mainMenu.activeSelf)
        {
            GetComponent<CinemachineCamera>().Priority = 10;
        }
        else
        {
            GetComponent<CinemachineCamera>().Priority = 0;
        }
    }
}
