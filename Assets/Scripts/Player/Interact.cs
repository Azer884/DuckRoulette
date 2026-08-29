using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class Interact : NetworkBehaviour
{
    // Public so TutorialManager can inject the offline wiring at runtime.
    public LayerMask pickUpLayerMask;
    public float maxDistance = 5f;
    public Transform bumBoxPickUpPosition, fakeBox, fakeboxShadow;
    private Transform pickedUpObject;
    private Transform mainCameraTransform;
    public Shooting shooting;
    private InputAction interactAction, muteAction;

    // Raised whenever the local player picks up / drops an interactable, so external
    // systems (e.g. TutorialManager step tracking) don't have to poll for it.
    public event Action ObjectPickedUp;
    public event Action ObjectDropped;

    private bool isPaused;

    // True when running without an active Netcode session (offline tutorial): held objects
    // are moved directly on this client and no ServerRpc is sent.
    public bool IsLocalMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

    /// <summary>Whether the local player is currently holding an object.</summary>
    public bool IsHoldingObject => pickedUpObject != null;

    /// <summary>The object currently being held, if any.</summary>
    public Transform HeldObject => pickedUpObject;

    /// <summary>Force-drops whatever is currently held (e.g. used by TutorialReseter).</summary>
    public void DropHeldObject()
    {
        if (pickedUpObject != null)
        {
            DropObject();
        }
    }

    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
        mainCameraTransform = Camera.main != null ? Camera.main.transform : null;

        CacheInputActions();
    }

    private void Awake()
    {
        // Always cache here, not just in local/offline mode: a Tutorial player is never
        // Netcode-spawned, so OnNetworkSpawn (which also caches these) never runs for it.
        // Gating this on IsLocalMode was wrong - IsLocalMode reflects whether *any*
        // NetworkManager happens to be listening (e.g. still connected because Tutorial was
        // opened from an active Lobby session), not whether this object will ever be spawned.
        // When a stale NetworkManager was listening, this block was skipped, interactAction/
        // muteAction were never assigned, and Update() threw a NullReferenceException the
        // moment it touched them (blocking pickup and the interaction-prompt HUD). Caching
        // unconditionally is harmless for the real networked player too, since OnNetworkSpawn
        // re-caches the same actions.
        mainCameraTransform = Camera.main != null ? Camera.main.transform : null;
        CacheInputActions();
    }

    private void CacheInputActions()
    {
        InputActionAsset inputActions = GetComponent<InputSystem>().inputActions;
        interactAction = inputActions.FindAction("Interact");
        muteAction = inputActions.FindAction("Mute");
    }

    private void OnEnable()
    {
        PauseMenu.OnPause += HandlePause;
        PauseMenu.OnUnPause += HandleUnpause;
    }

    private void HandlePause()
    {
        isPaused = true;
        InteractionPromptHUD.Hide();
    }

    private void HandleUnpause()
    {
        isPaused = false;
    }

    // Update is called once per frame
    void Update()
    {
        // Interact.Update() only reads input.triggered (which naturally reports false while
        // RebindSaveLoad.Instance.input is disabled by PauseMenu.Pause()) but the raycast below
        // doesn't - it kept running and re-showing the prompt every frame the crosshair still
        // hovered an interactable, even while paused.
        if (isPaused)
        {
            return;
        }

        // Re-resolve lazily: the first-person camera may only become active after spawn
        // (or after TutorialManager sets up the offline rig).
        if (mainCameraTransform == null)
        {
            mainCameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (mainCameraTransform == null)
            {
                return;
            }
        }

        if (pickedUpObject != null && interactAction.triggered)
        {
            DropObject();
            return;
        }

        // Raycast for interactions
        if (Physics.Raycast(mainCameraTransform.position, mainCameraTransform.forward,
                out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            if (pickedUpObject == null)
            {
                UpdateInteractionPrompt(hit.collider);
            }
            else
            {
                InteractionPromptHUD.Hide();
            }

            // Try pickup
            if (interactAction.triggered && pickedUpObject == null)
            {
                if (shooting.enabled) return;
                PickUpObject(hit.collider);
            }

            // Try mute
            if (muteAction.triggered)
            {
                TryToMute(hit.transform);
            }
        }
        else
        {
            InteractionPromptHUD.Hide();
        }

        // Move object you are holding
        if (pickedUpObject != null)
        {
            var interact = pickedUpObject.GetComponent<IInteractable>();

            if (interact.IsPickable)
            {
                if (IsLocalMode)
                {
                    // Offline: move the real object directly - no fake-box swap, matching the
                    // original tutorial behavior.
                    pickedUpObject.SetPositionAndRotation(
                        bumBoxPickUpPosition.position,
                        bumBoxPickUpPosition.rotation
                    );
                }
                else
                {
                    MoveObjectServerRpc(
                        pickedUpObject.GetComponent<NetworkObject>().NetworkObjectId,
                        bumBoxPickUpPosition.position,
                        bumBoxPickUpPosition.rotation
                    );

                    fakeBox.localScale = pickedUpObject.localScale;
                    fakeboxShadow.localScale = pickedUpObject.localScale;
                }
            }

            if (muteAction.triggered)
            {
                TryToMute(pickedUpObject);
            }
        }
    }

    private void OnDisable()
    {
        PauseMenu.OnPause -= HandlePause;
        PauseMenu.OnUnPause -= HandleUnpause;
        InteractionPromptHUD.Hide();
    }

    private void UpdateInteractionPrompt(Collider collider)
    {
        if (shooting.enabled ||
            !collider.TryGetComponent(out IInteractable interactable) ||
            interactable.IsHeld)
        {
            InteractionPromptHUD.Hide();
            return;
        }

        InteractionPromptHUD.Show(interactable.InteractionPrompt, interactAction);
    }

    private void PickUpObject(Collider collider)
    {
        if(collider.TryGetComponent(out IInteractable interactable) && !interactable.IsHeld)
        {
            interactable.Interact(IsLocalMode ? 0 : OwnerClientId);
            pickedUpObject = collider.transform;

            if (IsLocalMode)
            {
                ObjectPickedUp?.Invoke();
                return;
            }

            if (interactable.IsPickable)
            {
                Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 2);
            }
        }
    }

    private void DropObject()
    {
        pickedUpObject.GetComponent<IInteractable>().Drop();

        if (!IsLocalMode)
        {
            bool isPickable = pickedUpObject.GetComponent<IInteractable>().IsPickable;
            if (isPickable)
            {
                Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 13);
            }
        }

        pickedUpObject = null;
        ObjectDropped?.Invoke();
    }

    private void TryToMute(Transform obj)
    {
        if (obj.TryGetComponent(out BumBox bumBox))
        {
            bumBox.Mute();
        }
        else if (obj.TryGetComponent(out OfflineBumBox offlineBumBox))
        {
            offlineBumBox.Mute();
        }
    }

    [ServerRpc]
    private void MoveObjectServerRpc(ulong objToMove, Vector3 position, Quaternion rotation)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(objToMove, out var networkObject))
        {
            networkObject.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
