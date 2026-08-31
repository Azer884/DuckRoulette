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
    }
}
