using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs even while Shooting is disabled (that's the whole point): keeps the assigned player's
// gun hidden until they explicitly draw it (Change Weapon toggle, or scroll up/down to pull/
// stow explicitly), and forces it back down the instant their turn ends - mirrors
// TutorialManager's offline HandleWeaponSwitching.
public class HideGun : MonoBehaviour
{
    [SerializeField] private Shooting gunScript;
    private InputAction changeWeaponAction;
    private InputAction pullGunAction;
    private InputAction stowGunAction;
    private NetworkObject networkObject;

    private void Awake()
    {
        InputActionAsset inputActions = GetComponent<InputSystem>().inputActions;
        changeWeaponAction = inputActions.FindAction("Change Weapon");
        pullGunAction = inputActions.FindAction("PullGun");
        stowGunAction = inputActions.FindAction("StowGun");
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

        // Mid Trigger/Reload animation: never cut it off, whether the player is switching away
        // voluntarily or their turn timed out under them - let the current action finish first,
        // then the turn-eligibility check below hides it on the very next frame it's clear.
        if (gunScript.enabled && (!gunScript.canTrigger || !gunScript.canShoot))
        {
            return;
        }

        if (!isMyTurn)
        {
            gunScript.enabled = false;
            return;
        }

        if (changeWeaponAction.triggered)
        {
            gunScript.enabled = !gunScript.enabled;
        }
        else if (pullGunAction.triggered)
        {
            gunScript.enabled = true;
        }
        else if (stowGunAction.triggered)
        {
            gunScript.enabled = false;
        }
    }
}
