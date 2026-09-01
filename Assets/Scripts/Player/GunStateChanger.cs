using UnityEngine;
using Unity.Netcode;

public class GunStateChanger : NetworkBehaviour
{
    [SerializeField]private Shooting shooting;

    private void Update()
    {
        // Runs for the owner too now, not just remote copies.
        //
        // shooting.gun is the THIRD-PERSON gun: it hangs off Player3rd's Hand_R, it is the gun
        // every other player actually sees, and its Animator is both what Shooting's
        // Play("Reload")/Play("Shooting") calls drive and what the gun's own OwnerNetworkAnimator
        // watches for state changes to replicate. (The owner's own first-person gun and its shadow
        // are two separate instances, swapped by Hands.SwitchParent.)
        //
        // On the owner that object was left inactive: Ragdoll.Awake -> DisableRagdoll ->
        // SetVisualsEnabled(true) switches it off at startup (HasGun is false then) and nothing
        // ever switched it back on, because this check used to be gated to !IsOwner. An Animator on
        // an inactive GameObject never evaluates, so Play() produced no state change on the
        // authority, so the NetworkAnimator had nothing to send: the gun in a remote player's hand
        // showed up but never played reload/trigger/shoot for anyone watching. Only the Triggered
        // bool got through, since parameters are read straight off the Animator either way.
        //
        // The owner does not see this gun themselves - Movement.ApplyOwnerVisualState puts the
        // whole third-person body (and therefore its child gun) on the layer their own camera
        // culls, which is exactly why it can be left active for them.
        shooting.gun.SetActive(shooting.HasGun);
    }
}
