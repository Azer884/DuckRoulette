using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;


public class HideGun : MonoBehaviour
{
    private bool haveGun;
    [SerializeField]private Shooting gunScript;
    private InputActionAsset inputActions;

    private void Awake() {
        inputActions = RebindSaveLoad.Instance.actions;
    }

    private void Update() {
        haveGun = (int)GetComponent<NetworkObject>().OwnerClientId == GameManager.Instance.playerWithGun;
        if (haveGun)
        {
            if (inputActions.FindAction("Change Weapon").triggered)
            {
                gunScript.enabled = !gunScript.enabled;
            }
        }
    }
}