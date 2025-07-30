using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class BulletBehavior : NetworkBehaviour
{
    private Rigidbody rb;
    public float speed = 15f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        DestroyServerRpc(5);
    }

    public void Init(Vector3 direction)
    {
        transform.rotation = Quaternion.LookRotation(direction);
        rb.linearVelocity = direction * speed;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DestroyServerRpc(float delay)
    {
        StartCoroutine(DestroyAfterDelay(delay));
    }

    private IEnumerator DestroyAfterDelay(float waitingTime)
    {
        yield return new WaitForSeconds(waitingTime);
        gameObject.GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
    }
}

