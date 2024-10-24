using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class BulletBehavior : NetworkBehaviour
{
    private Rigidbody rb;
    public ulong bulletId = 10;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        StartCoroutine(DestroyAfterDelay());
    }

    void OnCollisionEnter(Collision collision)
    {
        rb.useGravity = true;
        if (!IsServer) return;
    }

    private IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(5);
        GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
    }
}
