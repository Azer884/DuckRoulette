using UnityEngine;
using Unity.Netcode;

public class BonesState : NetworkBehaviour
{
    public NetworkVariable<bool> bonesState = new(true);
}
