using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs even while Shooting is disabled (that's the whole point): keeps the assigned player's
// gun hidden until they explicitly draw it with the Change Weapon input, and forces it back
// down the instant their turn ends - mirrors TutorialManager's offline HandleWeaponSwitching.
public class HideGun : MonoBehaviour
{
    [SerializeField] private Shooting gunScript;
    private InputAction changeWeaponAction;
    private NetworkObject networkObject;

    private void Awake()
    {
        changeWeaponAction = GetComponent<InputSystem>().inputActions.FindAction("Change Weapon");
        networkObject = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        if (!networkObject.IsSpawned || !networkObject.IsOwner || GameManager.Instance == null)
        {
            return;
        }

        bool isMyTurn = GameManager.Instance.playerWithGun.Value == networkObject.OwnerClientId
            && GameManager.Instance.canShoot.Value;

        if (!isMyTurn)
        {
            gunScript.enabled = false;
            return;
        }

        // Mid Trigger/Reload animation: block switching away so the gun can't vanish
        // half-way through firing.
        if (gunScript.enabled && (!gunScript.canTrigger || !gunScript.canShoot))
        {
            return;
        }

        if (changeWeaponAction.triggered)
        {
            gunScript.enabled = !gunScript.enabled;
        }
    }
}
