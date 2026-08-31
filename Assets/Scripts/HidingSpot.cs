using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class HidingSpot : NetworkBehaviour, IInteractable
{
    public enum ViewType
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("View")]
    public ViewType viewType = ViewType.FirstPerson;
    [Tooltip("Camera activated for the local hiding player only. Child of this hiding spot.")]
    public CinemachineCamera hidingCamera;
    [Tooltip("Decoy shown in place of the hidden player, on every client, while this spot is occupied.")]
    public GameObject hidingModel;
    [Tooltip("ThirdPerson only: the transform hidingCamera's Body/Aim (CinemachineThirdPersonFollow/" +
        "CinemachineThirdPersonAim) actually reads rotation from every frame - assign this same " +
        "transform as that CinemachineCamera's Tracking Target. Rotating hidingCamera's own " +
        "transform does nothing for this rig type, since CM recomputes it from the tracking target, " +
        "not the other way around. Leave unset for FirstPerson spots.")]
    public Transform thirdPersonLookTarget;

    [Header("Look limits"), Tooltip("Degrees relative to the look pivot's own authored rest rotation - hidingCamera itself for FirstPerson, thirdPersonLookTarget for ThirdPerson.")]
    public float minYaw = -60f;
    public float maxYaw = 60f;
    public float minPitch = -40f;
    public float maxPitch = 40f;

    public string causeOfLeaving;
    public float hideDuration = 10f;

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = false;
    public string InteractionPrompt => "Hide";
    public int holderId = -1;

    private InputAction lookAction;
    private float yaw, pitch;
    private bool isLocalHider;
    private Vector3 hidePosition;
    private Quaternion hideRotation;
    private bool hitReported;
    private Transform lookPivot;
    private Quaternion lookPivotRestRotation;

    public void Interact(ulong clientId)
    {
        if (IsHeld) return;

        HideServerRpc(clientId);
    }

    public void Drop()
    {
        if (!IsHeld) return;

        ExitServerRpc((ulong)holderId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void HideServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        // clientId is otherwise a client-supplied value with no other check - without this, any
        // connected client could force an arbitrary player into (or out of) hiding.
        if (clientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        HideClientRpc(clientId, client.PlayerObject.NetworkObjectId);
    }

    [ClientRpc]
    private void HideClientRpc(ulong clientId, ulong playerNetworkObjectId)
    {
        IsHeld = true;
        holderId = (int)clientId;
        hitReported = false;

        if (hidingModel != null) hidingModel.SetActive(true);

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObject))
        {
            return;
        }

        Hide(playerObject.gameObject);

        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            EnterLocalHidingView(playerObject.gameObject);
            StartCountDown();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ExitServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        if (clientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        ExitClientRpc(clientId, client.PlayerObject.NetworkObjectId);
    }

    // Unlike ExitServerRpc, the caller here is never the victim - it's whichever peer's bullet
    // collision detected the hit (see OnCollisionEnter), same as GameManager.UpdatePlayerStateServerRpc
    // already trusts a caller-supplied victim id from DeathTrigger. Still validated against this
    // spot's own server-side state (IsHeld/holderId), so a hit can't force-exit someone who isn't
    // actually hiding here.
    [ServerRpc(RequireOwnership = false)]
    private void KillHolderServerRpc(ulong victimId)
    {
        if (!IsHeld || holderId != (int)victimId)
        {
            return;
        }

        if (!NetworkManager.ConnectedClients.TryGetValue(victimId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        ExitClientRpc(victimId, client.PlayerObject.NetworkObjectId);
    }

    [ClientRpc]
    private void ExitClientRpc(ulong clientId, ulong playerNetworkObjectId)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            ExitLocalHidingView();
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObject))
        {
            Exit(playerObject.gameObject);
        }

        if (hidingModel != null) hidingModel.SetActive(false);

        IsHeld = false;
        holderId = -1;
    }

    // Runs on every client for the hiding player's NetworkObject: the player stays exactly
    // where they are, they just stop being rendered/controlled/collided-with anywhere.
    private void Hide(GameObject player)
    {
        if (player.TryGetComponent(out Movement movement))
        {
            movement.SetModelVisible(false);
        }

        if (player.TryGetComponent(out Username username) && username.userName != null)
        {
            username.userName.gameObject.SetActive(false);
        }

        if (player.TryGetComponent(out Shooting shooting) && shooting.gun != null)
        {
            shooting.gun.SetActive(false);
        }

        // Hitbox: a hidden player must not block bullets/other players. Each client owns its
        // own local copy of every NetworkObject, so this has to be toggled per-client here
        // rather than once on the server.
        if (player.TryGetComponent(out CharacterController controller))
        {
            controller.enabled = false;
        }
    }

    private void Exit(GameObject player)
    {
        if (player.TryGetComponent(out Movement movement))
        {
            movement.SetModelVisible(true);
        }

        if (player.TryGetComponent(out Username username) && username.userName != null)
        {
            username.userName.gameObject.SetActive(!username.IsOwner);
        }

        if (player.TryGetComponent(out Shooting shooting) && shooting.gun != null)
        {
            shooting.gun.SetActive(shooting.HasGun);
        }

        // Re-enable the hitbox after the owner's own client (see ExitLocalHidingView) has already
        // put the transform back where it was clicked from, so the controller doesn't fight it.
        if (player.TryGetComponent(out CharacterController controller))
        {
            controller.enabled = true;
        }
    }

    // Owner-only: swap the local camera and hand input control to whoever is hiding here.
    private void EnterLocalHidingView(GameObject player)
    {
        isLocalHider = true;
        yaw = 0f;
        pitch = 0f;

        // Owner-authoritative transform: the owner's own client is the only one whose position
        // for this player actually sticks, so capture/restore happens here, not in Hide()/Exit().
        hidePosition = player.transform.position;
        hideRotation = player.transform.rotation;

        SetLocalScriptsEnabled(player, false);

        if (player.TryGetComponent(out InputSystem inputSystem))
        {
            lookAction = inputSystem.inputActions.FindAction("Look");
        }

        lookPivot = viewType == ViewType.FirstPerson
            ? (hidingCamera != null ? hidingCamera.transform : null)
            : thirdPersonLookTarget;

        if (lookPivot != null)
        {
            // Level against world up instead of taking the pivot's own rest rotation as-is - a
            // tilted hiding prop (e.g. a fallen log) bakes its own roll into that rest rotation,
            // which would otherwise make the camera visibly tilt/roll as the player looks around.
            Vector3 levelForward = Vector3.ProjectOnPlane(lookPivot.forward, Vector3.up);
            if (levelForward.sqrMagnitude < 0.0001f)
            {
                levelForward = Vector3.ProjectOnPlane(lookPivot.up, Vector3.up);
            }
            lookPivotRestRotation = Quaternion.LookRotation(levelForward.normalized, Vector3.up);
        }

        if (hidingCamera != null)
        {
            // The player's own gameplay cams sit at explicit boosted priorities (CameraHolder 10,
            // SlidingCamHolder 15, ...) so a default/unboosted hidingCamera can never actually win
            // the live-camera vote against them - whatever the player was last looking through
            // (e.g. their normal third-person cam, easy to mistake for a "spectate" view) just
            // kept showing instead of the hiding view.
            hidingCamera.Priority = new PrioritySettings { Enabled = true, Value = 20 };
            hidingCamera.gameObject.SetActive(true);
        }
    }

    private void ExitLocalHidingView()
    {
        isLocalHider = false;
        lookAction = null;
        lookPivot = null;

        if (hidingCamera != null) hidingCamera.gameObject.SetActive(false);

        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null)
        {
            // Put the player back where they clicked Hide from, not wherever they happened to
            // spawn - the CharacterController is still disabled at this point (Exit() re-enables
            // it after), so it can't fight this teleport.
            localPlayer.transform.SetPositionAndRotation(hidePosition, hideRotation);

            SetLocalScriptsEnabled(localPlayer.gameObject, true);

            // A kick-out (shot/timeout) never goes through Interact.DropObject() - it's driven
            // straight from HidingSpot (KillHolderServerRpc/CountDown), not the player's own E
            // press - so Interact's pickedUpObject was left pointing at this spot. The first E
            // press afterward would silently consume itself clearing that stale reference instead
            // of re-triggering Interact(), requiring a second press to actually rehide. Cleared
            // directly (not via DropHeldObject/Drop()) since IsHeld is still true at this point in
            // the kick-out flow - going through Drop() again would re-fire ExitServerRpc.
            if (localPlayer.TryGetComponent(out Interact interact))
            {
                interact.ClearHeldObjectIfMatches(transform);
            }
        }
    }

    // Movement/TeamUp/Shooting/Slap disable themselves on every client except the owner
    // (see Movement/Ragdoll OnNetworkSpawn), so it's only ever meaningful to toggle them here,
    // on the hiding player's own client. Ragdoll.SetScriptsEnabled isn't reused because it
    // restores shooting/slap from whatever it last cached during a ragdoll knockout, not from
    // the gun state at the moment hiding actually started.
    private void SetLocalScriptsEnabled(GameObject player, bool enabledState)
    {
        if (player.TryGetComponent(out Movement movement)) movement.enabled = enabledState;
        if (player.TryGetComponent(out TeamUp teamUp)) teamUp.enabled = enabledState;

        if (player.TryGetComponent(out Shooting shooting) && player.TryGetComponent(out Slap slap))
        {
            bool hasGun = enabledState && shooting.HasGun;
            shooting.enabled = hasGun;
            slap.enabled = enabledState && !hasGun;
        }
    }

    private void Update()
    {
        if (!isLocalHider || lookPivot == null || lookAction == null)
        {
            return;
        }

        Vector2 look = lookAction.ReadValue<Vector2>();
        float sensitivityX = 1f;
        float sensitivityY = 1f;

        if (SettingsManager.Instance != null)
        {
            sensitivityX = SettingsManager.Instance.MouseSensitivityX;
            sensitivityY = SettingsManager.Instance.MouseSensitivityY;
        }

        yaw = Mathf.Clamp(yaw + look.x * sensitivityX * Time.deltaTime, minYaw, maxYaw);
        pitch = Mathf.Clamp(pitch - look.y * sensitivityY * Time.deltaTime, minPitch, maxPitch);

        // World-space, not local: lookPivotRestRotation is already levelled against world up
        // (see EnterLocalHidingView), so applying pitch/yaw here keeps the camera level
        // regardless of how the hiding prop itself is tilted/parented.
        lookPivot.rotation = lookPivotRestRotation * Quaternion.Euler(pitch, yaw, 0f);
    }

    // Mirrors DeathTrigger.OnTriggerEnter's peer-detection pattern, against this spot's own
    // collider instead of a player hitbox - whoever's hiding here has no hitbox of their own to
    // be hit while hidden, so a shot has to land on the spot itself. Bullet.prefab's own collider
    // is a trigger (see DeathTrigger), so this has to be OnTriggerEnter too - OnCollisionEnter
    // never fires against a trigger collider, which silently made the log unshootable.
    private void OnTriggerEnter(Collider other)
    {
        if (!IsHeld || hitReported)
        {
            return;
        }

        BulletBehavior bullet = other.GetComponentInParent<BulletBehavior>();
        if (bullet == null)
        {
            return;
        }

        ulong victimId = (ulong)holderId;
        if (bullet.OwnerClientId == victimId)
        {
            return;
        }

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(victimId, out var victimClient) &&
            victimClient.PlayerObject != null &&
            victimClient.PlayerObject.TryGetComponent(out TeamUp teamUp) &&
            teamUp.isTeamedUp && teamUp.teamMateId == (int)bullet.OwnerClientId)
        {
            return;
        }

        hitReported = true;

        if (bullet.IsOwner)
        {
            bullet.DestroyServerRpc(0);
            GameManager.Instance.UpdateKillsServerRpc(bullet.OwnerClientId, 1);
            bullet.SpawnImpactVfxServerRpc(bullet.transform.position);
        }

        // Leave hiding first so the model/camera/hitbox/position all reset before ragdoll takes
        // over - same exit visuals as pressing E or the hide-duration timeout, just server-
        // triggered instead of victim-triggered since the victim's own client didn't request this.
        KillHolderServerRpc(victimId);

        GameManager.Instance.UpdatePlayerStateServerRpc(victimId, bullet.OwnerClientId);
    }

    private void StartCountDown()
    {
        StartCoroutine(CountDown());
    }

    private IEnumerator CountDown()
    {
        yield return new WaitForSeconds(hideDuration);

        Drop();
    }
}
