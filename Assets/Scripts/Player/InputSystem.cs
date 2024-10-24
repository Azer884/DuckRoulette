using UnityEngine;
using UnityEngine.InputSystem;


public class InputSystem : MonoBehaviour
{
    public InputActionAsset inputActions;

    private void OnEnable() 
    {
        inputActions.Enable();
    }
    private void OnDisable() 
    {
        inputActions.Disable();
    }
}