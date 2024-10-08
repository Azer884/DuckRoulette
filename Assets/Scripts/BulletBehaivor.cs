using Unity.Netcode;
using UnityEngine;

public class BulletBehaivor : MonoBehaviour
{
    private void Awake() 
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        Destroy(gameObject, 5f);
    }
    void OnCollisionEnter(Collision collision)
    {
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        GetComponent<Rigidbody>().useGravity = true;

        if (collision.transform.TryGetComponent<Ragdoll>(out var ragdoll))
        {
            Debug.Log("Dead");
            ragdoll.TriggerRagdoll(true);
        }
    }
}
