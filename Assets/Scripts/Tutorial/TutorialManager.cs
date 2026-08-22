using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;

// This is the offline (non-networked) counterpart to the real player scripts
// (Movement/Shooting/Slap/TeamUp/Interact/FootStepScript). Since the tutorial never uses
// Netcode, those NetworkBehaviour scripts can't be reused directly - the logic is mirrored
// here instead. Split across partial-class files by concern (see TutorialManager.*.cs) so it
// isn't one monolithic file, while still being a single component so every reference already
// wired up on the tutorial player prefab keeps working unchanged.
//
// MAINTENANCE WARNING: because the logic is duplicated rather than shared, any gameplay tuning
// to Movement/Shooting/Slap/TeamUp/Interact (speeds, timings, input gating) must be re-applied
// by hand to the matching TutorialManager.*.cs file, or the tutorial will silently diverge from
// real gameplay. Check both sides whenever you touch either one.
public partial class TutorialManager : MonoBehaviour
{
    public event Action OnLook, OnMove, OnSprint, OnJump, OnPickUp, OnThrow, OnShutDown, OnCrouch, OnSlide, OnSwitchToGun, OnReload, OnTrigger, OnGunShot, OnTeamUp, OnTalk, OnEndTeamUp, OnSlap;
    [HideInInspector] public bool looked, moved, sprinted, jumped, pickedUp, thrown, shutDown, crouched, slid, switchedToGun, reloaded, triggered, gunShot, teamedUp, talked, endedTeamUp, slapped;

    public InputActionAsset inputActions; // Use InputActionAsset from RebindSaveLoad
    public static TutorialManager Instance { get; private set; }
    private CharacterController controller;

    [SerializeField] private Transform camHolder;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] private float lookSensitivity = 1.0f;
    private float xRotation = 0f;

    [Header("Movement Variables"), Space]
    private Vector3 velocity;
    public float gravity = -9.81f;
    private bool grounded;
    public float speedMultiplier = 1.0f;
    [SerializeField] private float jumpHeight = 1.5f;

    [Header("Crouch Variables"), Space]
    public float initHeight;
    public float crouchHeight;
    public bool isCrouched;

    [SerializeField] private Animator[] animators;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    [SerializeField] private GameObject legs;
    [SerializeField] private GameObject FPShadow;
    [SerializeField] private GameObject Hands;

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector] public float realMovementSpeed;  // To store the calculated speed

    public CinemachineImpulseSource jumpImpulseSource;


    private bool isOnIce = false; // Check if the player is on ice
    private bool isSliding = false;
    [SerializeField] private float iceFriction = 0.98f; // Ice friction (less than 1 for sliding)
    [SerializeField] private float slidingSpeedMultiplier = 7f; // Speed boost during tobogganing
    [SerializeField] private float slidingFriction = 0.95f; // Friction for sliding deceleration
    [SerializeField] private float slidingStopThreshold = 0.1f; // Minimum velocity to stop sliding
    [SerializeField] private float slidingHeight = 0.5f;
    [SerializeField] private GameObject slidingCam;
    [SerializeField] private Rig rig;
    [SerializeField] private GameObject pauseMenu, crosshair;
    private bool isPaused = false;

    float mouseXSmooth = 0f;

    // Cached once in Start() instead of calling inputActions.FindAction(...) by string every
    // frame - mirrors the same fix applied to Movement/Shooting/Slap/TeamUp/Interact in the
    // real (networked) player scripts.
    private InputAction changeWeaponAction, pauseAction, talkAction;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        inputActions = GetComponent<InputSystem>().inputActions;
        initHeight = controller.height;
        Cursor.lockState = CursorLockMode.Locked;

        lastPosition = transform.position;

        CacheInputActions();
    }

    private void CacheInputActions()
    {
        changeWeaponAction = inputActions.FindAction("Change Weapon");
        pauseAction = inputActions.FindAction("Pause");
        CacheMovementInputActions();
        CacheShootingInputActions();
        CacheSlapInputActions();
        CacheTeamUpInputActions();
        CacheInteractInputActions();
        talkAction = inputActions.FindAction("Talk");
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
        DoLooking();
        if (looked)
        {
            DoMovement();
            PlayFootstep();
        }
        if (jumped) PickUpThrowShut();
        if (shutDown) DoCrouch();
        if (slid && canSwitch)
        {
            if (changeWeaponAction.triggered && !onlySlap)
            {
                haveGun = !haveGun;
                SwitchParent(haveGun);
            }
        }
        if (haveGun && !switchedToGun)
        {
            switchedToGun = true;
            OnSwitchToGun?.Invoke();
        }
        if (switchedToGun && haveGun)
        {
            Reload();
            Trigger();
            Shoot();
        }
        if (gunShot) TeamUp();
        if (teamedUp && talkAction.triggered)
        {
            if (!talked)
            {
                talked = true;
                OnTalk?.Invoke();
            }
        }
        if (!haveGun && endedTeamUp)
        {
            Slap();
        }

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
}
