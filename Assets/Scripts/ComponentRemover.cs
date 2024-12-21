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
            Destroy(GetComponent<NetworkObject>());
            Destroy(GetComponent<NetworkCosmetics>());
            transform.parent = GameObject.Find("Player6").transform;
            transform.localScale = Vector3.one;
            hasChanged = true;
        }
    }
}
