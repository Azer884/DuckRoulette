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
        // Standing on top of a body is not pushing it.
        if (hit.collider.attachedRigidbody != null && hit.normal.y < 0.7f)
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

        // First collider at the contact that belongs to a rigidbody. Taking overlaps[0] blindly
        // often picked the ground under the player's feet instead of the thing being pushed.
        Collider hitCollider = null;
        foreach (Collider overlap in Physics.OverlapSphere(hitPoint, 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (overlap.attachedRigidbody != null && !overlap.transform.IsChildOf(transform))
            {
                hitCollider = overlap;
                break;
            }
        }

        if (hitCollider != null)
        {
            var rigidBody = hitCollider.attachedRigidbody;

            // Heavy bodies like the truck model their own response to being pushed.
            if (rigidBody.TryGetComponent(out IPushable pushable))
            {
                pushable.OnPushed(OwnerClientId, playerPosition, hitPoint);
                return;
            }

            if (rigidBody.GetComponentInParent<BulletBehavior>() == null)
            {
                var forceDirection = hitCollider.transform.position - playerPosition;
                forceDirection.y = 0;
                forceDirection.Normalize();

                rigidBody.AddForceAtPosition(forceDirection * forceMagnitude / rigidBody.mass, hitPoint, ForceMode.Impulse);
            }
        }
    }
}
