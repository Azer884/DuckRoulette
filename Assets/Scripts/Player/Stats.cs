using UnityEngine;
using Unity.Netcode;

public class Stats : NetworkBehaviour
{
    public NetworkVariable<float> timeSurvived = new(0f, NetworkVariableReadPermission.Everyone , NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> shotCounter = new(0, NetworkVariableReadPermission.Everyone , NetworkVariableWritePermission.Owner), 
        emptyShots = new(0, NetworkVariableReadPermission.Everyone , NetworkVariableWritePermission.Owner);
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
    }
}
