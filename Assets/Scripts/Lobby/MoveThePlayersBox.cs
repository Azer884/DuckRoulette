using UnityEngine;

public class MoveThePlayersBox : MonoBehaviour
{
    [SerializeField]private Vector3 pos1, pos2;
    void OnEnable()
    {
        transform.localPosition = !LobbyManager.instance.isHost? pos2 : pos1;
    }
}
