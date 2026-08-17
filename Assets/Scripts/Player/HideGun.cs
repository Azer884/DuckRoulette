using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;


public class HideGun : MonoBehaviour
{
    public bool haveGun;
    [SerializeField]private Shooting gunScript;
    private InputActionAsset _inputActions;
    [HideInInspector] public float survivedTime;
    private void Awake() 
    {
        _inputActions = GetComponent<InputSystem>().inputActions;
    }


    private void Update() 
    {
        haveGun = GetComponent<NetworkObject>().OwnerClientId == GameManager.Instance.playerWithGun.Value;
        if(!haveGun)
        {
            survivedTime += Time.deltaTime;
        }
        
        haveGun = haveGun && GameManager.Instance.canShoot.Value && gunScript.canTrigger && gunScript.canShoot;
        if (haveGun)
        {
            if (_inputActions.FindAction("Change Weapon").triggered)
            {
                gunScript.enabled = !gunScript.enabled;
            }
        }
    }
}