using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Rocks : MonoBehaviour
{
    void Awake()
    {
        DestroyServerRpc(10);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.StunPlayerServerRpc(other.GetComponent<NetworkObject>().OwnerClientId);
        }
        DestroyServerRpc(0);
        Destroy(gameObject);
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
