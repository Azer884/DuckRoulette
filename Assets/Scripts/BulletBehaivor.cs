using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class BulletBehavior : NetworkBehaviour
{
    private Rigidbody rb;
    private float speed = 15f;
    private Coroutine destroyRoutine;
    public NetworkVariable<Vector3> initialVelocity = new();
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        else
        {
            Debug.LogError("Bullet missing Rigidbody component!");
        }

        if (IsServer)
        {
            ScheduleDestroy(5);
        }

        initialVelocity.OnValueChanged += MoveBullet;
    }
    
    private void MoveBullet(Vector3 previousValue, Vector3 newValue) 
    {
        if (rb != null)
        {
            transform.rotation = Quaternion.LookRotation(newValue);
            rb.linearVelocity = newValue * speed;
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        initialVelocity.OnValueChanged -= MoveBullet;
    }

    // RequireOwnership (the default) means only the shooter (this bullet's owner) can request an
    // early despawn - previously any client (including the intended victim) could despawn an
    // incoming bullet themselves to dodge it before it registers a hit. OnNetworkSpawn already
    // schedules an unconditional server-side despawn after 5s regardless, so a missed call here
    // only costs bullet lifetime, never a permanent leak.
    [ServerRpc]
    public void DestroyServerRpc(float delay)
    {
        ScheduleDestroy(delay);
    }

    // Called once per real hit, from the same dedupe-gated spot as kill credit (see
    // DeathTrigger.OnTriggerEnter) - not from DestroyServerRpc, which can fire multiple times per
    // bullet in one frame across this player's several hitbox colliders.
    [ServerRpc]
    public void SpawnImpactVfxServerRpc(Vector3 position)
    {
        SpawnImpactVfxClientRpc(position);
    }

    [ClientRpc]
    private void SpawnImpactVfxClientRpc(Vector3 position)
    {
        if (VfxManager.Instance == null)
        {
            return;
        }

        VfxManager.SpawnOneShot(VfxManager.Instance.bulletImpactVfxPrefab, position, VfxManager.Instance.bulletImpactVfxLifetime);
    }

    private void ScheduleDestroy(float delay)
    {
        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
        }

        destroyRoutine = StartCoroutine(DestroyAfterDelay(delay));
    }

    private IEnumerator DestroyAfterDelay(float waitingTime)
    {
        yield return new WaitForSeconds(waitingTime);
        if (this != null && TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
        {
            netObj.Despawn();
        }
    }
}
