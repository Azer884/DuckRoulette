using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Movement : NetworkBehaviour
{
    private InputActionAsset inputActions; // Use InputActionAsset from RebindSaveLoad
    private CharacterController controller;

    // Public so TutorialManager can inject the offline rig wiring at runtime (the shared
    // script drives both the networked game and the tutorial).
    public Transform camHolder;
    [SerializeField] private GameObject secondCamHolder;
    [SerializeField] private GameObject cam;
    public float movementSpeed = 2.0f;
    public Rig spinRig;
    private float xRotation = 0f;

    [Header("Movement Variables"), Space]
    private Vector3 velocity;
    public float gravity = -9.81f;
    [Header("Jumping"), Space]
    [SerializeField] private float coyoteTime = 0.2f; // Grace period after leaving ground to still allow jump
    private float lastGroundedTime = -Mathf.Infinity;
    private bool grounded;
    public float speedMultiplier = 1.0f;
    public float jumpHeight = 1.5f;

    [Header("Ground Parry"), Space]
    [SerializeField] private bool enableGroundParry = true;
    [Tooltip("How long before touchdown a Jump press still counts as a parry attempt, in seconds.")]
    [SerializeField] private float groundParryWindow = 0.15f;
    [Tooltip("Multiplies jumpHeight for a successful parry's vertical jump.")]
    [SerializeField] private float groundParryJumpMultiplier = 1.25f;
    [Tooltip("Multiplies current horizontal velocity on a successful parry, for extra carry distance.")]
    [SerializeField] private float groundParryHorizontalMultiplier = 1.3f;
    [Tooltip("Movement speed multiplier active for groundParrySpeedBoostDuration seconds after a successful parry.")]
    [SerializeField] private float groundParrySpeedBoostMultiplier = 1.25f;
    [SerializeField] private float groundParrySpeedBoostDuration = 2f;
    [Tooltip("Minimum time between two successful parries.")]
    [SerializeField] private float groundParryCooldown = 1.5f;
    [SerializeField] private ShakeProfile groundParryShakeProfile;


    // Buffers the last Jump press regardless of grounded state, so a press made shortly before
    // touchdown still parries once the landing frame arrives.
    private float lastJumpPressedTime = -Mathf.Infinity;
    // One attempt per airtime: cleared when the player leaves the ground, not when they land,
    // so holding Jump through a single landing can't retrigger a parry.
    private bool parryConsumedThisAirtime;
    private float lastParryTime = -Mathf.Infinity;
    private float parrySpeedBoostEndTime = -Mathf.Infinity;

    [Header("Crouch Variables"), Space]
    public float initHeight;
    public float crouchHeight;
    public bool isCrouched;

    public Animator[] animators;
    public Animator handAnim;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    public GameObject legs;
    public GameObject FPShadow;
    public GameObject Hands;
    [SerializeField] private GameObject fullBody, thirdPersonCam;

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector]public float realMovementSpeed;  // To store the calculated speed
    
    public ShakeProfile jumpShakeProfile;
    private CameraShaker cameraShaker;

    
    private bool isOnIce = false; // Check if the player is on ice
    private bool isSliding = false;
    private float slideStartTime = 0f;
    private float slideEndTime = 0f; // When momentum depletes
    private float slidePunishmentDuration = 0.6f; // Extra punishment time after momentum depletes
    private Vector3 slideDirection = Vector3.zero;
    
    [SerializeField] private float iceFriction = 0.98f; // Ice friction (less than 1 for sliding)
    [SerializeField] private float slidingSpeedMultiplier = 7f; // Speed boost during tobogganing
    [SerializeField] private float slidingFriction = 0.95f; // Friction for sliding deceleration
    [SerializeField] private float slidingStopThreshold = 0.1f; // Minimum velocity to stop sliding
    [SerializeField] private float slidingHeight = 0.5f;
    public GameObject slidingCam;
    public Rig rig;

    [Header("Camera FOV"), Space]
    [SerializeField] private float walkFov = 60f;
    [SerializeField] private float runFov = 70f;
    [SerializeField] private float fovLerpSpeed = 8f;

    [Header("Run VFX"), Space]
    [SerializeField] private Transform runVfxOrigin;
    // Per-instance override: left unset in the networked game (VfxManager's default is used
    // instead), but Tutorial assigns its own offline-safe dust prefab here.
    [SerializeField] private GameObject defaultRunVfxPrefab;
    [SerializeField] private LayerMask groundLayerMask = ~0;
    [SerializeField] private float runVfxRayDistance = 1.5f;
    [SerializeField] private float runVfxSurfaceOffset = 0.02f;
    [SerializeField] private float runVfxRequestCooldown = 0.1f;

    private CinemachineCamera playerCamera;
    private float targetFov;
    private Shooting shootingComponent;
    private InputAction moveAction, lookAction, runAction, jumpAction, crouchAction;
    private GameObject activeRunVfxPrefab;
    private string activeRunSurfaceKey;
    private bool runVfxActive;
    private float lastRunVfxRequestTime;
    private GameObject spawnedRunVfxPrefab;
    private NetworkObject spawnedRunVfxObject;
    private bool isRunning;

    [Header("Ability Gating (tutorial)"), Space]
    // Tutorial gating: TutorialManager flips these off/on to lock abilities behind steps.
    // They default to true so the networked game is unaffected.
    public bool canMove = true;
    public bool canJump = true;
    public bool canCrouch = true;

    // True when this component runs without an active Netcode session (e.g. the offline
    // tutorial scene). In that mode all RPCs/NetworkVariables are skipped and everything
    // happens locally - the same code drives both modes.
    public bool IsLocalMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

    private GameObject localRunVfxInstance;

    public Transform RunVfxOrigin => runVfxOrigin;
    public bool IsSliding => isSliding;
    public bool IsRunning => isRunning;

    // Degrees per second a fully deflected stick turns the player at controller sensitivity 1.
    // See DoLooking for why gamepad look needs this and mouse look does not.
    [SerializeField] private float gamepadLookDegreesPerSecond = 180f;

    float mouseXSmooth = 0f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            ApplyRemoteVisualState();
            enabled = false;
        }
        else
        {
            // Spawn-slot positioning is a one-time, spawn-only concern (a networked-game concept -
            // the offline tutorial keeps whatever position the scene placed it at). It used to live
            // inside ApplyOwnerVisualState, but that method is also re-run later by
            // Movement.SetModelVisible(true) (HidingSpot's exit path) to restore cam/layer state -
            // which was silently teleporting the player back to their spawn slot instead of wherever
            // they actually stood before hiding.
            if (!IsLocalMode)
            {
                transform.position = new Vector3(0, 2, (int)OwnerClientId * 2);
            }

            ApplyOwnerVisualState();
        }
    }

    // Owner-side camera/layer setup. Split out of OnNetworkSpawn so the offline tutorial
    // (which never gets a NetworkObject/OnNetworkSpawn call) can run the same setup from
    // Start(), and so Movement.SetModelVisible(true) can re-run just the cam/layer part without
    // re-placing the player at their spawn slot. Every reference is optional: the tutorial
    // player doesn't carry the full set.
    private void ApplyOwnerVisualState()
    {
        if (cam != null) cam.SetActive(true);
        if (thirdPersonCam != null) thirdPersonCam.SetActive(false);
        if (camHolder != null) camHolder.gameObject.SetActive(true);
        if (secondCamHolder != null) secondCamHolder.SetActive(true);
        if (slidingCam != null) slidingCam.SetActive(false);
        if (rig != null)
        {
            rig.gameObject.SetActive(true);
            rig.weight = 1f;
        }
        if (spinRig != null) spinRig.weight = 1f;

        isSliding = false;
        isOnIce = false;
        slideStartTime = 0f;
        slideEndTime = 0f;
        slideDirection = Vector3.zero;

        if (fullBody != null) ChangeLayerRecursively(fullBody, 2);
        if (legs != null) ChangeLayerRecursively(legs, 3);
        if (FPShadow != null) ChangeLayerRecursively(FPShadow, 3);
        if (Hands != null) ChangeLayerRecursively(Hands, LayerMask.NameToLayer("Hands"));
    }

    private void ApplyRemoteVisualState()
    {
        if (cam != null) cam.SetActive(false);
        if (thirdPersonCam != null) thirdPersonCam.SetActive(true);
        if (camHolder != null) camHolder.gameObject.SetActive(false);
        if (secondCamHolder != null) secondCamHolder.SetActive(false);
        if (slidingCam != null) slidingCam.SetActive(false);
        if (rig != null) rig.gameObject.SetActive(false);
        if (fullBody != null) ChangeLayerRecursively(fullBody, 3);
        if (legs != null) ChangeLayerRecursively(legs, 2);
        if (FPShadow != null) ChangeLayerRecursively(FPShadow, 2);
        if (Hands != null) ChangeLayerRecursively(Hands, 2);
    }

    private void OnDisable()
    {
        // Offline there is no server to notify - just clean up any locally spawned VFX.
        // (Also avoids firing a ServerRpc during teardown on networked clients.)
        if (!IsLocalMode && IsOwner)
        {
            RequestRunVfxServerRpc(false, string.Empty, string.Empty, Color.white);
        }

        StopRunVfx();
        DestroyLocalRunVfx();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            StopRunVfxServer();
        }

        base.OnNetworkDespawn();
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        inputActions = GetComponent<InputSystem>().inputActions;
        cameraShaker = CameraShaker.GetOrAdd(gameObject);
        shootingComponent = GetComponent<Shooting>();
        initHeight = controller.height;
        Cursor.lockState = CursorLockMode.Locked;

        moveAction = inputActions.FindAction("Move");
        lookAction = inputActions.FindAction("Look");
        runAction = inputActions.FindAction("Run");
        jumpAction = inputActions.FindAction("Jump");
        crouchAction = inputActions.FindAction("Crouch");

        playerCamera = cam != null ? cam.GetComponentInChildren<CinemachineCamera>() : null;
        if (playerCamera == null && camHolder != null)
        {
            playerCamera = camHolder.GetComponentInChildren<CinemachineCamera>();
        }
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<CinemachineCamera>();
        }
        if (playerCamera != null)
        {
            walkFov = playerCamera.Lens.FieldOfView;
            targetFov = walkFov;
        }

        lastPosition = transform.position;

        // Offline (tutorial) mode never receives OnNetworkSpawn - apply the owner-side
        // visual state here so cameras/rig/layers end up in the same state as a networked owner.
        if (IsLocalMode)
        {
            ApplyOwnerVisualState();
        }
    }

    private void Update()
    {
        DoMovement();
        DoCrouch();
        DoLooking();
        UpdateFov();
        UpdateRunVfx(isRunning);
        Vector3 currentPos = transform.position;
        Vector3 deltaPosition = currentPos - lastPosition;
        realMovementSpeed = deltaPosition.magnitude / Time.deltaTime;
        lastPosition = currentPos;
    }

    private void UpdateFov()
    {
        if (playerCamera == null)
        {
            return;
        }

        var lens = playerCamera.Lens;
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        playerCamera.Lens = lens;
    }

    private void DoLooking()
    {
        Vector2 looking = GetPlayerLook();
        bool isGamepadLook = IsGamepadLookInput();

        float sensitivityX = 1f;
        float sensitivityY = 1f;

        if (SettingsManager.Instance != null)
        {
            if (isGamepadLook)
            {
                sensitivityX = SettingsManager.Instance.ControllerSensitivityX;
                sensitivityY = SettingsManager.Instance.ControllerSensitivityY;
            }
            else
            {
                sensitivityX = SettingsManager.Instance.MouseSensitivityX;
                sensitivityY = SettingsManager.Instance.MouseSensitivityY;
            }
        }

        // A gamepad stick reports a normalised [-1, 1] value; the mouse reports a per-frame pixel
        // delta, which is tens of units. Feeding both through the same multiplier meant a stick held
        // at full tilt turned the player about one degree per SECOND at sensitivity 1 - the whole
        // reason controller look felt unusably slow even with the setting maxed. Gamepad look is
        // scaled into degrees per second here so sensitivity 1 is a normal turn rate; the mouse path
        // is left exactly as it was so existing mouse sensitivity settings still feel the same.
        float deviceScale = isGamepadLook ? gamepadLookDegreesPerSecond : 1f;

        float lookX = looking.x * sensitivityX * deviceScale * Time.deltaTime;
        float lookY = looking.y * sensitivityY * deviceScale * Time.deltaTime;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -85f, 75f);

        camHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);

        mouseXSmooth = Mathf.Lerp(mouseXSmooth, looking.x / 20, 4 * Time.deltaTime);
        mouseXSmooth = Mathf.Clamp(mouseXSmooth, -1, 1);
    }

    private bool IsGamepadLookInput()
    {
        var activeControl = lookAction != null ? lookAction.activeControl : null;
        return activeControl != null && activeControl.device is Gamepad;
    }


// Update the OnControllerColliderHit to detect ice
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Ice"))
        {
            isOnIce = true;
        }
        else
        {
            isOnIce = false;
        }
    }

    private void DoMovement()
    {
        bool wasGrounded = grounded;
        grounded = controller.isGrounded;
        // Record the last time we were on ground to allow coyote-time jumps
        if (grounded)
        {
            lastGroundedTime = Time.time;
        }

        // Handle gravity and grounded state
        if (grounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        Vector2 movement = GetPlayerMovement();
        // Tutorial gating: with canMove off the player is rooted in place (gravity and
        // looking keep working) until TutorialManager unlocks movement.
        if (!canMove)
        {
            movement = Vector2.zero;
        }
        isRunning = runAction.ReadValue<float>() > 0 && movement.y > 0 && !isCrouched;
        speedMultiplier = isRunning ? 2.0f : 1.0f;
        targetFov = isRunning ? runFov : walkFov;

        if (isSliding)
        {
            HandleSliding();
        }
        else
        {
            Vector3 move = transform.right * movement.x + transform.forward * movement.y;
            float parryBoost = Time.time <= parrySpeedBoostEndTime ? groundParrySpeedBoostMultiplier : 1f;

            if (isOnIce)
            {
                // Ice sliding with movement control
                velocity.x = Mathf.Lerp(velocity.x, move.x * movementSpeed * speedMultiplier * parryBoost * 1.2f, Time.deltaTime * iceFriction);
                velocity.z = Mathf.Lerp(velocity.z, move.z * movementSpeed * speedMultiplier * parryBoost * 1.2f, Time.deltaTime * iceFriction);
            }
            else
            {
                // Regular movement
                velocity.x = movementSpeed * speedMultiplier * parryBoost * move.x;
                velocity.z = movementSpeed * speedMultiplier * parryBoost * move.z;
            }
        }

        // Handle jumping (allow coyote time)
        bool canUseCoyote = Time.time - lastGroundedTime <= coyoteTime;
        // Jumping is unrestricted on normal ground regardless of the gun. On ice specifically,
        // the gun holder can't jump (and so can't trigger the toboggan slide below either) -
        // only someone without the gun can jump their way off ice.
        bool jumpBlockedByGun = isOnIce && IsHoldingGun();

        if (jumpAction.triggered)
        {
            lastJumpPressedTime = Time.time;
        }

        bool justLanded = grounded && !wasGrounded;
        if (wasGrounded && !grounded)
        {
            // Left the ground - a fresh airtime gets its own single parry attempt.
            parryConsumedThisAirtime = false;
        }

        bool parryPressBuffered = Time.time - lastJumpPressedTime <= groundParryWindow;
        bool parryOffCooldown = Time.time - lastParryTime >= groundParryCooldown;
        bool tryingGroundParry = enableGroundParry && justLanded && parryPressBuffered && parryOffCooldown &&
            !parryConsumedThisAirtime && canJump && !isCrouched && !isSliding && !jumpBlockedByGun;

        if (tryingGroundParry)
        {
            PerformGroundParry();
        }
        else if (canJump && (grounded || canUseCoyote) && jumpAction.triggered && !isCrouched && !isSliding && !jumpBlockedByGun)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastGroundedTime = -Mathf.Infinity;
            if (cameraShaker != null)
            {
                cameraShaker.Shake(jumpShakeProfile);
            }

            if (isOnIce && !isSliding)
            {
                StartSliding();
            }
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        
        // Apply movement
        controller.Move(velocity * Time.deltaTime);


        // Update animator
        velocityX = Mathf.Lerp(velocityX, realMovementSpeed > 1.2 ? movement.x : 0, 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, realMovementSpeed > 1.2 ? (movement.y * speedMultiplier) : 0, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void PerformGroundParry()
    {
        lastParryTime = Time.time;
        parryConsumedThisAirtime = true;
        lastGroundedTime = -Mathf.Infinity;

        float parryJumpHeight = jumpHeight * groundParryJumpMultiplier;
        velocity.y = Mathf.Sqrt(parryJumpHeight * -2f * gravity);
        velocity.x *= groundParryHorizontalMultiplier;
        velocity.z *= groundParryHorizontalMultiplier;

        if (cameraShaker != null)
        {
            cameraShaker.Shake(groundParryShakeProfile != null ? groundParryShakeProfile : jumpShakeProfile);
        }

        if (isOnIce && !isSliding)
        {
            StartSliding();
        }

        ParryFeedbackHUD.Show();
        RequestGroundParryVfx();
    }

    private void RequestGroundParryVfx()
    {
        if (VfxManager.Instance == null || VfxManager.Instance.groundParryVfxPrefab == null)
        {
            return;
        }

        Vector3 position = GetFeetPosition();

        if (IsLocalMode)
        {
            SpawnGroundParryVfx(position);
        }
        else
        {
            RequestGroundParryVfxServerRpc(position);
        }
    }

    private Vector3 GetFeetPosition()
    {
        Vector3 origin = controller.bounds.center;
        float distance = controller.bounds.extents.y + 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }
        return transform.position - Vector3.up * (controller.height * 0.5f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestGroundParryVfxServerRpc(Vector3 position)
    {
        SpawnGroundParryVfxClientRpc(position);
    }

    [ClientRpc]
    private void SpawnGroundParryVfxClientRpc(Vector3 position)
    {
        SpawnGroundParryVfx(position);
    }

    private void SpawnGroundParryVfx(Vector3 position)
    {
        if (VfxManager.Instance == null || VfxManager.Instance.groundParryVfxPrefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(VfxManager.Instance.groundParryVfxPrefab, position, Quaternion.identity);
        instance.transform.localScale = Vector3.one * VfxManager.Instance.groundParryVfxScale;

        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear(true);
            ps.Play(true);
        }

        Destroy(instance, VfxManager.Instance.groundParryVfxLifetime);
    }

    private void HandleSliding()
    {
        float slideElapsed = Time.time - slideStartTime;
        
        // Check if slide momentum is depleted (below threshold)
        float momentumMagnitude = new Vector3(velocity.x, 0, velocity.z).magnitude;
        if (momentumMagnitude < slidingStopThreshold && slideEndTime == 0f)
        {
            slideEndTime = Time.time; // Mark when momentum ended
        }
        
        // Calculate total lock duration: (momentum duration) + punishment
        float momentumDuration = slideEndTime > 0f ? slideEndTime - slideStartTime : slideElapsed;
        float totalLockDuration = momentumDuration + slidePunishmentDuration;
        
        // End slide when total duration is reached
        if (slideElapsed >= totalLockDuration)
        {
            EndSliding();
            return;
        }
        
        // Decay the slide direction smoothly until it reaches near-zero
        float decayRate = 1.5f; // Slower decay so the slide travels farther
        slideDirection = Vector3.Lerp(slideDirection, Vector3.zero, Time.deltaTime * decayRate);
        
        velocity.x = slideDirection.x;
        velocity.z = slideDirection.z;
        
        // Apply slight downward force to keep grounded
        if (velocity.y <= 0.5f)
        {
            velocity.y -= 0.1f;
        }
    }
    
    private void StartSliding()
    {
        isSliding = true;
        slideStartTime = Time.time;
        slideEndTime = 0f; // Reset
        
        // Capture current momentum direction
        float horizontalSpeed = new Vector3(velocity.x, 0, velocity.z).magnitude;
        if (horizontalSpeed > 0.1f)
        {
            slideDirection = new Vector3(velocity.x, 0, velocity.z).normalized * horizontalSpeed * 2.5f;
        }
        else
        {
            slideDirection = transform.forward * 3.5f;
        }
        
        // Give slight upward velocity for jump effect
        velocity.y = Mathf.Sqrt(jumpHeight * 0.5f * -2f * gravity);

        controller.height = slidingHeight;
        legs.SetActive(false);
        FPShadow.SetActive(false);
        Hands.SetActive(false);

        spinRig.weight = 0f; // Disable spin rig for sliding
        
        slidingCam.SetActive(true);
    }
    
    private void EndSliding()
    {
        isSliding = false;

        rig.weight = Mathf.Lerp(rig.weight, 1, Time.deltaTime * 5f);
        legs.SetActive(true);
        FPShadow.SetActive(true);
        Hands.SetActive(true);
        slidingCam.SetActive(false);

        spinRig.weight = 1f;

        // The gun may have changed hands while sliding - that switch was deferred (see
        // Shooting.OnEnable) so the slide could finish uninterrupted. Apply it now that we're up.
        if (IsHoldingGun() && shootingComponent != null)
        {
            shootingComponent.ApplyGunHandsPose();
        }
    }

    private void UpdateRunVfx(bool isRunning)
    {
        if (!isRunning || !grounded || isSliding)
        {
            if (runVfxActive)
            {
                if (IsLocalMode)
                {
                    DestroyLocalRunVfx();
                }
                else
                {
                    RequestRunVfxServerRpc(false, string.Empty, string.Empty, Color.white);
                }
                runVfxActive = false;
            }

            StopRunVfx();
            return;
        }

        if (!TryGetGroundHit(out RaycastHit hit))
        {
            StopRunVfx();
            return;
        }

        GameObject prefab = GetRunVfxPrefab(hit);
        if (prefab == null)
        {
            if (runVfxActive)
            {
                if (IsLocalMode)
                {
                    DestroyLocalRunVfx();
                }
                else
                {
                    RequestRunVfxServerRpc(false, string.Empty, string.Empty, Color.white);
                }
                runVfxActive = false;
            }

            StopRunVfx();
            return;
        }

        if (!runVfxActive || activeRunVfxPrefab != prefab || activeRunSurfaceKey != GetSurfaceKey(hit))
        {
            if (Time.time - lastRunVfxRequestTime >= runVfxRequestCooldown)
            {
                Color groundTint = SampleGroundTint(hit);
                if (IsLocalMode)
                {
                    SpawnLocalRunVfx(prefab, groundTint);
                }
                else
                {
                    RequestRunVfxServerRpc(true, hit.collider.tag, hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.name : string.Empty, groundTint);
                }
                lastRunVfxRequestTime = Time.time;
                runVfxActive = true;
            }

            activeRunVfxPrefab = prefab;
            activeRunSurfaceKey = GetSurfaceKey(hit);
        }
    }

    // Reads the ground renderer's base color under the hit point so the walk-smoke VFX
    // can be tinted to match whatever surface the player is running on.
    private Color SampleGroundTint(RaycastHit hit)
    {
        Renderer renderer = hit.collider != null ? hit.collider.GetComponentInParent<Renderer>() : null;
        if (renderer == null || renderer.sharedMaterial == null)
        {
            return Color.white;
        }

        Material material = renderer.sharedMaterial;
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }
        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }

        return Color.white;
    }

    // Multiplies every particle system's start color by the sampled ground tint, keeping
    // each system's own alpha/fade authored in the prefab.
    private static void ApplyGroundTint(GameObject instance, Color tint)
    {
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = ps.main;
            Color baseColor = main.startColor.color;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(baseColor.r * tint.r, baseColor.g * tint.g, baseColor.b * tint.b, baseColor.a));
        }
    }

    // Offline equivalent of RequestRunVfxServerRpc: spawn the VFX locally and keep the
    // instance around until the surface changes or running stops.
    private void SpawnLocalRunVfx(GameObject prefab, Color groundTint)
    {
        if (localRunVfxInstance != null && activeRunVfxPrefab == prefab)
        {
            return;
        }

        DestroyLocalRunVfx();

        Vector3 position = runVfxOrigin != null ? runVfxOrigin.position : transform.position;
        Quaternion rotation = runVfxOrigin != null ? runVfxOrigin.rotation : transform.rotation;
        localRunVfxInstance = Instantiate(prefab, position, rotation);
        ApplyGroundTint(localRunVfxInstance, groundTint);
    }

    private void DestroyLocalRunVfx()
    {
        if (localRunVfxInstance != null)
        {
            Destroy(localRunVfxInstance);
            localRunVfxInstance = null;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRunVfxServerRpc(bool isRunning, string surfaceTag, string physicMaterialName, Color groundTint)
    {
        if (!isRunning)
        {
            StopRunVfxServer();
            return;
        }

        GameObject prefab = ResolveRunVfxPrefab(surfaceTag, physicMaterialName);
        if (prefab == null)
        {
            StopRunVfxServer();
            return;
        }

        if (spawnedRunVfxObject != null && spawnedRunVfxPrefab == prefab)
        {
            return;
        }

        StopRunVfxServer();

        GameObject instance = Instantiate(prefab, runVfxOrigin.position, runVfxOrigin.rotation);
        ApplyGroundTint(instance, groundTint);
        spawnedRunVfxPrefab = prefab;
        spawnedRunVfxObject = instance.GetComponent<NetworkObject>();

        instance.SendMessage("SetTargetNetworkObjectId", NetworkObject.NetworkObjectId, SendMessageOptions.DontRequireReceiver);

        spawnedRunVfxObject.Spawn(true);
    }

    private void StopRunVfx()
    {
        activeRunVfxPrefab = null;
        activeRunSurfaceKey = null;
    }

    private void StopRunVfxServer()
    {
        if (spawnedRunVfxObject != null)
        {
            spawnedRunVfxObject.Despawn(true);
        }

        spawnedRunVfxObject = null;
        spawnedRunVfxPrefab = null;
    }

    private GameObject ResolveRunVfxPrefab(string surfaceTag, string physicMaterialName)
    {
        return VfxManager.Instance != null
            ? VfxManager.Instance.ResolveGroundVfx(surfaceTag, physicMaterialName, defaultRunVfxPrefab)
            : defaultRunVfxPrefab;
    }

    private bool TryGetGroundHit(out RaycastHit hit)
    {
        Vector3 origin = runVfxOrigin != null ? runVfxOrigin.position : controller.bounds.center;
        origin += Vector3.up * 0.1f;

        float distance = controller.bounds.extents.y + runVfxRayDistance;
        return Physics.Raycast(origin, Vector3.down, out hit, distance, groundLayerMask, QueryTriggerInteraction.Ignore);
    }


    private GameObject GetRunVfxPrefab(RaycastHit hit)
    {
        string surfaceKey = GetSurfaceKey(hit);
        if (surfaceKey == activeRunSurfaceKey && activeRunVfxPrefab != null)
        {
            return activeRunVfxPrefab;
        }

        activeRunSurfaceKey = surfaceKey;

        return VfxManager.Instance != null
            ? VfxManager.Instance.ResolveGroundVfx(hit, defaultRunVfxPrefab)
            : defaultRunVfxPrefab;
    }

    private string GetSurfaceKey(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return string.Empty;
        }

        string tagKey = $"tag:{hit.collider.tag}";
        string materialKey = hit.collider.sharedMaterial != null ? $"mat:{hit.collider.sharedMaterial.name}" : "mat:None";
        return $"{tagKey}|{materialKey}";
    }

    private void UpdateAnimator(float xVelocity, float yVelocity)
    {
        foreach (Animator animator in animators)
        {
            animator.SetFloat("XVelocity", xVelocity);
            animator.SetFloat("YVelocity", yVelocity);
            animator.SetBool("IsGrounded", grounded);
            animator.SetBool("IsCrouched", isCrouched);
            animator.SetFloat("Turning", mouseXSmooth);
            animator.SetBool("IsSliding", isSliding);
        }
        if (handAnim == null)
        {
            return;
        }
        handAnim.SetFloat("XVelocity", xVelocity);
        handAnim.SetFloat("YVelocity", yVelocity);
        handAnim.SetBool("IsGrounded", grounded);
        handAnim.SetBool("IsSliding", isSliding);
    }

    private void DoCrouch()
    {
        // Tutorial gating: crouch stays locked until TutorialManager enables it.
        if (!canCrouch)
        {
            return;
        }

        if (!isSliding)
        {
            if (crouchAction.ReadValue<float>() > 0)
            {
                controller.height = crouchHeight;
                isCrouched = true;
            }
            else
            {
                if (!Physics.Raycast(transform.position, Vector3.up, 2.0f))
                {
                    controller.height = initHeight;
                    isCrouched = false;
                }
            }
        }
    }

    public Vector2 GetPlayerMovement()
    {
        return moveAction.ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return lookAction.ReadValue<Vector2>();
    }

    public static void ChangeLayerRecursively(GameObject currentGameObject, int newLayer)
    {
        currentGameObject.layer = newLayer;

        foreach (Transform child in currentGameObject.transform)
        {
            ChangeLayerRecursively(child.gameObject, newLayer);
        }
    }

    private bool IsHoldingGun()
    {
        return shootingComponent != null && shootingComponent.HasGun;
    }

    // Used by HidingSpot: while hidden this player must be fully invisible on every client
    // (not just the owner), which normal owner/remote visual state never needed to do.
    public void SetModelVisible(bool visible)
    {
        if (!visible)
        {
            if (fullBody != null) fullBody.SetActive(false);
            if (thirdPersonCam != null) thirdPersonCam.SetActive(false);
            if (legs != null) legs.SetActive(false);
            if (FPShadow != null) FPShadow.SetActive(false);
            if (Hands != null) Hands.SetActive(false);
            if (camHolder != null) camHolder.gameObject.SetActive(false);
            if (secondCamHolder != null) secondCamHolder.SetActive(false);
            return;
        }

        if (fullBody != null) fullBody.SetActive(true);
        if (legs != null) legs.SetActive(true);
        if (FPShadow != null) FPShadow.SetActive(true);
        if (Hands != null) Hands.SetActive(true);

        // Owner vs. remote disagree on which cam/rig should end up active - let the existing
        // per-role setup decide instead of duplicating that logic here.
        if (IsOwner) ApplyOwnerVisualState();
        else ApplyRemoteVisualState();
    }
}
