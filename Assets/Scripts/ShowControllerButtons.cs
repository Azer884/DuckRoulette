using UnityEngine;

public class ShowControllerButtons : MonoBehaviour
{
    [SerializeField] private GameObject[] buttons;
    void Update()
    {   
        foreach (GameObject child in buttons)
        {
            child.SetActive(RebindSaveLoad.Instance.currentControlScheme == "Gamepad");
        }
    }
}
