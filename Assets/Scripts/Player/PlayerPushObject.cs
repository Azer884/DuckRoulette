using UnityEngine;
using Unity.Netcode;

public class PlayerPushObject : NetworkBehaviour
{
    [SerializeField]
    private float forceMagnitude;

    private const float MaxPushReach = 4f;

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
        // Every vector used to be trusted: a modified client could shove any rigidbody anywhere on
        // the map (in-flight bullets included). Contact range of the server's own view of this
        // player only, and the push direction is derived server-side.
        if (!RpcValidation.IsWithinDistance(hitPoint, transform.position, MaxPushReach))
        {
            return;
        }

        playerPosition = transform.position;

        Collider[] overlaps = Physics.OverlapSphere(hitPoint, 0.1f);
        Collider hitCollider = overlaps.Length > 0 ? overlaps[0] : null; // Get the first collider in the overlap sphere (should be the one hit)

        if (hitCollider != null)
        {
            var rigidBody = hitCollider.attachedRigidbody;

            if (rigidBody != null && rigidBody.GetComponentInParent<BulletBehavior>() == null)
            {
                var forceDirection = hitCollider.transform.position - playerPosition;
                forceDirection.y = 0;
                forceDirection.Normalize();

                rigidBody.AddForceAtPosition(forceDirection * forceMagnitude / rigidBody.mass, hitPoint, ForceMode.Impulse);
            }
        }
    }
}
