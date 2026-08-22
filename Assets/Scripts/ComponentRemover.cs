using UnityEngine;
using Unity.Netcode;

public class ComponentRemover : NetworkBehaviour
{
    private bool hasChanged = false;
    public override void OnNetworkSpawn()
    {
    }
    
    private void Update() 
    {
        if(!hasChanged)
        {
            GameObject player6 = GameObject.Find("Player6");
            if (player6 == null)
            {
                return;
            }

            Destroy(GetComponent<NetworkObject>());
            Destroy(GetComponent<NetworkCosmetics>());
            transform.parent = player6.transform;
            transform.localScale = Vector3.one;
            hasChanged = true;
        }
    }
}
