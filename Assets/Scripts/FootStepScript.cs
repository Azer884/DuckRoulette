using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using UnityEngine;
using Player;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
    public AudioSource footstepSource;
    private CharacterController controller;
    public ShakeProfile walkShakeProfile;
    public ShakeProfile runShakeProfile;
    private CameraShaker cameraShaker;

    [Header("Footstep VFX")]
    [SerializeField] private LayerMask groundLayerMask = ~0;
    [SerializeField] private float groundRayDistance = 1.25f;
    [SerializeField] private float surfaceOffset = 0.02f;
    [SerializeField] private float vfxLifetime = 1.25f;

    #region Input Things
    private InputActionAsset inputActions;
    private InputAction moveAction;
    private void Awake()
    {
        controller = movement.GetComponent<CharacterController>();
        // A hardcoded transform.parent.parent assumed one specific hierarchy depth (Player.prefab's)
        // and NRE'd wherever FootStepScript sits at a different depth (e.g. the Tutorial rig's
        // extra "Rig" grouping object) - search upward for whichever ancestor actually has it.
        inputActions = GetComponentInParent<InputSystem>().inputActions;
        moveAction = inputActions.FindAction("Move");
        cameraShaker = CameraShaker.GetOrAdd(movement.gameObject);
    }
    #endregion

    // NetworkVariable to track whether the player is walking
    private NetworkVariable<bool> isWalking = new(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    void Update () {
        // Adjust step rate based on movement speed
        if (IsOwner)
        {
            if (movement.speedMultiplier > 1) {
                stepRate = 0.35f;
            } else {
                stepRate = 0.5f;
            }
    
            stepCoolDown -= Time.deltaTime;
    
            // Determine if the player is walking
            isWalking.Value =
                (moveAction.ReadValue<Vector2>() != Vector2.zero)
                && movement.realMovementSpeed > 1.2f  // Use movement speed instead of velocity magnitude
                && controller.isGrounded;
        }

        // Only the owning player can trigger their own footsteps
        if (isWalking.Value && stepCoolDown < 0f) 
        {
            if (IsOwner)
            {
                if (cameraShaker != null)
                {
                    bool isRunning = movement != null && movement.speedMultiplier > 1f;
                    cameraShaker.Shake(isRunning ? runShakeProfile : walkShakeProfile);
                }

                SpawnFootstepVfx();
            }
            PlayFootstep();
            stepCoolDown = stepRate;
        }
    }

    private void PlayFootstep()
    {
        if (footstepSource == null || SFXManager.Instance == null)
        {
            return;
        }

        AudioClip clip = SFXManager.Instance.RandomFootstep();
        if (clip == null) return;

        footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
        footstepSource.PlayOneShot(clip, 0.9f);
    }

    private void SpawnFootstepVfx()
    {
        if (!TryGetGroundHit(out RaycastHit hit))
        {
            return;
        }

        GameObject prefab = ResolveFootstepVfxPrefab(hit);
        if (prefab == null)
        {
            return;
        }

        SpawnFootstepVfxServerRpc(hit.collider.tag, hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.name : string.Empty, hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal));
    }

    [ServerRpc]
    private void SpawnFootstepVfxServerRpc(string surfaceTag, string physicMaterialName, Vector3 position, Quaternion rotation)
    {
        GameObject prefab = ResolveFootstepVfxPrefab(surfaceTag, physicMaterialName);
        if (prefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(prefab, position + Vector3.up * surfaceOffset, rotation);
        if (!instance.TryGetComponent(out NetworkObject networkObject))
        {
            Destroy(instance);
            return;
        }

        networkObject.Spawn(true);

        if (instance.TryGetComponent(out NetworkFootstepVfx footstepVfx))
        {
            footstepVfx.SetLifetime(vfxLifetime);
        }
    }

    private bool TryGetGroundHit(out RaycastHit hit)
    {
        if (controller == null)
        {
            hit = default;
            return false;
        }

        Vector3 origin = controller.bounds.center + Vector3.up * 0.1f;
        float distance = controller.bounds.extents.y + groundRayDistance;
        return Physics.Raycast(origin, Vector3.down, out hit, distance, groundLayerMask, QueryTriggerInteraction.Ignore);
    }

    private GameObject ResolveFootstepVfxPrefab(RaycastHit hit)
    {
        return VfxManager.Instance != null ? VfxManager.Instance.ResolveGroundVfx(hit) : null;
    }

    private GameObject ResolveFootstepVfxPrefab(string surfaceTag, string physicMaterialName)
    {
        return VfxManager.Instance != null ? VfxManager.Instance.ResolveGroundVfx(surfaceTag, physicMaterialName) : null;
    }

}
