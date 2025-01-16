using UnityEngine;
using Unity.Netcode;

public class Death : NetworkBehaviour
{
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);
}
