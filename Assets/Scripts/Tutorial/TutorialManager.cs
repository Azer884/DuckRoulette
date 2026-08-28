using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;

// Offline tutorial orchestrator. The actual gameplay mechanics (movement, shooting,
// pickup/throw/mute) are driven by the SAME shared scripts used by the networked player
// (Movement / Shooting / Interact - see Assets/Scripts/Player). This component no longer
// duplicates any of that logic; it only:
//   1. makes sure the shared components exist on this object and injects the rig wiring
//      below into them (transitional: so the tutorial scene keeps working without any
//      editor re-wiring - long term, assign these directly on the components instead),
//   2. gates abilities behind tutorial steps by flipping Movement's ability flags and
//      enabling/disabling the Shooting/Interact components,
//   3. detects task completion (input polls + events from the shared scripts) and raises
//      the step events consumed by TutorialStepController,
//   4. implements the offline-only interactions that have no networked counterpart
//      (team-up with TutoBot, slapping the TutoBot ragdoll, footsteps audio, pause).
// Because the mechanics live in the shared scripts, any tuning there automatically
// applies to the tutorial - the old hand-mirrored copies are gone.
public partial class TutorialManager : MonoBehaviour
{
    public event Action OnLook, OnMove, OnSprint, OnJump, OnPickUp, OnThrow, OnShutDown, OnCrouch, OnSlide, OnSwitchToGun, OnReload, OnTrigger, OnGunShot, OnTeamUp, OnTalk, OnEndTeamUp, OnSlap;
    [HideInInspector] public bool looked, moved, sprinted, jumped, pickedUp, thrown, shutDown, crouched, slid, switchedToGun, reloaded, triggered, gunShot, teamedUp, talked, endedTeamUp, slapped;

    public InputActionAsset inputActions; // Use InputActionAsset from RebindSaveLoad
    public static TutorialManager Instance { get; private set; }
    private CharacterController controller;

    [Header("Shared Rig Wiring (injected into Movement/Shooting/Interact)"), Space]
    [SerializeField] private Transform camHolder;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float crouchHeight;
    [SerializeField] private Animator[] animators;
    [SerializeField] private GameObject legs;
    [SerializeField] private GameObject FPShadow;
    [SerializeField] private GameObject Hands;
    [SerializeField] private Rig rig;
    [SerializeField] private GameObject slidingCam;
    public ShakeProfile jumpShakeProfile;

    [Header("Gun Wiring (injected into Shooting)"), Space]
    public GameObject bulletPrefab, vfxPrefab;
    public Transform spawnPt;
    public Animator bulletAnimator;
    public GameObject gun, shadowGun;

    [Header("Pickup Wiring (injected into Interact)"), Space]
    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    public Transform bumBoxPickUpPosition;

    [Header("UI"), Space]
    [SerializeField] private GameObject pauseMenu, crosshair;
    private bool isPaused = false;

    // Shared components driving the real gameplay.
    private Movement movementComp;
    private Shooting shootingComp;
    private Interact interactComp;

    // Weapon-switch phase state (mirrors HideGun's "toggle Shooting.enabled" behavior).
    private bool _allowWeaponSwitch;
    private bool _onlySlap;
    private bool _gunFullyLowered;

    // Cached once in Start() instead of calling inputActions.FindAction(...) by string every frame.
    private InputAction changeWeaponAction, pauseAction, talkAction;
    private InputAction moveAction, lookAction, runAction, jumpAction, crouchAction;
    private InputAction interactAction, muteAction;

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector] public float realMovementSpeed;  // To store the calculated speed

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        EnsureSharedPlayerComponents();

        // Lock everything down until the matching step completes (see Update detection).
        movementComp.canMove = false;
        movementComp.canJump = false;
        movementComp.canCrouch = false;
        interactComp.enabled = false;
        shootingComp.enabled = false;
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        inputActions = GetComponent<InputSystem>().inputActions;
        Cursor.lockState = CursorLockMode.Locked;

        lastPosition = transform.position;
        CacheInputActions();
        SubscribeSharedComponentEvents();
    }

    // Adds (once) the same components the networked player uses, then transfers the wiring
    // that lives on this component's inspector onto them.
    private void EnsureSharedPlayerComponents()
    {
        movementComp = GetComponent<Movement>();
        if (movementComp == null) movementComp = gameObject.AddComponent<Movement>();

        interactComp = GetComponent<Interact>();
        if (interactComp == null) interactComp = gameObject.AddComponent<Interact>();

        // Added after Movement because Shooting.Awake resolves its sibling Movement.
        shootingComp = GetComponent<Shooting>();
        if (shootingComp == null) shootingComp = gameObject.AddComponent<Shooting>();

        movementComp.camHolder = camHolder;
        movementComp.movementSpeed = movementSpeed;
        movementComp.jumpHeight = jumpHeight;
        movementComp.crouchHeight = crouchHeight;
        movementComp.animators = animators;
        movementComp.legs = legs;
        movementComp.FPShadow = FPShadow;
        movementComp.Hands = Hands;
        movementComp.rig = rig;
        movementComp.spinRig = rig;
        movementComp.slidingCam = slidingCam;
        movementComp.jumpShakeProfile = jumpShakeProfile;

        shootingComp.bulletPrefab = bulletPrefab;
        shootingComp.vfxPrefab = vfxPrefab;
        shootingComp.spawnPt = spawnPt;
        shootingComp.bulletAnimator = bulletAnimator;
        shootingComp.gun = gun;
        shootingComp.animators = animators;
        shootingComp.fPHands = Hands != null ? Hands.GetComponent<Hands>() : null;

        interactComp.shooting = shootingComp;
        interactComp.pickUpLayerMask = pickUpLayerMask;
        interactComp.maxDistance = maxDistance;
        interactComp.bumBoxPickUpPosition = bumBoxPickUpPosition;
    }

    private void SubscribeSharedComponentEvents()
    {
        interactComp.ObjectPickedUp += HandleObjectPickedUp;
        interactComp.ObjectDropped += HandleObjectDropped;
        OfflineBumBox.MuteToggled += HandleBumBoxMuteToggled;
        shootingComp.OnReloaded += HandleGunReloaded;
        shootingComp.OnTriggered += HandleGunTriggered;
        shootingComp.OnGunShot += HandleGunShotFired;
    }

    private void CacheInputActions()
    {
        moveAction = inputActions.FindAction("Move");
        lookAction = inputActions.FindAction("Look");
        runAction = inputActions.FindAction("Run");
        jumpAction = inputActions.FindAction("Jump");
        crouchAction = inputActions.FindAction("Crouch");
        changeWeaponAction = inputActions.FindAction("Change Weapon");
        pauseAction = inputActions.FindAction("Pause");
        interactAction = inputActions.FindAction("Interact"); // reused by the TeamUp partial
        muteAction = inputActions.FindAction("Mute");
        talkAction = inputActions.FindAction("Talk");

        CacheSlapInputActions();
        CacheTeamUpInputActions();
    }

    public void Pause(bool state)
    {
        pauseMenu.SetActive(state);
        crosshair.SetActive(!state);
        if (state)
        {
            Cursor.lockState = CursorLockMode.None; // Unlock the cursor
            Time.timeScale = 0; // Pause the game
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked; // Lock the cursor again
            Time.timeScale = 1; // Resume the game
        }
    }

    private void Update()
    {
        DetectLook();
        if (looked)
        {
            PlayFootstep();
        }
        DetectMovement();
        DetectJump();
        DetectCrouch();
        DetectSlide();
        HandleWeaponSwitching();
        HandlePostGunPhases();

        if (pauseAction.triggered)
        {
            isPaused = !isPaused;
            Pause(isPaused);
        }

        Vector3 currentPos = transform.position;
        Vector3 deltaPosition = currentPos - lastPosition;
        realMovementSpeed = deltaPosition.magnitude / Time.deltaTime;
        lastPosition = currentPos;
    }

    // ------------------ Step detection (raises the events TutorialStepController listens to) ------------------

    private void DetectLook()
    {
        if (looked || lookAction == null || lookAction.ReadValue<Vector2>().magnitude <= 0.1f)
        {
            return;
        }

        looked = true;
        movementComp.canMove = true; // Step complete -> unlock movement
        OnLook?.Invoke();
    }

    private void DetectMovement()
    {
        Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        if (!moved && looked && move.magnitude > 0.1f)
        {
            moved = true;
            OnMove?.Invoke();
        }
        else if (!sprinted && moved && runAction.ReadValue<float>() > 0f && move.y > 0f && !movementComp.isCrouched)
        {
            sprinted = true;
            movementComp.canJump = true; // Step complete -> unlock jumping
            OnSprint?.Invoke();
        }
    }

    private void DetectJump()
    {
        if (jumped || !sprinted || !jumpAction.triggered)
        {
            return;
        }

        jumped = true;
        interactComp.enabled = true; // Step complete -> unlock picking things up
        OnJump?.Invoke();
    }

    private void DetectCrouch()
    {
        if (crouched || !shutDown || !crouchAction.triggered)
        {
            return;
        }

        crouched = true;
        OnCrouch?.Invoke();
    }

    private void DetectSlide()
    {
        if (slid || !movementComp.IsSliding)
        {
            return;
        }

        slid = true;
        _allowWeaponSwitch = true; // Step complete -> unlock the weapon switch
        OnSlide?.Invoke();
    }

    private void HandleWeaponSwitching()
    {
        if (!_allowWeaponSwitch || _onlySlap || changeWeaponAction == null || !changeWeaponAction.triggered)
        {
            return;
        }

        // Same mechanism as the networked game (HideGun): switching = toggling Shooting.
        SwitchWeapon(!shootingComp.enabled);

        if (shootingComp.enabled && !switchedToGun)
        {
            switchedToGun = true;
            OnSwitchToGun?.Invoke();
        }
    }

    private void SwitchWeapon(bool state)
    {
        shootingComp.enabled = state;

        // Fallback for rigs without a Hands component: toggle the gun meshes directly.
        if (shootingComp.fPHands == null)
        {
            if (gun != null) gun.SetActive(state);
            if (shadowGun != null) shadowGun.SetActive(state);
        }
    }

    private void HandlePostGunPhases()
    {
        if (!gunShot)
        {
            return;
        }

        // Team-up flow (offline-only, see TutorialManager.TeamUp.cs).
        TeamUp();

        if (teamedUp && talkAction != null && talkAction.triggered && !talked)
        {
            talked = true;
            OnTalk?.Invoke();
        }

        // Slapping unlocks once the team-up is over and the gun is gone.
        if (_gunFullyLowered && endedTeamUp)
        {
            Slap();
        }
    }

    // ------------------ Shared-script event handlers ------------------

    private void HandleObjectPickedUp()
    {
        if (pickedUp)
        {
            return;
        }
        pickedUp = true;
        OnPickUp?.Invoke();
    }

    private void HandleObjectDropped()
    {
        if (thrown)
        {
            return;
        }
        thrown = true;
        OnThrow?.Invoke();
    }

    private void HandleBumBoxMuteToggled()
    {
        if (shutDown)
        {
            return;
        }

        shutDown = true;
        movementComp.canCrouch = true; // Step complete -> unlock crouching
        OnShutDown?.Invoke();
    }

    private void HandleGunReloaded()
    {
        if (reloaded)
        {
            return;
        }
        reloaded = true;
        OnReload?.Invoke();
    }

    private void HandleGunTriggered()
    {
        if (triggered)
        {
            return;
        }
        triggered = true;
        OnTrigger?.Invoke();
    }

    private void HandleGunShotFired()
    {
        if (gunShot)
        {
            return;
        }

        gunShot = true;
        OnGunShot?.Invoke();
        StartCoroutine(LowerGunWithDelay(2f));
    }

    // Mirrors the old offline behavior: right after the shot the gun comes off so the
    // team-up/slap phases take over, and the weapon switch stays locked from then on.
    private IEnumerator LowerGunWithDelay(float delay = 1f)
    {
        yield return new WaitForSeconds(delay);

        SwitchWeapon(false);
        _onlySlap = true;
        _gunFullyLowered = true;
    }

    private void OnDestroy()
    {
        if (interactComp != null)
        {
            interactComp.ObjectPickedUp -= HandleObjectPickedUp;
            interactComp.ObjectDropped -= HandleObjectDropped;
        }
        OfflineBumBox.MuteToggled -= HandleBumBoxMuteToggled;

        if (shootingComp != null)
        {
            shootingComp.OnReloaded -= HandleGunReloaded;
            shootingComp.OnTriggered -= HandleGunTriggered;
            shootingComp.OnGunShot -= HandleGunShotFired;
        }
    }
}
