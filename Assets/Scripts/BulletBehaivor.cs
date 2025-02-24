using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class BulletBehavior : NetworkBehaviour
{
    private Rigidbody rb;
    private float speed = 15f;
    public NetworkVariable<Vector3> initialVelocity;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        DestroyServerRpc(5);
        initialVelocity.OnValueChanged += MoveBullet;
    }
    private void MoveBullet(Vector3 previousValue, Vector3 newValue) 
    {
        transform.rotation = Quaternion.LookRotation(newValue);
        rb.linearVelocity = newValue * speed;
    }
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        initialVelocity.OnValueChanged -= MoveBullet;
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
