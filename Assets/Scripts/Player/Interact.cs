using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class Interact : NetworkBehaviour
{
    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    public Transform bumBoxPickUpPosition, fakeBox, fakeboxShadow;
    private Transform pickedUpObject;
    private Transform mainCameraTransform;
    public Shooting shooting;
    private InputAction interactAction, muteAction;

    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
        mainCameraTransform = Camera.main != null ? Camera.main.transform : null;

        InputActionAsset inputActions = GetComponent<InputSystem>().inputActions;
        interactAction = inputActions.FindAction("Interact");
        muteAction = inputActions.FindAction("Mute");
    }

    // Update is called once per frame
    void Update()
    {
        if (mainCameraTransform == null)
        {
            return;
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

        // Move object you are holding
        if (pickedUpObject != null)
        {
            var interact = pickedUpObject.GetComponent<IInteractable>();

            if (interact.IsPickable)
            {
                MoveObjectServerRpc(
                    pickedUpObject.GetComponent<NetworkObject>().NetworkObjectId,
                    bumBoxPickUpPosition.position,
                    bumBoxPickUpPosition.rotation
                );

                fakeBox.localScale = pickedUpObject.localScale;
                fakeboxShadow.localScale = pickedUpObject.localScale;
            }

            if (muteAction.triggered)
            {
                TryToMute(pickedUpObject);
            }
        }
    }

    private void PickUpObject(Collider collider)
    {
        if(collider.TryGetComponent(out IInteractable interactable) && !interactable.IsHeld)
        {
            interactable.Interact(OwnerClientId);
            pickedUpObject = collider.transform;

            if (interactable.IsPickable)
            {
                Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 2);
            }
        }
    }

    private void DropObject()
    {
        pickedUpObject.GetComponent<IInteractable>().Drop();
        bool isPickable = pickedUpObject.GetComponent<IInteractable>().IsPickable;

        if (isPickable)
        {
            Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 13);
        }
        pickedUpObject = null;
    }

    private void TryToMute(Transform obj)
    {
        if (obj.TryGetComponent(out BumBox bumBox))
        {
            bumBox.Mute();
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
