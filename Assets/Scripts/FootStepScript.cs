using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using UnityEngine;
using Player;

public class FootStepScript : NetworkBehaviour {
    [System.Serializable]
    public class SurfaceFootstepVfxEntry
    {
        public string surfaceTag;
        public string physicMaterialName;
        public GameObject vfxPrefab;
    }

    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    private CharacterController controller;
    public CinemachineImpulseSource impulseSource;
    private NoiseHandler noiseHandler;

    [Header("Footstep VFX")]
    [SerializeField] private GameObject defaultFootstepVfxPrefab;
    [SerializeField] private List<SurfaceFootstepVfxEntry> footstepVfxBySurface = new();
    [SerializeField] private LayerMask groundLayerMask = ~0;
    [SerializeField] private float groundRayDistance = 1.25f;
    [SerializeField] private float surfaceOffset = 0.02f;
    [SerializeField] private float vfxLifetime = 1.25f;

    #region Input Things
    private InputActionAsset inputActions;
    private void Awake()
    {
        controller = movement.GetComponent<CharacterController>();
        inputActions = transform.parent.parent.GetComponent<InputSystem>().inputActions;
        noiseHandler = GetComponent<NoiseHandler>() ?? movement.GetComponent<NoiseHandler>();
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
                (inputActions.FindAction("Move").ReadValue<Vector2>() != Vector2.zero)
                && movement.realMovementSpeed > 1.2f  // Use movement speed instead of velocity magnitude
                && controller.isGrounded;
        }

        // Only the owning player can trigger their own footsteps
        if (isWalking.Value && stepCoolDown < 0f) 
        {
            if (IsOwner)
            {
                if (noiseHandler != null)
                {
                    noiseHandler.TriggerStepShake(movement != null && movement.speedMultiplier > 1f);
                }
                else if (impulseSource != null)
                {
                    impulseSource.GenerateImpulse();
                }

                SpawnFootstepVfx();
            }
            PlayFootstep();
            stepCoolDown = stepRate;
        }
    }

    private void PlayFootstep() 
    {
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0)
        {
            return;
        }

        footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
        int index = Random.Range(0, footstepClips.Length);
        footstepSource.PlayOneShot(footstepClips[index], 0.9f);
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
        for (int i = 0; i < footstepVfxBySurface.Count; i++)
        {
            SurfaceFootstepVfxEntry entry = footstepVfxBySurface[i];
            if (entry == null || entry.vfxPrefab == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.surfaceTag) && hit.collider.CompareTag(entry.surfaceTag))
            {
                return entry.vfxPrefab;
            }

            if (!string.IsNullOrWhiteSpace(entry.physicMaterialName) && hit.collider.sharedMaterial != null &&
                string.Equals(hit.collider.sharedMaterial.name, entry.physicMaterialName, System.StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }
        }

        return defaultFootstepVfxPrefab;
    }

    private GameObject ResolveFootstepVfxPrefab(string surfaceTag, string physicMaterialName)
    {
        for (int i = 0; i < footstepVfxBySurface.Count; i++)
        {
            SurfaceFootstepVfxEntry entry = footstepVfxBySurface[i];
            if (entry == null || entry.vfxPrefab == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(surfaceTag) && !string.IsNullOrWhiteSpace(entry.surfaceTag) &&
                string.Equals(surfaceTag, entry.surfaceTag, System.StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }

            if (!string.IsNullOrWhiteSpace(physicMaterialName) && !string.IsNullOrWhiteSpace(entry.physicMaterialName) &&
                string.Equals(physicMaterialName, entry.physicMaterialName, System.StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }
        }

        return defaultFootstepVfxPrefab;
    }

}
