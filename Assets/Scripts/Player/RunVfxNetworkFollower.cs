using Unity.Netcode;
using UnityEngine;

namespace Player
{
    [DisallowMultipleComponent]
    public class RunVfxNetworkFollower : NetworkBehaviour
    {
        [SerializeField] private LayerMask groundLayerMask = ~0;
        [SerializeField] private float groundRayDistance = 4f;
        [SerializeField] private bool alignToGroundNormal = true;
        [SerializeField] private Vector3 positionOffset;

        private readonly NetworkVariable<ulong> targetNetworkObjectId = new(ulong.MaxValue);

        private NetworkObject targetNetworkObject;
        private Transform targetTransform;
        private CharacterController targetController;
        private Movement targetMovement;

        public void SetTargetNetworkObjectId(ulong networkObjectId)
        {
            if (!IsServer)
            {
                return;
            }

            targetNetworkObjectId.Value = networkObjectId;
            ResolveTarget();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolveTarget();
            UpdateFollowTransform();
        }

        private void Update()
        {
            ResolveTarget();
            UpdateFollowTransform();
        }

        private void ResolveTarget()
        {
            if (targetTransform != null || targetNetworkObjectId.Value == ulong.MaxValue || NetworkManager.Singleton == null)
            {
                return;
            }

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId.Value, out targetNetworkObject))
            {
                targetTransform = targetNetworkObject.transform;
                targetController = targetNetworkObject.GetComponent<CharacterController>();
                targetMovement = targetNetworkObject.GetComponent<Movement>();
            }
        }

        private void UpdateFollowTransform()
        {
            if (targetTransform == null)
            {
                return;
            }

            Transform originTransform = targetMovement != null ? targetMovement.RunVfxOrigin : null;
            Vector3 originPosition = originTransform != null
                ? originTransform.position
                : targetController != null
                    ? targetController.bounds.center
                    : targetTransform.position;

            Vector3 basePosition = originPosition + positionOffset;

            transform.position = basePosition;

            if (alignToGroundNormal && Physics.Raycast(basePosition + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            }
            else
            {
                transform.rotation = originTransform != null ? originTransform.rotation : targetTransform.rotation;
            }
        }
    }
}

