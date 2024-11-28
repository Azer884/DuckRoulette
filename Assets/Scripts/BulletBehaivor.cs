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
        
        StartCoroutine(DestroyAfterDelay(5));
        initialVelocity.OnValueChanged += MoveBullet;
    }

    public void DestroyNow()
    {
        StartCoroutine(DestroyAfterDelay(0));
    }

    public IEnumerator DestroyAfterDelay(float waitingTime)
    {
        yield return new WaitForSeconds(waitingTime);
        GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
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
}
