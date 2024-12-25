using Unity.Netcode;
using UnityEngine;

public class MoveThePlayersBox : MonoBehaviour
{
    [SerializeField]private Vector3 pos1, pos2;
    void OnEnable()
    {
        transform.localPosition = !NetworkManager.Singleton.IsHost? pos2 : pos1;
    }
}
