using UnityEngine;
using Unity.Netcode;

public class ComponentRemover : NetworkBehaviour
{
    private bool hasChanged = false;
    public override void OnNetworkSpawn()
    {
        Destroy(GetComponent<NetworkObject>());
        Destroy(GetComponent<NetworkCosmetics>());
    }
    
    private void Update() 
    {
        if(!hasChanged)
        {
            transform.parent = GameObject.Find("Player6").transform;
            transform.localScale = Vector3.one;
            hasChanged = true;
        }
    }
}
