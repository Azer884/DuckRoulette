using System.Collections.Generic;
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
    
    [SerializeField] private Transform camHolder;
    [SerializeField] private GameObject secondCamHolder;
    [SerializeField] private GameObject cam;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] public float lookSensitivity = 1.0f;
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
    [SerializeField] private Animator handAnim;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    [SerializeField] private GameObject legs;
    [SerializeField] private GameObject FPShadow;
    [SerializeField] private GameObject Hands;
    [SerializeField] private GameObject fullBody, thirdPersonCam;

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector]public float realMovementSpeed;  // To store the calculated speed
    
    public CinemachineImpulseSource jumpImpulseSource;

    
    private bool isOnIce = false; // Check if the player is on ice
    private bool isSliding = false;
    [SerializeField] private float slidingFriction = 0.95f;    
    [SerializeField] private float iceFriction = 0.90f;        
    [SerializeField] private float slidingSpeedMultiplier = 4f;
    [SerializeField] private float slidingStopThreshold = 0.5f;

    [SerializeField] private float slidingHeight = 0.5f;
    [SerializeField] private GameObject slidingCam;
    [SerializeField] private Rig rig;

    [SerializeField] private float fallMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;
    [SerializeField] private float coyoteTime = 0.2f;
    [SerializeField] private float jumpBufferTime = 0.2f;

    private float coyoteTimeCounter;
    private float jumpBufferCounter;

    float mouseXSmooth = 0f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            cam.SetActive(false);
            thirdPersonCam.SetActive(true);
            camHolder.gameObject.SetActive(false);
            secondCamHolder.SetActive(false);
            slidingCam.SetActive(false);
            rig.gameObject.SetActive(false);
            ChangeLayerRecursively(fullBody, 3);
            ChangeLayerRecursively(legs, 2);
            ChangeLayerRecursively(FPShadow, 2);
            ChangeLayerRecursively(Hands, 2);
            enabled = false;
        }
        else
        {
            transform.position = new Vector3(0, 2, (int)OwnerClientId * 2);
            cam.SetActive(true);
            thirdPersonCam.SetActive(false);
            camHolder.gameObject.SetActive(true);
            secondCamHolder.SetActive(true);
            slidingCam.SetActive(true);
            rig.gameObject.SetActive(true);
            ChangeLayerRecursively(fullBody, 2);
            ChangeLayerRecursively(legs, 3);
            ChangeLayerRecursively(FPShadow, 3);
            ChangeLayerRecursively(Hands, LayerMask.NameToLayer("Hands"));
        }
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        inputActions = GetComponent<InputSystem>().inputActions;
        initHeight = controller.height;
        Cursor.lockState = CursorLockMode.Locked;

        lastPosition = transform.position;
    }

    private void Update()
    {
        DoMovement();
        DoCrouch();
        DoLooking();
        Vector3 currentPos = transform.position;
        Vector3 deltaPosition = currentPos - lastPosition;
        realMovementSpeed = deltaPosition.magnitude / Time.deltaTime;
        lastPosition = currentPos;
    }

    private void DoLooking()
    {
        Vector2 looking = GetPlayerLook();
        float lookX = looking.x * lookSensitivity * Time.deltaTime;
        float lookY = looking.y * lookSensitivity * Time.deltaTime;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -85f, 75f);

        camHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);

        mouseXSmooth = Mathf.Lerp(mouseXSmooth, looking.x / 20, 4 * Time.deltaTime);
        mouseXSmooth = Mathf.Clamp(mouseXSmooth, -1, 1);
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
        grounded = controller.isGrounded;

        // === COYOTE TIME ===
        if (grounded)
        {
            coyoteTimeCounter = coyoteTime;
            if (velocity.y < 0)
                velocity.y = -2f;
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;
        }

        // === JUMP BUFFERING ===
        if (inputActions.FindAction("Jump").triggered)
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }

        // === INPUT MOVEMENT ===
        Vector2 input = GetPlayerMovement();
        Vector3 move = transform.right * input.x + transform.forward * input.y;

        // Sprint logic
        if (inputActions.FindAction("Run").ReadValue<float>() > 0 && input.y > 0 && !isCrouched)
        {
            speedMultiplier = 2.0f;
        }
        else
        {
            speedMultiplier = 1.0f;
        }

        // === MOVEMENT LOGIC (air vs ground) ===
        if (isSliding && isOnIce)
        {
            HandleSliding();
        }
        else
        {
            EndSliding();

            if (isOnIce)
            {
                velocity.x = Mathf.Lerp(velocity.x, move.x * movementSpeed * speedMultiplier * 1.2f, Time.deltaTime * iceFriction);
                velocity.z = Mathf.Lerp(velocity.z, move.z * movementSpeed * speedMultiplier * 1.2f, Time.deltaTime * iceFriction);
            }
            else
            {
                float airControl = grounded ? 1f : 0.3f; // limit air movement

                Vector3 targetVelocity = move * movementSpeed * speedMultiplier;
                velocity.x = Mathf.Lerp(velocity.x, targetVelocity.x, Time.deltaTime * 10f * airControl);
                velocity.z = Mathf.Lerp(velocity.z, targetVelocity.z, Time.deltaTime * 10f * airControl);
            }
        }

        // === JUMP ===
        if (jumpBufferCounter > 0 && coyoteTimeCounter > 0 && !isCrouched && !isSliding)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;

            jumpImpulseSource.GenerateImpulse();
            if (isOnIce && !isSliding)
            {
                StartSliding();
            }
        }

        // === CUSTOM GRAVITY ===

        if (velocity.y < 0)
        {
            velocity.y += gravity * fallMultiplier * Time.deltaTime;
        }
        else
        {
            velocity.y += gravity * lowJumpMultiplier * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);

        // === ANIMATOR ===
        velocityX = Mathf.Lerp(velocityX, realMovementSpeed > 1.2 ? input.x : 0, 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, realMovementSpeed > 1.2 ? (input.y * speedMultiplier) : 0, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void HandleSliding()
    {
        if (velocity.y <= 0.5f)
        {
            Vector2 input = GetPlayerMovement();
            Vector3 inputDir = transform.right * input.x + transform.forward * input.y;
            inputDir.y = 0;

            Vector2 flatVel = new Vector2(velocity.x, velocity.z);
            float flatSpeed = flatVel.magnitude;

            // === STRONG ACCELERATION on input ===
            if (inputDir.magnitude > 0.1f)
            {
                Vector3 target = inputDir.normalized * movementSpeed * slidingSpeedMultiplier;

                float accelerationStrength = 20f; // Stronger = faster acceleration
                velocity.x = Mathf.MoveTowards(velocity.x, target.x, accelerationStrength * Time.deltaTime);
                velocity.z = Mathf.MoveTowards(velocity.z, target.z, accelerationStrength * Time.deltaTime);
            }
            else
            {
                // === NATURAL DECELERATION when no input ===
                float decel = 0.985f; // Closer to 1 = slower deceleration
                velocity.x *= decel;
                velocity.z *= decel;
            }

            // === STOP SLIDING when speed too low ===
            if (flatSpeed < slidingStopThreshold)
            {
                EndSliding();
                return;
            }

            rig.weight = Mathf.Lerp(rig.weight, 0.1f, Time.deltaTime * 5f);
        }
    }



    private void StartSliding()
    {
        isSliding = true;

        controller.height = slidingHeight;
        legs.SetActive(false);
        FPShadow.SetActive(false);
        Hands.SetActive(false);
        GetComponent<Shooting>().enabled = false;
        slidingCam.SetActive(true);
    }

    private void EndSliding()
    {
        isSliding = false;
        slidingSpeedMultiplier = 2.5f;

        rig.weight = Mathf.Lerp(rig.weight, 1, Time.deltaTime * 5f);
        legs.SetActive(true);
        FPShadow.SetActive(true);
        Hands.SetActive(true);
        slidingCam.SetActive(false);
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
        handAnim.SetFloat("XVelocity", xVelocity);
        handAnim.SetFloat("YVelocity", yVelocity);
        handAnim.SetBool("IsGrounded", grounded);
        handAnim.SetBool("IsSliding", isSliding);
    }

    private void DoCrouch()
    {
        if (!isSliding)
        {
            if (inputActions.FindAction("Crouch").ReadValue<float>() > 0)
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
        return inputActions.FindAction("Move").ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return inputActions.FindAction("Look").ReadValue<Vector2>();
    }

    public static void ChangeLayerRecursively(GameObject currentGameObject, int newLayer)
    {
        currentGameObject.layer = newLayer;

        foreach (Transform child in currentGameObject.transform)
        {
            ChangeLayerRecursively(child.gameObject, newLayer);
        }
    }
}
