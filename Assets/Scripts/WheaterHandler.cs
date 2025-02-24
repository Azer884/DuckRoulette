using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

public class WheaterHandler : NetworkBehaviour
{
    [SerializeField] private float duration = 20f;
    [SerializeField] private GameObject rockPrefab;
    [SerializeField] private BoxCollider spawnArea;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
        }
    }
    private void OnEnable() 
    {
        GameManager.OnWheaterChanged += HandleWheaterChange;
    }
    private void OnDisable()
    {
        GameManager.OnWheaterChanged -= HandleWheaterChange;
    }
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && IsServer)
        {
            HandleWheaterChange();
        }
    }

    private void HandleWheaterChange()
    {
        HandleWheaterServerRpc();
    }

    private IEnumerator ChangeWheater()
    {
        float timer = 0;
        float spawnInterval = 1f;

        while (timer < duration)
        {
            SpawnRocksServerRpc();
            yield return new WaitForSeconds(1 / spawnInterval);
            timer += 1 / spawnInterval;
        }
        StopBadWheater();
    }

    [ServerRpc (RequireOwnership = false)]
    private void HandleWheaterServerRpc()
    {
        HandleWheaterClientRpc();
        StartCoroutine(ChangeWheater());
    }

    [ClientRpc]
    private void HandleWheaterClientRpc()
    {
        StartBadWheater();
    }

    [ServerRpc (RequireOwnership = false)]
    private void SpawnRocksServerRpc()
    {
        int rockCount = Random.Range(10, 20);
        if (rockPrefab == null || spawnArea == null)
        {
            Debug.LogWarning("Rock prefab or spawn area not assigned!");
            return;
        }

        for (int i = 0; i < rockCount; i++)
        {
            Vector3 randomPosition = GetRandomPositionInBox(spawnArea);
            GameObject newRock = Instantiate(rockPrefab, randomPosition, Quaternion.identity);
            newRock.GetComponent<NetworkObject>().Spawn(true);

            if (newRock.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
            }
        }
    }

    private void StartBadWheater()
    {
        Debug.Log("Bad wheater started!");
    }

    private void StopBadWheater()
    {
        Debug.Log("Bad wheater is over!");
    }

    private Vector3 GetRandomPositionInBox(BoxCollider box)
    {
        Vector3 center = box.bounds.center;
        Vector3 size = box.bounds.size;

        float x = Random.Range(center.x - size.x / 2, center.x + size.x / 2);
        float y = Random.Range(center.y - size.y / 2, center.y + size.y / 2);
        float z = Random.Range(center.z - size.z / 2, center.z + size.z / 2);

        return new Vector3(x, y, z);
    }
}
