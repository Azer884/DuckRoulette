using UnityEngine;
using Unity.Netcode;

public class GunStateChanger : NetworkBehaviour
{
    [SerializeField]private Shooting shooting;

    private void Update()
    {
        if (!IsOwner)
        {
            shooting.gun.SetActive(shooting.haveGun.Value);
        }
    }
}