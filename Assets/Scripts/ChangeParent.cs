using UnityEngine;
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;

public class ChangeParent : NetworkBehaviour
{
    [SerializeField]private Transform child;
    [SerializeField]private Transform parent;
    [SerializeField]private bool matchParentTransform;

    public override void OnNetworkSpawn() {
        if(IsOwner)
        {
            child.parent = parent;
        }
        if (matchParentTransform)
        {
            child.position = child.parent.position;
        }
    }

}