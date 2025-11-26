using UnityEngine;
using Unity.Netcode;

public class Interact : NetworkBehaviour
{
    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    public Transform bumBoxPickUpPosition, fakeBox, fakeboxShadow;
    private Transform pickedUpObject;
    public Shooting shooting;

    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
    }

    // Update is called once per frame
    void Update()
    {
        if (pickedUpObject != null && Input.GetKeyDown(KeyCode.E))
        {
            DropObject();
            return;
        }

        // Raycast for interactions
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward,
                out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            // Try pickup
            if (Input.GetKeyDown(KeyCode.E) && pickedUpObject == null)
            {
                if (shooting.enabled) return;
                PickUpObject(hit.collider);
            }

            // Try mute
            if (Input.GetKeyDown(KeyCode.F))
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

            if (Input.GetKeyDown(KeyCode.F))
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
        NetworkManager.Singleton.SpawnManager.SpawnedObjects[objToMove].transform.SetPositionAndRotation(position, rotation);
    }
}
