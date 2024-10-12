using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class BulletBehavior : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        StartCoroutine(DestroyAfterDelay());
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.useGravity = true;
        if (collision.transform.TryGetComponent(out Ragdoll ragdoll))
        {
            KillPlayerServerRpc(ragdoll.GetComponent<NetworkObject>().OwnerClientId);
            Debug.Log("Collision Detected");
        }
    
    }

    private IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(5);
        GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
    }

    [ServerRpc]
    private void KillPlayerServerRpc(ulong clientId)
    {
        KillPlayerClientRpc(clientId);
    }
    [ClientRpc]
    private void KillPlayerClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            // Get the player's object and trigger the ragdoll
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                playerObject.GetComponent<Ragdoll>().TriggerRagdoll(true);
            }
        }
    }
}