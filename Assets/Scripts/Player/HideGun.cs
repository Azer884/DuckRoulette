using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;


public class HideGun : MonoBehaviour
{
    public bool haveGun;
    [SerializeField]private Shooting gunScript;
    private InputActionAsset inputActions;
    [HideInInspector] public  float survivedTime = 0f;
    private void Awake() 
    {
        inputActions = GetComponent<InputSystem>().inputActions;
    }


    private void Update() 
    {
        haveGun = (int)GetComponent<NetworkObject>().OwnerClientId == GameManager.Instance.playerWithGun;
        if(!haveGun)
        {
            survivedTime += Time.deltaTime;
        }
        
        haveGun = haveGun && GameManager.Instance.canShoot.Value && gunScript.canTrigger && gunScript.canShoot;
        if (haveGun)
        {
            if (inputActions.FindAction("Change Weapon").triggered)
            {
                gunScript.enabled = !gunScript.enabled;
            }
        }
    }
}