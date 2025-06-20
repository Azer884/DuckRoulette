using UnityEngine;

public class TutoHolder : MonoBehaviour
{
    public void StopMovement()
    {
        GetComponentInChildren<TutoBot>().StopMovement();
    }
}
