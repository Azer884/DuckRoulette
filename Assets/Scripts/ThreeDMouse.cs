using UnityEngine;

public class ThreeDMouse : MonoBehaviour
{
    [SerializeField]private Transform cam;
    [SerializeField] private LayerMask hitLayer;
    [SerializeField] private float rotSpeed = 5f;
    void Update()
    {
        Ray ray = new(cam.position, cam.forward);

        if (Physics.Raycast(ray, out RaycastHit raycastHit, 200, hitLayer))
        {
            transform.position = Vector3.Lerp(transform.position, raycastHit.point, Time.deltaTime * rotSpeed);
        }
        else
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, new Vector3(0, 0, 200), Time.deltaTime * rotSpeed);
        }
    }
}