using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Rocks : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        // IsServer/IsClient are only valid from OnNetworkSpawn onward, not in Awake.
        if (IsServer)
        {
            DestroyServerRpc(10);
        }
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnCollisionEnter(Collision other)
    {
        if (!IsServer) return;

        if (other.transform.CompareTag("Hittable"))
            GameManager.Instance.StunPlayerServerRpc(other.transform.GetComponentInParent<NetworkObject>().OwnerClientId);
        DestroyServerRpc(0);
    }

    // Both call sites above already gate on IsServer and call this on the rock's own
    // (server-owned) instance - RequireOwnership (the default) closes off any client calling
    // this directly to despawn rocks early.
    [ServerRpc]
    public void DestroyServerRpc(float delay)
    {
        StartCoroutine(DestroyAfterDelay(delay));
    }

    private IEnumerator DestroyAfterDelay(float waitingTime)
    {
        yield return new WaitForSeconds(waitingTime);
        if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
        {
            netObj.Despawn();
        }
    }
}
