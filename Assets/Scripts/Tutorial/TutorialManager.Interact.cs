using UnityEngine;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    public Transform bumBoxPickUpPosition;
    public Transform pickedUpObject;
    private InputAction interactAction, muteAction;

    private void CacheInteractInputActions()
    {
        interactAction = inputActions.FindAction("Interact");
        muteAction = inputActions.FindAction("Mute");
    }

    // Update is called once per frame
    void PickUpThrowShut()
    {
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            if (interactAction.triggered)
            {
                if (pickedUpObject == null)
                {
                    if (haveGun) return;

                    PickUpObject(hit.collider);
                }
            }

            if (muteAction.triggered)
            {
                TryToMute(hit.transform);
            }
        }

        else if (pickedUpObject != null)
        {
            pickedUpObject.transform.SetPositionAndRotation(bumBoxPickUpPosition.position, bumBoxPickUpPosition.rotation);
            if (interactAction.triggered)
            {
                DropObject();
            }
            if (muteAction.triggered)
            {
                TryToMute(pickedUpObject);
            }

        }
    }

    private void PickUpObject(Collider collider)
    {
        if (collider.TryGetComponent(out IInteractable interactable) && !interactable.IsHeld)
        {
            interactable.Interact(0);

            if (interactable.IsPickable)
            {
                if (!pickedUp)
                {
                    pickedUp = true;
                    OnPickUp?.Invoke();
                }
                pickedUpObject = collider.transform;
            }
        }
    }

    private void DropObject()
    {
        pickedUpObject.GetComponent<IInteractable>().Drop();

        if (pickedUpObject.GetComponent<IInteractable>().IsPickable)
        {
            pickedUpObject.gameObject.SetActive(true);
            pickedUpObject = null;
            if (!thrown)
            {
                thrown = true;
                OnThrow?.Invoke();
            }
        }
    }

    private void TryToMute(Transform obj)
    {
        if (obj.TryGetComponent(out OfflineBumBox bumBox))
        {
            bumBox.Mute();
            if (!shutDown)
            {
                shutDown = true;
                OnShutDown?.Invoke();
            }
        }
    }
}
