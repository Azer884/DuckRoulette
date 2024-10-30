using UnityEngine;
using UnityEngine.InputSystem;


public class InputSystem : MonoBehaviour
{
    public InputActionAsset inputActions;

    private void OnEnable() 
    {
        inputActions.Disable();
        inputActions.Enable();
    }
}