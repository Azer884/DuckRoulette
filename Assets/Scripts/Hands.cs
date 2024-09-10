using Unity.Netcode;
using UnityEngine;

public class Hands : NetworkBehaviour
{
    [SerializeField]private Transform withGunParent;
    [SerializeField]private Transform withoutGunParent;
    [SerializeField]private GameObject gun;
    [SerializeField]private GameObject shadowGun;

    private void Update() 
    {
        transform.SetLocalPositionAndRotation
        (
            Vector3.Lerp
            (
                transform.localPosition, 
                Vector3.zero, 
                5 * Time.deltaTime
            ),
            
            Quaternion.Lerp
            (
                transform.localRotation, 
                Quaternion.identity, 
                5 * Time.deltaTime
            )
        );
    }
    public void SwitchParent(bool state)
    {
        gun.SetActive(state);
        shadowGun.SetActive(state);
        if (state)
        {
            transform.parent = withGunParent;
        }
        else
        {
            transform.parent = withoutGunParent;
        }
    }
}