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
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (pickedUpObject == null)
                {
                    if (shooting.enabled) return;
                    
                    PickUpObject(hit.collider);
                }
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                TryToMute(hit.transform);
            }
        }

        else if (pickedUpObject != null)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                DropObject();
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                TryToMute(pickedUpObject);
            }

            MoveObjectServerRpc(pickedUpObject.GetComponent<NetworkObject>().NetworkObjectId, bumBoxPickUpPosition.position, bumBoxPickUpPosition.rotation);
            fakeBox.localScale = pickedUpObject.localScale;
            fakeboxShadow.localScale = pickedUpObject.localScale;
        }
    }

    private void PickUpObject(Collider collider)
    {
        if(collider.TryGetComponent(out IInteractable interactable) && !interactable.IsHeld)
        {
            interactable.Interact(OwnerClientId);

            if (interactable.IsPickable)
            {
                pickedUpObject = collider.transform;
                Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 2);
            }
        }
    }

    private void DropObject()
    {
        pickedUpObject.GetComponent<IInteractable>().Drop();

        if (pickedUpObject.GetComponent<IInteractable>().IsPickable)
        {
            Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 13);
            pickedUpObject = null;
        }
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
