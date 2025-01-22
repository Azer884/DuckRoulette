using UnityEngine;
using Unity.Netcode;

public class PickUp : NetworkBehaviour
{
    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private Transform bumBoxPickUpPosition, fakeBox, fakeboxShadow;
    private Transform pickedUpObject;
    public Shooting shooting;
    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
    }

    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (pickedUpObject == null)
            {
                if (shooting.enabled) return;
                
                PickUpObject();
            }
            else
            {
                DropObject();
            }
        }

        if (pickedUpObject != null)
        {
            pickedUpObject.transform.position = bumBoxPickUpPosition.position;
            pickedUpObject.transform.rotation = bumBoxPickUpPosition.rotation;

            fakeBox.localScale = pickedUpObject.localScale;
            fakeboxShadow.localScale = pickedUpObject.localScale;
        }
    }

    private void PickUpObject()
    {
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            pickedUpObject = hit.collider.transform;
            hit.collider.isTrigger = true;
            if (pickedUpObject.CompareTag("Player"))
            {
                Destroy(pickedUpObject.GetComponent<Rigidbody>());

                fakeBox.gameObject.SetActive(true);
                fakeboxShadow.gameObject.SetActive(true);

                pickedUpObject.position = bumBoxPickUpPosition.position;
                pickedUpObject.rotation = bumBoxPickUpPosition.rotation;
                Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 2);
            }
        }
    }

    private void DropObject()
    {
        fakeBox.gameObject.SetActive(false);
        fakeboxShadow.gameObject.SetActive(false);

        pickedUpObject.GetComponent<Collider>().isTrigger = false;
        Rigidbody rb = pickedUpObject.gameObject.AddComponent<Rigidbody>();
        rb.AddForce(Camera.main.transform.forward * 5f, ForceMode.Impulse);

        Movement.ChangeLayerRecursively(pickedUpObject.gameObject, 13);
        pickedUpObject = null;
    }
}
