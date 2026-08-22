using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;


public class HideGun : MonoBehaviour
{
    public bool haveGun;
    [SerializeField]private Shooting gunScript;
    private InputActionAsset _inputActions;
    private InputAction _changeWeaponAction;
    private NetworkObject _networkObject;
    [HideInInspector] public float survivedTime;
    private void Awake()
    {
        _inputActions = GetComponent<InputSystem>().inputActions;
        _changeWeaponAction = _inputActions.FindAction("Change Weapon");
        _networkObject = GetComponent<NetworkObject>();
    }


    private void Update()
    {
        haveGun = _networkObject.OwnerClientId == GameManager.Instance.playerWithGun.Value;
        if(!haveGun)
        {
            survivedTime += Time.deltaTime;
        }
        
        haveGun = haveGun && GameManager.Instance.canShoot.Value && gunScript.canTrigger && gunScript.canShoot;
        if (haveGun)
        {
            if (_changeWeaponAction.triggered)
            {
                gunScript.enabled = !gunScript.enabled;
            }
        }
    }
}