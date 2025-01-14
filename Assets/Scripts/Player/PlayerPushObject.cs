using UnityEngine;
using Unity.Netcode;

public class PlayerPushObject : NetworkBehaviour
{
    [SerializeField]
    private float forceMagnitude;

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider.attachedRigidbody != null)
        {
            Vector3 hitPosition = hit.point;
            Vector3 hitColliderPosition = hit.collider.transform.position;

            ColliderHitServerRpc(transform.position, hitColliderPosition, hitPosition);
        }
    }

    [ServerRpc]
    private void ColliderHitServerRpc(Vector3 playerPosition, Vector3 hitColliderPosition, Vector3 hitPoint)
    {
        Collider hitCollider = Physics.OverlapSphere(hitPoint, 0.1f)?[0]; // Get the first collider in the overlap sphere (should be the one hit)

        if (hitCollider != null)
        {
            var rigidBody = hitCollider.attachedRigidbody;

            if (rigidBody != null)
            {
                var forceDirection = hitColliderPosition - playerPosition;
                forceDirection.y = 0;
                forceDirection.Normalize();

                rigidBody.AddForceAtPosition(forceDirection * forceMagnitude / rigidBody.mass, hitPoint, ForceMode.Impulse);
            }
        }
    }
}
